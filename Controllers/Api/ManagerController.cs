using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.DTOs;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Manager")]
    public class ManagerController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;
        private readonly ILogger<ManagerController> _logger;

        public ManagerController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            ILogger<ManagerController> logger)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _logger = logger;
        }

        // GET: api/Manager/applications
        [HttpGet("applications")]
        public async Task<IActionResult> GetApplications([FromQuery] ApplicationStatus? status = null)
        {
            var query = _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(sa => sa.Status == status.Value);
            }
            else
            {
                // Managers typically see Pending, UnderReview, Incomplete, Rejected
                query = query.Where(sa => sa.Status == ApplicationStatus.Pending ||
                                        sa.Status == ApplicationStatus.UnderReview ||
                                        sa.Status == ApplicationStatus.Incomplete ||
                                        sa.Status == ApplicationStatus.Rejected);
            }

            var applications = await query
                .OrderByDescending(sa => sa.SubmittedAt)
                .ToListAsync();

            return Ok(applications.Select(MapToDto));
        }

        // GET: api/Manager/applications/{id}
        [HttpGet("applications/{id}")]
        public async Task<IActionResult> GetApplication(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Include(sa => sa.ReviewedByManager)
                .Include(sa => sa.ApprovedByAdmin)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            // Managers can only view applications that are not yet fully approved by admin
            if (application.Status == ApplicationStatus.Approved && application.ApprovedByAdminId != null)
            {
                return StatusCode(403, new { message = "This application has already been approved by an Admin." });
            }

            return Ok(MapToDto(application));
        }

        // POST: api/Manager/message-student-by-email
        [HttpPost("message-student-by-email")]
        public async Task<IActionResult> MessageStudentByEmail([FromBody] MessageStudentByEmailDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Find student by email
            var student = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Email == dto.StudentEmail);

            if (student == null)
            {
                // Try to find by user email
                var user = await _userManager.FindByEmailAsync(dto.StudentEmail);
                if (user == null)
                    return NotFound(new { message = "Student not found with the provided email" });

                // Check if user is a student
                var roles = await _userManager.GetRolesAsync(user);
                if (!roles.Contains("Student"))
                    return BadRequest(new { message = "The provided email does not belong to a student" });

                student = await _context.StudentApplications
                    .FirstOrDefaultAsync(sa => sa.ApplicationUserId == user.Id);
            }

            if (student == null)
                return NotFound(new { message = "Student application not found" });

            var managerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (managerId == null)
                return Unauthorized();
            
            var manager = await _userManager.FindByIdAsync(managerId);
            if (manager == null)
                return Unauthorized();

            // Create message
            var message = new Message
            {
                SenderId = managerId,
                ReceiverId = student.ApplicationUserId,
                StudentId = student.StudentId,
                Content = dto.Message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Send email notification
            var emailSubject = $"Message from Student Charity Hub Manager";
            var emailBody = $"Dear {student.FullName},\n\n" +
                          $"You have received a message from {manager.FirstName} {manager.LastName} (Manager):\n\n" +
                          $"{dto.Message}\n\n" +
                          $"Please log in to your account to respond.\n\n" +
                          $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(student.Email, emailSubject, emailBody);

            // Send in-app notification
            await _notificationService.SendNotificationAsync(
                student.ApplicationUserId,
                "New Message from Manager",
                $"You have received a message from {manager.FirstName} {manager.LastName}",
                "Info",
                $"/messages/conversation/{managerId}"
            );

            _logger.LogInformation("Manager {ManagerId} sent message to student {StudentEmail}", managerId, dto.StudentEmail);

            return Ok(new { message = "Message sent successfully", messageId = message.Id });
        }

        // PUT: api/Manager/applications/{id}/review
        [HttpPut("applications/{id}/review")]
        public async Task<IActionResult> MarkUnderReview(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.Pending)
            {
                return BadRequest(new { message = "Only pending applications can be marked for review." });
            }

            var managerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            application.Status = ApplicationStatus.UnderReview;
            application.ReviewedByManagerId = managerId;
            application.ReviewedByManagerAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} marked UnderReview by Manager {ManagerId}", id, managerId);

            // Notify Admin that application is ready for final approval
            await NotifyAdmins(application);

            return Ok(MapToDto(application));
        }

        // PUT: api/Manager/applications/{id}/mark-incomplete
        [HttpPut("applications/{id}/mark-incomplete")]
        public async Task<IActionResult> MarkIncomplete(int id, [FromBody] ApplicationActionDto dto)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status == ApplicationStatus.Approved || application.Status == ApplicationStatus.Rejected)
            {
                return BadRequest(new { message = "Application has already been processed." });
            }

            application.Status = ApplicationStatus.Incomplete;
            application.RejectionReason = dto.Reason;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} marked Incomplete by Manager {ManagerId}", id, User.FindFirstValue(ClaimTypes.NameIdentifier));

            await NotifyStudentIncomplete(application);

            return Ok(MapToDto(application));
        }

        // PUT: api/Manager/applications/{id}/reject
        [HttpPut("applications/{id}/reject")]
        public async Task<IActionResult> RejectApplication(int id, [FromBody] ApplicationActionDto dto)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status == ApplicationStatus.Approved || application.Status == ApplicationStatus.Rejected)
            {
                return BadRequest(new { message = "Application has already been processed." });
            }

            application.Status = ApplicationStatus.Rejected;
            application.RejectionReason = dto.Reason;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} rejected by Manager {ManagerId}", id, User.FindFirstValue(ClaimTypes.NameIdentifier));

            await NotifyStudentRejection(application);

            return Ok(MapToDto(application));
        }

        // DELETE: api/Manager/applications/{id}
        [HttpDelete("applications/{id}")]
        public async Task<IActionResult> DeleteRejectedApplication(int id)
        {
            var application = await _context.StudentApplications.FindAsync(id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.Rejected)
            {
                return BadRequest(new { message = "Only rejected applications can be deleted by a manager." });
            }

            _context.StudentApplications.Remove(application);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Rejected application {ApplicationId} deleted by Manager {ManagerId}", id, User.FindFirstValue(ClaimTypes.NameIdentifier));

            return Ok(new { message = "Application deleted successfully." });
        }

        private StudentApplicationDto MapToDto(StudentApplication application)
        {
            List<string>? documentUrls = null;
            if (!string.IsNullOrEmpty(application.ProofDocuments))
            {
                try
                {
                    documentUrls = JsonSerializer.Deserialize<List<string>>(application.ProofDocuments);
                }
                catch { }
            }

            List<string>? galleryImageUrls = null;
            if (!string.IsNullOrEmpty(application.GalleryImages))
            {
                try
                {
                    galleryImageUrls = JsonSerializer.Deserialize<List<string>>(application.GalleryImages);
                }
                catch { }
            }

            return new StudentApplicationDto
            {
                Id = application.Id,
                ApplicationUserId = application.ApplicationUserId,
                FullName = application.FullName,
                Age = application.Age,
                PlaceOfBirth = application.PlaceOfBirth,
                CurrentResidency = application.CurrentResidency,
                Email = application.Email,
                PhoneNumber = application.PhoneNumber,
                FatherName = application.FatherName,
                MotherName = application.MotherName,
                ParentsAnnualSalary = application.ParentsAnnualSalary,
                FamilySituation = application.FamilySituation,
                PersonalStory = application.PersonalStory,
                AcademicBackground = application.AcademicBackground,
                CurrentEducationLevel = application.CurrentEducationLevel,
                FieldOfStudy = application.FieldOfStudy,
                DreamCareer = application.DreamCareer,
                ProfileImageUrl = application.ProfileImageUrl,
                ProofDocumentUrls = documentUrls,
                GalleryImageUrls = galleryImageUrls,
                RequestedFundingAmount = application.RequestedFundingAmount,
                FundingPurpose = application.FundingPurpose,
                Status = application.Status,
                RejectionReason = application.RejectionReason,
                ReviewedByManagerId = application.ReviewedByManagerId,
                ReviewedByManagerAt = application.ReviewedByManagerAt,
                ApprovedByAdminId = application.ApprovedByAdminId,
                ApprovedByAdminAt = application.ApprovedByAdminAt,
                IsPostedAsStudent = application.IsPostedAsStudent,
                StudentId = application.StudentId,
                SubmittedAt = application.SubmittedAt,
                UpdatedAt = application.UpdatedAt
            };
        }

        private async Task NotifyAdmins(StudentApplication application)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                var subject = "Student Application Forwarded for Admin Review";
                var body = $"Hello {admin.FirstName},\n\n" +
                          $"A student application has been reviewed by a manager and is now awaiting your final approval.\n\n" +
                          $"Application ID: {application.Id}\n" +
                          $"Student: {application.FullName}\n" +
                          $"Status: Under Review\n" +
                          $"Reviewed by Manager: {application.ReviewedByManager?.FirstName} {application.ReviewedByManager?.LastName} at {application.ReviewedByManagerAt:yyyy-MM-dd HH:mm}\n\n" +
                          $"Please log in to the Admin dashboard to review and approve/reject the application.\n\n" +
                          $"Best regards,\nStudent Charity Hub Team";

                await _notificationService.SendEmailNotificationAsync(admin.Email!, subject, body);
                await _notificationService.SendNotificationAsync(admin.Id, "Application Under Review", $"Application by {application.FullName} is now Under Review.", "Info", $"/admin/applications/{application.Id}");
            }
        }

        private async Task NotifyStudentIncomplete(StudentApplication application)
        {
            var subject = "Action Required: Your Student Charity Hub Application is Incomplete";
            var body = $"Dear {application.FullName},\n\n" +
                      $"Your application to Student Charity Hub (ID: {application.Id}) has been marked as INCOMPLETE by our review team.\n\n" +
                      $"Reason for incompleteness: {application.RejectionReason}\n\n" +
                      $"Please log in to your student dashboard to view the details and resubmit the missing information or documents.\n\n" +
                      $"We look forward to receiving your updated application.\n\n" +
                      $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
            await _notificationService.SendNotificationAsync(application.ApplicationUserId, "Application Incomplete", $"Your application (ID: {application.Id}) has been marked incomplete. Please review and resubmit.", "Warning", $"/student/application-tracking");
        }

        // GET: api/Manager/donations-statistics
        [HttpGet("donations-statistics")]
        public async Task<IActionResult> GetDonationsStatistics()
        {
            try
            {
                var donations = await _context.Donations
                    .Include(d => d.Student)
                    .ThenInclude(s => s.ApplicationUser)
                    .ToListAsync();

                var totalDonations = donations.Count;
                var completedDonations = donations.Where(d => d.Status == "Completed").ToList();
                var pendingDonations = donations.Where(d => d.Status == "Pending" || d.Status == "Processing").ToList();
                var totalAmount = completedDonations.Sum(d => d.Amount);

                return Ok(new
                {
                    totalDonations,
                    completedDonations = completedDonations.Count,
                    pendingDonations = pendingDonations.Count,
                    totalAmount,
                    donations = donations.Select(d => new
                    {
                        id = d.Id,
                        amount = d.Amount,
                        status = d.Status,
                        studentName = d.Student?.FullName ?? "Unknown",
                        createdAt = d.CreatedAt,
                        completedAt = d.CompletedAt
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching donation statistics for manager");
                return StatusCode(500, new { message = "Error fetching donation statistics" });
            }
        }

        // GET: api/Manager/messaging/users-for-messaging
        [HttpGet("messaging/users-for-messaging")]
        public async Task<IActionResult> GetUsersForMessaging([FromQuery] string? role = null, [FromQuery] string? search = null)
        {
            try
            {
                // Managers can only message Students and Admins
                var allowedRoles = new[] { "Student", "Admin" };
                
                var query = _userManager.Users.AsQueryable();

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(u => 
                        (u.Email != null && u.Email.Contains(search)) || 
                        (u.FirstName != null && u.FirstName.Contains(search)) || 
                        (u.LastName != null && u.LastName.Contains(search)));
                }

                var users = await query.ToListAsync();
                var userDtos = new List<object>();

                foreach (var user in users)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    var userRole = roles.FirstOrDefault();

                    // Only include Students and Admins
                    if (userRole != null && allowedRoles.Contains(userRole))
                    {
                        if (string.IsNullOrEmpty(role) || userRole == role)
                        {
                            userDtos.Add(new
                            {
                                id = user.Id,
                                email = user.Email,
                                firstName = user.FirstName,
                                lastName = user.LastName,
                                role = userRole,
                                profilePictureUrl = user.ProfilePictureUrl
                            });
                        }
                    }
                }

                return Ok(userDtos.OrderBy(u => ((dynamic)u).firstName).ThenBy(u => ((dynamic)u).lastName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users for messaging");
                return StatusCode(500, new { message = "Error fetching users" });
            }
        }

        private async Task NotifyStudentRejection(StudentApplication application)
        {
            var subject = "Update on Your Student Charity Hub Application";
            var body = $"Dear {application.FullName},\n\n" +
                      $"Thank you for your interest in Student Charity Hub.\n\n" +
                      $"After careful review, we regret to inform you that your application (ID: {application.Id}) has been REJECTED.\n\n";

            if (!string.IsNullOrEmpty(application.RejectionReason))
            {
                body += $"Reason: {application.RejectionReason}\n\n";
            }

            body += $"We encourage you to reapply in the future if your circumstances change.\n\n" +
                   $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
            await _notificationService.SendNotificationAsync(application.ApplicationUserId, "Application Rejected", $"Your application (ID: {application.Id}) has been rejected. Reason: {application.RejectionReason}", "Error", $"/student/application-tracking");
        }
    }

    public class MessageStudentByEmailDto
    {
        [Required]
        [EmailAddress]
        public string StudentEmail { get; set; } = string.Empty;

        [Required]
        [StringLength(1000, MinimumLength = 10)]
        public string Message { get; set; } = string.Empty;
    }
}

