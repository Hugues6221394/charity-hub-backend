using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.Security.Claims;
using System.Text.Json;
using System.ComponentModel.DataAnnotations;
using static StudentCharityHub.Models.StudentApplication;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StudentsController> _logger;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public StudentsController(
            ApplicationDbContext context,
            ILogger<StudentsController> logger,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService)
        {
            _context = context;
            _logger = logger;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        // GET: api/Students
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetStudents(
            [FromQuery] string? search = null,
            [FromQuery] string? location = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 12)
        {
            var query = _context.Students
                .Include(s => s.ApplicationUser)
                .Where(s => s.IsVisible)
                .AsQueryable();

            // Apply filters
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(s =>
                    s.FullName.Contains(search) ||
                    s.Story.Contains(search) ||
                    s.DreamCareer!.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(location))
            {
                query = query.Where(s => s.Location.Contains(location));
            }

            var totalCount = await query.CountAsync();
            var students = await query
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.FullName,
                    s.Age,
                    s.Location,
                    s.Story,
                    s.AcademicBackground,
                    s.DreamCareer,
                    s.PhotoUrl,
                    s.FundingGoal,
                    s.AmountRaised,
                    FundingProgress = s.FundingGoal > 0 ? (s.AmountRaised / s.FundingGoal) * 100 : 0,
                    s.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                students,
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
            });
        }

        // GET: api/Students/{id}
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStudent(int id)
        {
            var student = await _context.Students
                .Include(s => s.ApplicationUser)
                .Include(s => s.Donations.Where(d => d.Status == "Completed"))
                .Include(s => s.ProgressReports)
                .Include(s => s.Documents)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (student == null)
                return NotFound();

            if (!student.IsVisible && !User.IsInRole("Admin") && !User.IsInRole("Manager"))
                return NotFound();

            var donorCount = await _context.Donations
                .Where(d => d.StudentId == id && d.Status == "Completed")
                .Select(d => d.DonorId)
                .Distinct()
                .CountAsync();

            // Get gallery images from the student's application if it exists
            List<string>? galleryImageUrls = null;
            var application = await _context.StudentApplications
                .FirstOrDefaultAsync(sa => sa.StudentId == id);
            if (application != null && !string.IsNullOrEmpty(application.GalleryImages))
            {
                try
                {
                    galleryImageUrls = JsonSerializer.Deserialize<List<string>>(application.GalleryImages);
                }
                catch { }
            }

            return Ok(new
            {
                student.Id,
                student.FullName,
                student.Age,
                student.Location,
                student.Story,
                student.AcademicBackground,
                student.DreamCareer,
                student.PhotoUrl,
                student.FundingGoal,
                student.AmountRaised,
                FundingProgress = student.FundingGoal > 0 ? (student.AmountRaised / student.FundingGoal) * 100 : 0,
                DonorCount = donorCount,
                GalleryImageUrls = galleryImageUrls ?? new List<string>(),
                ProgressReports = student.ProgressReports
                    .OrderByDescending(pr => pr.ReportDate)
                    .Select(pr => new
                    {
                        pr.Id,
                        pr.Title,
                        pr.Description,
                        pr.Grade,
                        pr.ReportDate
                    }),
                Documents = student.Documents.Select(d => new
                {
                    d.Id,
                    d.FileName,
                    d.FilePath,
                    d.FileType,
                    d.UploadedAt
                }),
                // Get proof documents from application if exists
                ProofDocuments = application != null && !string.IsNullOrEmpty(application.ProofDocuments) 
                    ? JsonSerializer.Deserialize<List<string>>(application.ProofDocuments) ?? new List<string>()
                    : new List<string>(),
                Email = student.ApplicationUser?.Email,
                ApplicationUserId = student.ApplicationUserId,
                student.CreatedAt,
                student.UpdatedAt
            });
        }

        // GET: api/Students/{id}/donations
        [HttpGet("{id}/donations")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStudentDonations(int id)
        {
            var student = await _context.Students.FindAsync(id);
            if (student == null)
                return NotFound();

            var donations = await _context.Donations
                .Include(d => d.Donor)
                .Where(d => d.StudentId == id && d.Status == "Completed")
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.Amount,
                    DonorName = $"{d.Donor.FirstName} {d.Donor.LastName}",
                    d.CreatedAt
                })
                .ToListAsync();

            return Ok(donations);
        }

        // GET: api/Students/{id}/progress-reports
        [HttpGet("{id}/progress-reports")]
        [AllowAnonymous]
        public async Task<IActionResult> GetStudentProgressReports(int id)
        {
            var student = await _context.Students.FindAsync(id);
            if (student == null)
                return NotFound();

            var reports = await _context.ProgressReports
                .Where(pr => pr.StudentId == id)
                .OrderByDescending(pr => pr.ReportDate)
                .Select(pr => new
                {
                    pr.Id,
                    pr.Title,
                    pr.Description,
                    pr.Grade,
                    pr.PhotoUrl,
                    pr.VideoUrl,
                    pr.ReportDate
                })
                .ToListAsync();

            return Ok(reports);
        }

        // PUT: api/Students/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> UpdateStudent(int id, [FromBody] UpdateStudentDto dto)
        {
            try
            {
                var student = await _context.Students
                    .Include(s => s.ApplicationUser)
                    .Include(s => s.Documents)
                    .FirstOrDefaultAsync(s => s.Id == id);
                    
                if (student == null)
                    return NotFound(new { message = "Student not found" });

                // Update basic student info
                student.FullName = dto.FullName;
                student.Age = dto.Age;
                student.Location = dto.Location;
                student.Story = dto.Story;
                student.AcademicBackground = dto.AcademicBackground;
                student.DreamCareer = dto.DreamCareer;
                student.FundingGoal = dto.FundingGoal;
                // Only update IsVisible if it's explicitly provided (nullable bool allows us to detect this)
                if (dto.IsVisible.HasValue)
                {
                    student.IsVisible = dto.IsVisible.Value;
                }
                // Otherwise, keep the existing IsVisible value unchanged
                if (!string.IsNullOrEmpty(dto.PhotoUrl))
                {
                    student.PhotoUrl = dto.PhotoUrl;
                }
                student.UpdatedAt = DateTime.UtcNow;

                // Update email if provided and user is Admin
                if (!string.IsNullOrEmpty(dto.Email) && User.IsInRole("Admin"))
                {
                    if (student.ApplicationUser == null)
                        return BadRequest(new { message = "Student user account not found" });

                    // Validate email format
                    if (!new EmailAddressAttribute().IsValid(dto.Email))
                    {
                        return BadRequest(new { message = "Invalid email format" });
                    }

                    // Check if email is already in use by another user
                    var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                    if (existingUser != null && existingUser.Id != student.ApplicationUserId)
                    {
                        return BadRequest(new { message = $"Email {dto.Email} is already in use by another account" });
                    }

                    // Check if email exists in applications (approved or posted) for different student
                    var existingApplication = await _context.StudentApplications
                        .FirstOrDefaultAsync(sa => sa.Email == dto.Email && 
                            sa.StudentId != id &&
                            (sa.Status == ApplicationStatus.Approved || sa.IsPostedAsStudent));
                    
                    if (existingApplication != null)
                    {
                        return BadRequest(new { message = $"Email {dto.Email} is already associated with another approved or posted application" });
                    }

                    // Update email
                    student.ApplicationUser.Email = dto.Email;
                    student.ApplicationUser.UserName = dto.Email;
                    student.ApplicationUser.NormalizedEmail = dto.Email.ToUpperInvariant();
                    student.ApplicationUser.NormalizedUserName = dto.Email.ToUpperInvariant();
                    
                    var updateResult = await _userManager.UpdateAsync(student.ApplicationUser);
                    if (!updateResult.Succeeded)
                    {
                        var errors = string.Join(", ", updateResult.Errors.Select(e => e.Description));
                        return BadRequest(new { message = $"Failed to update email: {errors}" });
                    }
                }

                // Update documents if provided
                if (dto.Documents != null && User.IsInRole("Admin"))
                {
                    // Remove existing documents
                    _context.Documents.RemoveRange(student.Documents);
                    
                    // Add new documents
                    foreach (var docDto in dto.Documents)
                    {
                        var document = new Document
                        {
                            StudentId = student.Id,
                            FileName = docDto.FileName,
                            FilePath = docDto.FilePath,
                            FileType = docDto.FileType,
                            Description = docDto.Description ?? docDto.FileType,
                            FileSize = docDto.FileSize,
                            UploadedAt = DateTime.UtcNow
                        };
                        _context.Documents.Add(document);
                    }
                }

                // Update gallery images if provided
                if (dto.GalleryImageUrls != null && (User.IsInRole("Admin") || User.IsInRole("Manager")))
                {
                    // Find or create application for this student
                    var application = await _context.StudentApplications
                        .FirstOrDefaultAsync(sa => sa.StudentId == id);
                    
                    if (application == null)
                    {
                        // Create a new application record for manually added students
                        application = new StudentApplication
                        {
                            ApplicationUserId = student.ApplicationUserId,
                            StudentId = id,
                            FullName = student.FullName,
                            Email = student.ApplicationUser?.Email ?? "",
                            Status = ApplicationStatus.Approved,
                            IsPostedAsStudent = true,
                            SubmittedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _context.StudentApplications.Add(application);
                        await _context.SaveChangesAsync();
                    }
                    
                    // Update gallery images
                    application.GalleryImages = JsonSerializer.Serialize(dto.GalleryImageUrls);
                    application.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation("Student {StudentId} updated by {UserId}", id, User.FindFirstValue(ClaimTypes.NameIdentifier));

                return Ok(new { message = "Student updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating student {StudentId}", id);
                return StatusCode(500, new { message = $"An error occurred while updating the student: {ex.Message}" });
            }
        }

        // DELETE: api/Students/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            var student = await _context.Students.FindAsync(id);
            if (student == null)
                return NotFound();

            _context.Students.Remove(student);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Student deleted successfully" });
        }

        // POST: api/Students
        [HttpPost]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> CreateStudent([FromBody] CreateStudentDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { message = "Invalid data provided", errors = ModelState });

                // Validate email format
                if (string.IsNullOrWhiteSpace(dto.Email) || !dto.Email.Contains("@"))
                {
                    return BadRequest(new { message = "Invalid email format. Please provide a valid email address." });
                }

                // Check if email already exists in users
                var existingUser = await _userManager.FindByEmailAsync(dto.Email);
                
                // Check if email exists in applications (approved or posted)
                var existingApplication = await _context.StudentApplications
                    .FirstOrDefaultAsync(sa => sa.Email == dto.Email && 
                        (sa.Status == ApplicationStatus.Approved || sa.IsPostedAsStudent));
                
                if (existingApplication != null)
                {
                    return BadRequest(new { message = $"This email ({dto.Email}) is already associated with an approved or posted application. Please use a different email." });
                }

                ApplicationUser user;
                
                // Create new user account if it doesn't exist
                if (existingUser == null)
                {
                    // Generate a temporary password
                    var tempPassword = Guid.NewGuid().ToString("N").Substring(0, 12) + "A1!";
                    
                    user = new ApplicationUser
                    {
                        UserName = dto.Email,
                        Email = dto.Email,
                        FirstName = dto.FullName.Split(' ').FirstOrDefault() ?? dto.FullName,
                        LastName = dto.FullName.Contains(' ') ? string.Join(" ", dto.FullName.Split(' ').Skip(1)) : "",
                        EmailConfirmed = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    var createResult = await _userManager.CreateAsync(user, tempPassword);
                    if (!createResult.Succeeded)
                    {
                        var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                        return BadRequest(new { message = $"Failed to create user account: {errors}" });
                    }

                    // Assign Student role
                    await _userManager.AddToRoleAsync(user, "Student");

                    // Generate password reset token and send email
                    var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                    var resetUrl = $"{Request.Scheme}://{Request.Host}/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(dto.Email)}";
                    
                    var emailBody = $"Hello {dto.FullName},\n\n" +
                                   "A student profile has been created for you on Student Charity Hub.\n\n" +
                                   "To set your password and access your account, please click the link below:\n\n" +
                                   $"{resetUrl}\n\n" +
                                   "If the link doesn't work, you can copy and paste it into your browser.\n\n" +
                                   "Best regards,\nStudent Charity Hub Team";
                    
                    await _notificationService.SendEmailNotificationAsync(
                        dto.Email, 
                        "Welcome to Student Charity Hub - Set Your Password", 
                        emailBody
                    );

                    _logger.LogInformation("Created new user account and sent password reset email for {Email}", dto.Email);
                }
                else
                {
                    user = existingUser;
                    
                    // Check if this user already has a student profile
                    var existingStudentCheck = await _context.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == user.Id);
                    if (existingStudentCheck != null)
                    {
                        return BadRequest(new { message = $"A student profile already exists for this email address ({dto.Email})." });
                    }
                }

                var student = new Student
                {
                    ApplicationUserId = user.Id,
                    FullName = dto.FullName,
                    Age = dto.Age,
                    Location = dto.Location,
                    Story = dto.Story,
                    AcademicBackground = dto.AcademicBackground,
                    DreamCareer = dto.DreamCareer,
                    PhotoUrl = dto.PhotoUrl,
                    FundingGoal = dto.FundingGoal,
                    AmountRaised = 0,
                    IsVisible = dto.IsVisible,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Students.Add(student);
                await _context.SaveChangesAsync();

                // Create StudentApplication record for manually added students
                var application = new StudentApplication
                {
                    ApplicationUserId = user.Id,
                    StudentId = student.Id,
                    FullName = student.FullName,
                    Email = user.Email ?? dto.Email,
                    Age = student.Age,
                    CurrentResidency = student.Location,
                    PersonalStory = student.Story,
                    AcademicBackground = student.AcademicBackground,
                    DreamCareer = student.DreamCareer,
                    ProfileImageUrl = student.PhotoUrl,
                    RequestedFundingAmount = student.FundingGoal,
                    Status = ApplicationStatus.Approved,
                    IsPostedAsStudent = true,
                    SubmittedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.StudentApplications.Add(application);
                await _context.SaveChangesAsync();

                // Add documents if provided
                if (dto.Documents != null && dto.Documents.Count > 0)
                {
                    foreach (var docDto in dto.Documents)
                    {
                        var document = new Document
                        {
                            StudentId = student.Id,
                            FileName = docDto.FileName,
                            FilePath = docDto.FilePath,
                            FileType = docDto.FileType,
                            Description = docDto.Description,
                            FileSize = docDto.FileSize,
                            UploadedAt = DateTime.UtcNow
                        };
                        _context.Documents.Add(document);
                    }
                    await _context.SaveChangesAsync();
                }

                // Notify the student that their profile has been created and posted
                var emailSubject = "Your Student Profile Has Been Created and Posted";
                var profilePostedEmailBody = $"Hello {student.FullName},\n\n" +
                               "Great news! Your student profile has been created and posted on Student Charity Hub.\n\n" +
                               "You can now:\n" +
                               "- View your profile on the platform\n" +
                               "- Receive donations from generous donors\n" +
                               "- Track your funding progress\n" +
                               "- Post progress updates to keep your donors informed\n\n" +
                               "Please log in to your account to view your profile and start your journey!\n\n" +
                               "Best regards,\nStudent Charity Hub Team";

                await _notificationService.SendEmailNotificationAsync(user.Email!, emailSubject, profilePostedEmailBody);
                await _notificationService.SendNotificationAsync(
                    user.Id,
                    "Your Profile Has Been Posted!",
                    "Your student profile has been created and posted. You can now receive donations and track your progress.",
                    "Success",
                    "/student/dashboard"
                );

                _logger.LogInformation("Student profile {StudentId} created successfully for user {UserId}", student.Id, user.Id);

                return Ok(new { 
                    message = existingUser == null 
                        ? "Student created successfully. Password reset email has been sent to the student's email address." 
                        : "Student created successfully.", 
                    studentId = student.Id 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating student profile for email {Email}", dto.Email);
                return StatusCode(500, new { message = $"An error occurred while creating the student profile: {ex.Message}" });
            }
        }

        // GET: api/Students/my-profile
        [HttpGet("my-profile")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var student = await _context.Students
                .Include(s => s.Donations.Where(d => d.Status == "Completed"))
                .Include(s => s.ProgressReports)
                .Include(s => s.Followers)
                .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

            if (student == null)
                return NotFound(new { message = "Student profile not found" });

            var donorCount = student.Donations
                .Select(d => d.DonorId)
                .Distinct()
                .Count();

            return Ok(new
            {
                student.Id,
                student.FullName,
                student.Age,
                student.Location,
                student.Story,
                student.AcademicBackground,
                student.DreamCareer,
                student.PhotoUrl,
                student.FundingGoal,
                student.AmountRaised,
                FundingProgress = student.FundingGoal > 0 ? (student.AmountRaised / student.FundingGoal) * 100 : 0,
                DonorCount = donorCount,
                FollowerCount = student.Followers.Count,
                TotalDonations = student.Donations.Count,
                RecentProgressReports = student.ProgressReports
                    .OrderByDescending(pr => pr.ReportDate)
                    .Take(5)
                    .Select(pr => new
                    {
                        pr.Id,
                        pr.Title,
                        pr.Description,
                        pr.Grade,
                        pr.ReportDate
                    })
            });
        }

        // GET: api/Students/my-donors
        [HttpGet("my-donors")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyDonors()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var student = await _context.Students
                .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

            if (student == null)
                return NotFound(new { message = "Student profile not found" });

            var donors = await _context.Donations
                .Where(d => d.StudentId == student.Id && d.Status == "Completed")
                .Include(d => d.Donor)
                .GroupBy(d => d.DonorId)
                .Select(g => new
                {
                    DonorId = g.Key,
                    Name = g.First().Donor.FirstName + " " + g.First().Donor.LastName,
                    Email = g.First().Donor.Email,
                    ProfilePictureUrl = g.First().Donor.ProfilePictureUrl,
                    TotalAmount = g.Sum(d => d.Amount),
                    DonationCount = g.Count(),
                    FirstDonation = g.Min(d => d.CreatedAt),
                    LastDonation = g.Max(d => d.CreatedAt)
                })
                .OrderByDescending(d => d.TotalAmount)
                .ToListAsync();

            return Ok(donors);
        }

        // GET: api/Students/by-status
        [HttpGet("by-status")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> GetStudentsByStatus([FromQuery] ApplicationStatus? status = null)
        {
            // 1) Students that already exist in Students table (posted/approved)
            var studentsQuery = _context.Students
                .Include(s => s.ApplicationUser)
                .Include(s => s.Documents)
                .AsQueryable();

            if (status.HasValue)
            {
                if (status.Value == ApplicationStatus.Approved)
                {
                    // Approved: include posted students OR applications marked approved
                    studentsQuery = studentsQuery.Where(s => _context.StudentApplications
                        .Any(sa => sa.ApplicationUserId == s.ApplicationUserId &&
                                   (sa.Status == ApplicationStatus.Approved || sa.IsPostedAsStudent)));
                }
                else
                {
                    // Other statuses: only include students whose latest application matches the filter
                    studentsQuery = studentsQuery.Where(s => _context.StudentApplications
                        .Any(sa => sa.ApplicationUserId == s.ApplicationUserId &&
                                   sa.Status == status.Value &&
                                   !sa.IsPostedAsStudent));
                }
            }

            var studentResults = await studentsQuery
                .Select(s => new
                {
                    s.Id,
                    s.FullName,
                    s.Age,
                    Location = s.Location,
                    Story = s.Story,
                    AcademicBackground = s.AcademicBackground,
                    DreamCareer = s.DreamCareer,
                    PhotoUrl = s.PhotoUrl,
                    s.FundingGoal,
                    s.AmountRaised,
                    s.IsVisible,
                    s.CreatedAt,
                    Email = s.ApplicationUser.Email,
                    ApplicationStatus = _context.StudentApplications
                        .Where(sa => sa.ApplicationUserId == s.ApplicationUserId)
                        .OrderByDescending(sa => sa.SubmittedAt)
                        .Select(sa => (ApplicationStatus?)(sa.IsPostedAsStudent ? ApplicationStatus.Approved : sa.Status))
                        .FirstOrDefault() ?? ApplicationStatus.Approved,
                    ApplicationId = _context.StudentApplications
                        .Where(sa => sa.ApplicationUserId == s.ApplicationUserId)
                        .OrderByDescending(sa => sa.SubmittedAt)
                        .Select(sa => sa.Id)
                        .FirstOrDefault(),
                    IsFromApplication = false
                })
                .ToListAsync();

            // 2) Applications that are not yet posted as students (covers Pending/UnderReview/Incomplete/Rejected)
            var applicationsQuery = _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Where(sa => !sa.IsPostedAsStudent); // only pre-posted apps

            if (status.HasValue)
            {
                applicationsQuery = applicationsQuery.Where(sa => sa.Status == status.Value);
            }

            var applicationResults = await applicationsQuery
                .Select(sa => new
                {
                    Id = sa.StudentId ?? 0, // 0 indicates not yet a student profile
                    FullName = sa.FullName,
                    Age = sa.Age,
                    Location = sa.CurrentResidency,
                    Story = sa.PersonalStory,
                    AcademicBackground = sa.AcademicBackground,
                    DreamCareer = sa.DreamCareer,
                    PhotoUrl = sa.ProfileImageUrl,
                    FundingGoal = sa.RequestedFundingAmount,
                    AmountRaised = 0m,
                    IsVisible = false,
                    CreatedAt = sa.SubmittedAt,
                    Email = sa.Email,
                    ApplicationStatus = sa.Status,
                    ApplicationId = sa.Id,
                    IsFromApplication = true
                })
                .ToListAsync();

            // Combine and order by newest first
            var combined = studentResults
                .Concat(applicationResults)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();

            return Ok(combined);
        }

        // GET: api/Students/messaging/users-for-messaging
        [HttpGet("messaging/users-for-messaging")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetUsersForMessaging([FromQuery] string? role = null, [FromQuery] string? search = null)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                // Get the student profile
                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

                if (student == null)
                    return BadRequest(new { message = "Student profile not found" });

                // Get donors who have donated to this student
                var donorIds = await _context.Donations
                    .Where(d => d.StudentId == student.Id && d.Status == "Completed")
                    .Select(d => d.DonorId)
                    .Distinct()
                    .ToListAsync();

                // Get all Managers and Admins
                var managers = await _userManager.GetUsersInRoleAsync("Manager");
                var admins = await _userManager.GetUsersInRoleAsync("Admin");

                // Combine all allowed user IDs
                var allowedUserIds = donorIds
                    .Concat(managers.Select(m => m.Id))
                    .Concat(admins.Select(a => a.Id))
                    .Distinct()
                    .ToList();

                var query = _userManager.Users
                    .Where(u => allowedUserIds.Contains(u.Id))
                    .AsQueryable();

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

                    if (userRole != null)
                    {
                        // Only include if role matches filter or no filter
                        if (string.IsNullOrEmpty(role) || userRole == role)
                        {
                            // For donors, only include if they have donated to this student
                            if (userRole == "Donor" && !donorIds.Contains(user.Id))
                                continue;

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
    }

    public class UpdateStudentDto
    {
        public string FullName { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Location { get; set; } = string.Empty;
        public string Story { get; set; } = string.Empty;
        public string? AcademicBackground { get; set; }
        public string? DreamCareer { get; set; }
        public decimal FundingGoal { get; set; }
        public bool? IsVisible { get; set; } // Make nullable to detect when not provided
        public string? PhotoUrl { get; set; }
        public string? Email { get; set; }
        public List<DocumentDto>? Documents { get; set; }
        public List<string>? GalleryImageUrls { get; set; }
    }

    public class CreateStudentDto
    {
        [Required]
        public string Email { get; set; } = string.Empty;
        [Required]
        public string FullName { get; set; } = string.Empty;
        [Required]
        public int Age { get; set; }
        [Required]
        public string Location { get; set; } = string.Empty;
        [Required]
        public string Story { get; set; } = string.Empty;
        public string? AcademicBackground { get; set; }
        public string? DreamCareer { get; set; }
        public string? PhotoUrl { get; set; }
        [Required]
        public decimal FundingGoal { get; set; }
        public bool IsVisible { get; set; } = true;
        public List<DocumentDto>? Documents { get; set; }
    }

    public class DocumentDto
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string? Description { get; set; }
        public long FileSize { get; set; }
    }
}
