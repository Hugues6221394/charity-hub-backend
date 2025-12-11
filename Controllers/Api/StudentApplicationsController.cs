using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.DTOs;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.Security.Claims;
using System.Text.Json;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class StudentApplicationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<StudentApplicationsController> _logger;

        public StudentApplicationsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            IWebHostEnvironment environment,
            ILogger<StudentApplicationsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _environment = environment;
            _logger = logger;
        }

        // POST: api/StudentApplications
        [HttpPost]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> SubmitApplication([FromBody] StudentApplicationSubmitDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            // Check if user already has a pending or approved application
            var existingApplication = await _context.StudentApplications
                .FirstOrDefaultAsync(sa => sa.ApplicationUserId == userId &&
                    (sa.Status == ApplicationStatus.Pending ||
                     sa.Status == ApplicationStatus.UnderReview ||
                     sa.Status == ApplicationStatus.Approved));

            if (existingApplication != null)
            {
                return BadRequest(new { message = "You already have an active application" });
            }

            var application = new StudentApplication
            {
                ApplicationUserId = userId,
                FullName = dto.FullName,
                Age = dto.Age,
                PlaceOfBirth = dto.PlaceOfBirth,
                CurrentResidency = dto.CurrentResidency,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,
                FatherName = dto.FatherName,
                MotherName = dto.MotherName,
                ParentsAnnualSalary = dto.ParentsAnnualSalary,
                FamilySituation = dto.FamilySituation,
                PersonalStory = dto.PersonalStory,
                AcademicBackground = dto.AcademicBackground,
                CurrentEducationLevel = dto.CurrentEducationLevel,
                FieldOfStudy = dto.FieldOfStudy,
                DreamCareer = dto.DreamCareer,
                ProfileImageUrl = dto.ProfileImageUrl,
                ProofDocuments = dto.ProofDocumentUrls != null ? JsonSerializer.Serialize(dto.ProofDocumentUrls) : null,
                GalleryImages = dto.GalleryImageUrls != null ? JsonSerializer.Serialize(dto.GalleryImageUrls) : null,
                RequestedFundingAmount = dto.RequestedFundingAmount,
                FundingPurpose = dto.FundingPurpose,
                Status = ApplicationStatus.Pending,
                SubmittedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.StudentApplications.Add(application);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Student application {ApplicationId} submitted by user {UserId}", application.Id, userId);

            // Notify all managers
            await NotifyManagers(application);

            return CreatedAtAction(nameof(GetApplication), new { id = application.Id }, MapToDto(application));
        }

        // GET: api/StudentApplications
        [HttpGet]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetApplications([FromQuery] ApplicationStatus? status = null)
        {
            var query = _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .AsQueryable();

            if (status.HasValue)
            {
                query = query.Where(sa => sa.Status == status.Value);
            }

            var applications = await query
                .OrderByDescending(sa => sa.SubmittedAt)
                .ToListAsync();

            return Ok(applications.Select(MapToDto));
        }

        // GET: api/StudentApplications/{id}
        [HttpGet("{id}")]
        [Authorize(Roles = "Admin,Manager,Student")]
        public async Task<IActionResult> GetApplication(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Include(sa => sa.ReviewedByManager)
                .Include(sa => sa.ApprovedByAdmin)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            // Students can only view their own applications
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.IsInRole("Student") && application.ApplicationUserId != userId)
            {
                return Forbid();
            }

            return Ok(MapToDto(application));
        }

        // GET: api/StudentApplications/my-application
        [HttpGet("my-application")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyApplication()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null || string.IsNullOrEmpty(user.Email))
                return Unauthorized();

            // First, try to find application by ApplicationUserId
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Include(sa => sa.ReviewedByManager)
                .Include(sa => sa.ApprovedByAdmin)
                .Where(sa => sa.ApplicationUserId == userId)
                .OrderByDescending(sa => sa.SubmittedAt)
                .FirstOrDefaultAsync();

            // If not found by userId, try to find by email (for manually created applications)
            if (application == null)
            {
                application = await _context.StudentApplications
                    .Include(sa => sa.ApplicationUser)
                    .Include(sa => sa.ReviewedByManager)
                    .Include(sa => sa.ApprovedByAdmin)
                    .Where(sa => sa.Email.ToLower() == user.Email.ToLower())
                    .OrderByDescending(sa => sa.SubmittedAt)
                    .FirstOrDefaultAsync();

                // If found by email, link it to the user
                if (application != null && application.ApplicationUserId != userId)
                {
                    application.ApplicationUserId = userId;
                    application.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Linked application {ApplicationId} to user {UserId} by email {Email}", application.Id, userId, user.Email);
                }
            }

            if (application == null)
                return NotFound();

            return Ok(MapToDto(application));
        }

        // PUT: api/StudentApplications/{id}/resubmit
        [HttpPut("{id}/resubmit")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> ResubmitApplication(int id, [FromBody] StudentApplicationSubmitDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var application = await _context.StudentApplications
                .FirstOrDefaultAsync(sa => sa.Id == id && sa.ApplicationUserId == userId);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.Incomplete)
            {
                return BadRequest(new { message = "Only incomplete applications can be resubmitted" });
            }

            // Update application with new data
            application.FullName = dto.FullName;
            application.Age = dto.Age;
            application.PlaceOfBirth = dto.PlaceOfBirth;
            application.CurrentResidency = dto.CurrentResidency;
            application.Email = dto.Email;
            application.PhoneNumber = dto.PhoneNumber;
            application.FatherName = dto.FatherName;
            application.MotherName = dto.MotherName;
            application.ParentsAnnualSalary = dto.ParentsAnnualSalary;
            application.FamilySituation = dto.FamilySituation;
            application.PersonalStory = dto.PersonalStory;
            application.AcademicBackground = dto.AcademicBackground;
            application.CurrentEducationLevel = dto.CurrentEducationLevel;
            application.FieldOfStudy = dto.FieldOfStudy;
            application.DreamCareer = dto.DreamCareer;
            application.ProfileImageUrl = dto.ProfileImageUrl;
                application.ProofDocuments = dto.ProofDocumentUrls != null ? JsonSerializer.Serialize(dto.ProofDocumentUrls) : null;
                application.GalleryImages = dto.GalleryImageUrls != null ? JsonSerializer.Serialize(dto.GalleryImageUrls) : null;
                application.RequestedFundingAmount = dto.RequestedFundingAmount;
            application.FundingPurpose = dto.FundingPurpose;
            application.Status = ApplicationStatus.Pending;
            application.RejectionReason = null; // Clear previous rejection reason
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} resubmitted by user {UserId}", id, userId);

            // Notify managers
            await NotifyManagers(application);

            // Notify student
            await _notificationService.SendNotificationAsync(
                userId,
                "Application Resubmitted",
                "Your application has been resubmitted and is pending review.",
                "Success",
                "/student/application"
            );

            return Ok(MapToDto(application));
        }

        // PUT: api/StudentApplications/{id}/approve
        [HttpPut("{id}/approve")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ApproveApplication(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.UnderReview)
            {
                return BadRequest(new { message = "Only applications under review can be approved" });
            }

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            application.Status = ApplicationStatus.Approved;
            application.ApprovedByAdminId = adminId;
            application.ApprovedByAdminAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} approved by Admin {AdminId}", id, adminId);

            // Notify student of approval
            await NotifyStudentApproval(application);
            await NotifyManagerOnStatusChange(
                application,
                "Application Approved",
                $"Application for {application.FullName} has been approved.",
                "/manager/applications"
            );

            return Ok(MapToDto(application));
        }

        // PUT: api/StudentApplications/{id}/reject
        [HttpPut("{id}/reject")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> RejectApplication(int id, [FromBody] ApplicationActionDto dto)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status == ApplicationStatus.Approved || application.Status == ApplicationStatus.Rejected)
            {
                return BadRequest(new { message = "Application has already been processed" });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.IsInRole("Manager"))
            {
                application.ReviewedByManagerId = userId;
                application.ReviewedByManagerAt = DateTime.UtcNow;
            }

            application.Status = ApplicationStatus.Rejected;
            application.RejectionReason = dto.Reason;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} rejected by {UserId}", id, userId);

            // Notify student of rejection
            await NotifyStudentRejection(application);
            await NotifyManagerOnStatusChange(
                application,
                "Application Rejected",
                $"Application for {application.FullName} has been rejected.",
                "/manager/applications"
            );

            return Ok(MapToDto(application));
        }

        // PUT: api/StudentApplications/{id}/mark-incomplete
        [HttpPut("{id}/mark-incomplete")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> MarkIncomplete(int id, [FromBody] ApplicationActionDto dto)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status == ApplicationStatus.Approved)
            {
                return BadRequest(new { message = "Approved applications cannot be marked as incomplete" });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (User.IsInRole("Manager"))
            {
                application.ReviewedByManagerId = userId;
                application.ReviewedByManagerAt = DateTime.UtcNow;
            }

            application.Status = ApplicationStatus.Incomplete;
            application.RejectionReason = dto.Reason; // Use as notes for missing documents
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} marked as incomplete by {UserId}", id, userId);

            // Notify student
            await NotifyStudentIncomplete(application);
            await NotifyManagerOnStatusChange(
                application,
                "Application Marked Incomplete",
                $"Application for {application.FullName} has been marked incomplete.",
                "/manager/applications"
            );

            return Ok(MapToDto(application));
        }

        // PUT: api/StudentApplications/{id}/forward-to-admin
        [HttpPut("{id}/forward-to-admin")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ForwardToAdmin(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.Pending && application.Status != ApplicationStatus.Incomplete)
            {
                return BadRequest(new { message = "Only pending or incomplete applications can be forwarded" });
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            application.Status = ApplicationStatus.UnderReview;
            application.ReviewedByManagerId = userId;
            application.ReviewedByManagerAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} forwarded to admin by Manager {UserId}", id, userId);

            // Notify admins
            await NotifyAdmins(application);

            // Notify student
            await NotifyStudentUnderReview(application);

            return Ok(MapToDto(application));
        }

        // DELETE: api/StudentApplications/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> DeleteApplication(int id)
        {
            var application = await _context.StudentApplications
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            // Only allow deletion of rejected applications
            if (application.Status != ApplicationStatus.Rejected)
            {
                return BadRequest(new { message = "Only rejected applications can be deleted" });
            }

            _context.StudentApplications.Remove(application);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Rejected application {ApplicationId} deleted by Manager", id);

            return Ok(new { message = "Application deleted successfully" });
        }

        // POST: api/StudentApplications/{id}/post-student
        [HttpPost("{id}/post-student")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PostAsStudent(int id, [FromBody] PostStudentDto dto)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.Approved)
            {
                return BadRequest(new { message = "Only approved applications can be posted as students" });
            }

            if (application.IsPostedAsStudent)
            {
                return BadRequest(new { message = "This application has already been posted as a student" });
            }

            // Check if user already has a student profile
            var existingStudent = await _context.Students
                .FirstOrDefaultAsync(s => s.ApplicationUserId == application.ApplicationUserId);

            if (existingStudent != null)
            {
                return BadRequest(new { message = "User already has a student profile" });
            }

            string TrimTo(string? value, int max) =>
                string.IsNullOrEmpty(value) ? string.Empty :
                value.Length > max ? value.Substring(0, max) : value;

            // Create student profile from application
            var student = new Student
            {
                ApplicationUserId = application.ApplicationUserId,
                FullName = TrimTo(application.FullName, 200),
                Age = application.Age,
                Location = TrimTo(dto.Location ?? application.CurrentResidency, 500),
                Story = TrimTo(application.PersonalStory, 2000),
                AcademicBackground = string.IsNullOrEmpty(application.AcademicBackground) ? null : TrimTo(application.AcademicBackground, 500),
                DreamCareer = string.IsNullOrEmpty(application.DreamCareer) ? null : TrimTo(application.DreamCareer, 200),
                PhotoUrl = string.IsNullOrEmpty(application.ProfileImageUrl) ? null : TrimTo(application.ProfileImageUrl, 500),
                FundingGoal = dto.FundingGoal ?? application.RequestedFundingAmount,
                AmountRaised = 0,
                IsVisible = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Students.Add(student);
            await _context.SaveChangesAsync();

            // Update application
            application.IsPostedAsStudent = true;
            application.StudentId = student.Id;
            application.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Notify the student that their application has been posted
            if (application.ApplicationUser != null && !string.IsNullOrEmpty(application.ApplicationUser.Email))
            {
                var emailSubject = "Your Application Has Been Posted!";
                var emailBody = $"Hello {application.FullName},\n\n" +
                              "Congratulations! Your student application has been approved and posted on Student Charity Hub.\n\n" +
                              "You can now:\n" +
                              "- View your profile on the platform\n" +
                              "- Receive donations from generous donors\n" +
                              "- Track your funding progress\n" +
                              "- Post progress updates to keep your donors informed\n\n" +
                              "Please log in to your account to view your profile and start your journey!\n\n" +
                              "Best regards,\nStudent Charity Hub Team";

                await _notificationService.SendEmailNotificationAsync(application.ApplicationUser.Email, emailSubject, emailBody);
                await _notificationService.SendNotificationAsync(
                    application.ApplicationUserId,
                    "Your Application Has Been Posted!",
                    "Your student application has been approved and posted. You can now receive donations and track your progress.",
                    "Success",
                    "/student/dashboard"
                );
            }

            _logger.LogInformation("Student profile {StudentId} created from application {ApplicationId}", student.Id, id);

            return Ok(new { message = "Student posted successfully", studentId = student.Id });
        }

        // POST: api/StudentApplications/upload-image
        [HttpPost("upload-image")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> UploadProfileImage([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded" });

            if (file.Length > 5 * 1024 * 1024) // 5MB limit
                return BadRequest(new { message = "File size exceeds 5MB limit" });

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { message = "Only JPG and PNG images are allowed" });

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "images");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var fileUrl = $"/images/{uniqueFileName}";
            return Ok(new { url = fileUrl });
        }

        // POST: api/StudentApplications/upload-document
        [HttpPost("upload-document")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> UploadDocument([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded" });

            if (file.Length > 10 * 1024 * 1024) // 10MB limit
                return BadRequest(new { message = "File size exceeds 10MB limit" });

            var allowedExtensions = new[] { ".pdf", ".jpg", ".jpeg", ".png", ".doc", ".docx" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
                return BadRequest(new { message = "Invalid file type" });

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "documents");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(fileStream);
            }

            var fileUrl = $"/documents/{uniqueFileName}";
            return Ok(new { url = fileUrl });
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

        private async Task NotifyManagers(StudentApplication application)
        {
            var managers = await _userManager.GetUsersInRoleAsync("Manager");
            foreach (var manager in managers)
            {
                var subject = "New Student Application Received";
                var body = $"Hello {manager.FirstName},\n\n" +
                          $"A new student application has been submitted by {application.FullName}.\n\n" +
                          $"Application ID: {application.Id}\n" +
                          $"Submitted: {application.SubmittedAt:yyyy-MM-dd HH:mm}\n\n" +
                          $"Please review the application in the Manager dashboard.\n\n" +
                          $"Best regards,\nStudent Charity Hub Team";

                await _notificationService.SendEmailNotificationAsync(manager.Email!, subject, body);
            }
        }

        private async Task NotifyAdmins(StudentApplication application)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            foreach (var admin in admins)
            {
                var subject = "Student Application Forwarded for Review";
                var body = $"Hello {admin.FirstName},\n\n" +
                          $"A student application has been forwarded to you for final review.\n\n" +
                          $"Application ID: {application.Id}\n" +
                          $"Student: {application.FullName}\n" +
                          $"Forwarded: {application.ReviewedByManagerAt:yyyy-MM-dd HH:mm}\n\n" +
                          $"Please review and approve/reject the application in the Admin dashboard.\n\n" +
                          $"Best regards,\nStudent Charity Hub Team";

                await _notificationService.SendEmailNotificationAsync(admin.Email!, subject, body);
            }
        }

        private async Task NotifyStudentApproval(StudentApplication application)
        {
            var subject = "Congratulations! Your Application Has Been Approved";
            var body = $"Dear {application.FullName},\n\n" +
                      $"We are pleased to inform you that your application to Student Charity Hub has been approved!\n\n" +
                      $"Your profile will be posted on our platform soon, making you visible to potential donors.\n\n" +
                      $"You can now log in to your account using the credentials you created during registration:\n" +
                      $"Email: {application.Email}\n\n" +
                      $"Thank you for your patience throughout the application process.\n\n" +
                      $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
        }

        private async Task NotifyStudentRejection(StudentApplication application)
        {
            var subject = "Update on Your Student Charity Hub Application";
            var body = $"Dear {application.FullName},\n\n" +
                      $"Thank you for your interest in Student Charity Hub.\n\n" +
                      $"After careful review, we regret to inform you that we are unable to approve your application at this time.\n\n";

            if (!string.IsNullOrEmpty(application.RejectionReason))
            {
                body += $"Reason: {application.RejectionReason}\n\n";
            }

            body += $"We encourage you to reapply in the future if your circumstances change.\n\n" +
                   $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
            
            // Send in-app notification
            await _notificationService.SendNotificationAsync(
                application.ApplicationUserId,
                "Application Rejected",
                $"Your application has been rejected. Reason: {application.RejectionReason ?? "No reason provided"}",
                "Warning",
                "/student/application"
            );
        }

        private async Task NotifyStudentIncomplete(StudentApplication application)
        {
            var subject = "Action Required: Complete Your Application";
            var body = $"Dear {application.FullName},\n\n" +
                      $"Your application requires additional information or documents.\n\n";

            if (!string.IsNullOrEmpty(application.RejectionReason))
            {
                body += $"Missing Information: {application.RejectionReason}\n\n";
            }

            body += $"Please log in to your account and submit the missing information.\n\n" +
                   $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
            
            // Send in-app notification
            await _notificationService.SendNotificationAsync(
                application.ApplicationUserId,
                "Application Incomplete",
                $"Your application has been marked as incomplete. Please submit the missing information.",
                "Warning",
                "/student/application"
            );
        }

        private async Task NotifyStudentUnderReview(StudentApplication application)
        {
            var subject = "Application Under Review";
            var body = $"Dear {application.FullName},\n\n" +
                      $"Your application has been reviewed by a manager and forwarded to administration for final review.\n\n" +
                      $"We will notify you once a decision has been made.\n\n" +
                      $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(application.Email, subject, body);
            
            // Send in-app notification
            await _notificationService.SendNotificationAsync(
                application.ApplicationUserId,
                "Application Under Review",
                "Your application has been forwarded to administration for final review.",
                "Info",
                "/student/application"
            );
        }

        private async Task NotifyManagerOnStatusChange(StudentApplication application, string title, string message, string link = "/manager/applications")
        {
            if (!string.IsNullOrEmpty(application.ReviewedByManagerId))
            {
                await _notificationService.SendNotificationAsync(
                    application.ReviewedByManagerId,
                    title,
                    message,
                    "Info",
                    link
                );
            }
        }
    }
}
