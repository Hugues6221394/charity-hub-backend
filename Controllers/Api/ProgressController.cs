using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProgressController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly ILogger<ProgressController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly UserManager<ApplicationUser> _userManager;

        public ProgressController(
            ApplicationDbContext context,
            INotificationService notificationService,
            ILogger<ProgressController> logger,
            IWebHostEnvironment environment,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _notificationService = notificationService;
            _logger = logger;
            _environment = environment;
            _userManager = userManager;
        }

        // POST: api/progress/create
        [HttpPost("create")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> CreateProgressReport([FromBody] CreateProgressReportDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { message = "Invalid data provided", errors = ModelState });

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                // Get student profile
                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

                if (student == null)
                    return BadRequest(new { message = "Student profile not found. Please complete your student profile first." });

                // Create progress report
                var progressReport = new ProgressReport
                {
                    StudentId = student.Id,
                    Title = dto.Title,
                    Description = dto.Description,
                    Grade = dto.Grade,
                    Achievement = dto.Achievement,
                    PhotoUrl = dto.PhotoUrl,
                    VideoUrl = dto.VideoUrl,
                    ReportDate = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow,
                    NotificationsSent = false
                };

                _context.ProgressReports.Add(progressReport);
                await _context.SaveChangesAsync();

                // Notify all donors who have donated to this student
                var donors = await _context.Donations
                    .Where(d => d.StudentId == student.Id && d.Status == "Completed")
                    .Select(d => d.DonorId)
                    .Distinct()
                    .ToListAsync();

                foreach (var donorId in donors)
                {
                    await _notificationService.SendNotificationAsync(
                        donorId,
                        $"New Progress Update from {student.FullName}",
                        $"{dto.Title}: {(dto.Description != null ? dto.Description.Substring(0, Math.Min(100, dto.Description.Length)) : "")}...",
                        "Info",
                        $"/donor/progress"
                    );
                }

                // Notify all managers and admins
                var managers = await _userManager.GetUsersInRoleAsync("Manager");
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                
                foreach (var manager in managers)
                {
                    await _notificationService.SendNotificationAsync(
                        manager.Id,
                        $"Student Progress Update: {student.FullName}",
                        $"{dto.Title}: {(dto.Description != null ? dto.Description.Substring(0, Math.Min(100, dto.Description.Length)) : "")}...",
                        "Info",
                        $"/manager/progress"
                    );
                }
                
                foreach (var admin in admins)
                {
                    await _notificationService.SendNotificationAsync(
                        admin.Id,
                        $"Student Progress Update: {student.FullName}",
                        $"{dto.Title}: {(dto.Description != null ? dto.Description.Substring(0, Math.Min(100, dto.Description.Length)) : "")}...",
                        "Info",
                        $"/admin/progress"
                    );
                }

                progressReport.NotificationsSent = true;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Progress report {ReportId} created by student {StudentId}", progressReport.Id, student.Id);

                return Ok(new { message = "Progress report created successfully. Donors have been notified.", reportId = progressReport.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating progress report");
                return StatusCode(500, new { message = $"An error occurred while creating the progress report: {ex.Message}" });
            }
        }

        // POST: api/progress/upload-media
        [HttpPost("upload-media")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> UploadMedia([FromForm] IFormFile file)
        {
            try
            {
                if (file == null || file.Length == 0)
                    return BadRequest(new { message = "No file uploaded" });

                if (file.Length > 10 * 1024 * 1024) // 10MB limit
                    return BadRequest(new { message = "File size exceeds 10MB limit" });

                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".mp4", ".mov", ".avi" };
                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(extension))
                    return BadRequest(new { message = "Invalid file type. Allowed: JPG, PNG, MP4, MOV, AVI" });

                var uploadsFolder = Path.Combine(_environment.WebRootPath, "progress-media");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var uniqueFileName = $"{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(fileStream);
                }

                var fileUrl = $"/progress-media/{uniqueFileName}";
                return Ok(new { url = fileUrl });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading progress media");
                return StatusCode(500, new { message = "Error uploading file" });
            }
        }

        // GET: api/progress/my-progress-updates
        [HttpGet("my-progress-updates")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> GetMyProgressUpdates()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

                if (student == null)
                    return NotFound(new { message = "Student profile not found" });

                var reports = await _context.ProgressReports
                    .Where(pr => pr.StudentId == student.Id)
                    .OrderByDescending(pr => pr.ReportDate)
                    .Select(pr => new
                    {
                        pr.Id,
                        pr.Title,
                        pr.Description,
                        pr.Grade,
                        pr.Achievement,
                        pr.PhotoUrl,
                        pr.VideoUrl,
                        pr.ReportDate,
                        pr.CreatedAt
                    })
                    .ToListAsync();

                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching progress updates");
                return StatusCode(500, new { message = "Error fetching progress updates" });
            }
        }

        // PUT: api/progress/{id}
        [HttpPut("{id}")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> UpdateProgressReport(int id, [FromBody] CreateProgressReportDto dto)
        {
            try
            {
                if (!ModelState.IsValid)
                    return BadRequest(new { message = "Invalid data provided", errors = ModelState });

                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

                if (student == null)
                    return BadRequest(new { message = "Student profile not found" });

                var report = await _context.ProgressReports
                    .FirstOrDefaultAsync(pr => pr.Id == id && pr.StudentId == student.Id);

                if (report == null)
                    return NotFound(new { message = "Progress report not found" });

                report.Title = dto.Title;
                report.Description = dto.Description;
                report.Grade = dto.Grade;
                report.Achievement = dto.Achievement;
                report.PhotoUrl = dto.PhotoUrl;
                report.VideoUrl = dto.VideoUrl;
                report.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Progress report {ReportId} updated by student {StudentId}", report.Id, student.Id);

                return Ok(new { message = "Progress report updated successfully", reportId = report.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating progress report");
                return StatusCode(500, new { message = $"An error occurred while updating the progress report: {ex.Message}" });
            }
        }

        // DELETE: api/progress/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> DeleteProgressReport(int id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                var student = await _context.Students
                    .FirstOrDefaultAsync(s => s.ApplicationUserId == userId);

                if (student == null)
                    return NotFound(new { message = "Student profile not found" });

                var report = await _context.ProgressReports
                    .FirstOrDefaultAsync(pr => pr.Id == id && pr.StudentId == student.Id);

                if (report == null)
                    return NotFound(new { message = "Progress report not found" });

                _context.ProgressReports.Remove(report);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Progress report deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting progress report");
                return StatusCode(500, new { message = "Error deleting progress report" });
            }
        }

        // GET: api/progress/all (for donors, managers, admins)
        [HttpGet("all")]
        [Authorize(Roles = "Donor,Manager,Admin")]
        public async Task<IActionResult> GetAllProgressReports([FromQuery] int? studentId = null)
        {
            try
            {
                var query = _context.ProgressReports
                    .Include(pr => pr.Student)
                        .ThenInclude(s => s.ApplicationUser)
                    .AsQueryable();

                if (studentId.HasValue)
                {
                    query = query.Where(pr => pr.StudentId == studentId.Value);
                }

                var reports = await query
                    .OrderByDescending(pr => pr.ReportDate)
                    .Select(pr => new
                    {
                        pr.Id,
                        pr.Title,
                        pr.Description,
                        pr.Grade,
                        pr.Achievement,
                        pr.PhotoUrl,
                        pr.VideoUrl,
                        pr.ReportDate,
                        pr.CreatedAt,
                        Student = new
                        {
                            pr.Student.Id,
                            pr.Student.FullName,
                            pr.Student.PhotoUrl,
                            pr.Student.Location
                        }
                    })
                    .ToListAsync();

                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all progress reports");
                return StatusCode(500, new { message = "Error fetching progress reports" });
            }
        }

        // GET: api/progress/student/{studentId} (for donors viewing specific student progress)
        [HttpGet("student/{studentId}")]
        [Authorize(Roles = "Donor,Manager,Admin")]
        public async Task<IActionResult> GetStudentProgressReports(int studentId)
        {
            try
            {
                var reports = await _context.ProgressReports
                    .Where(pr => pr.StudentId == studentId)
                    .Include(pr => pr.Student)
                        .ThenInclude(s => s.ApplicationUser)
                    .OrderByDescending(pr => pr.ReportDate)
                    .Select(pr => new
                    {
                        pr.Id,
                        pr.Title,
                        pr.Description,
                        pr.Grade,
                        pr.Achievement,
                        pr.PhotoUrl,
                        pr.VideoUrl,
                        pr.ReportDate,
                        pr.CreatedAt,
                        Student = new
                        {
                            pr.Student.Id,
                            pr.Student.FullName,
                            pr.Student.PhotoUrl,
                            pr.Student.Location
                        }
                    })
                    .ToListAsync();

                return Ok(reports);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching student progress reports");
                return StatusCode(500, new { message = "Error fetching progress reports" });
            }
        }
    }

    public class CreateProgressReportDto
    {
        [Required]
        public string Title { get; set; } = string.Empty;
        [Required]
        public string Description { get; set; } = string.Empty;
        public string? Grade { get; set; }
        public string? Achievement { get; set; }
        public string? PhotoUrl { get; set; }
        public string? VideoUrl { get; set; }
        public List<string>? MediaUrls { get; set; }
    }
}

