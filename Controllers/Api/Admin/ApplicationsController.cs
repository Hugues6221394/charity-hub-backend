using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.DTOs;
using StudentCharityHub.Models;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api.Admin
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class ApplicationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ApplicationsController> _logger;

        public ApplicationsController(
            ApplicationDbContext context,
            ILogger<ApplicationsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/admin/applications
        [HttpGet]
        public async Task<IActionResult> GetApplications([FromQuery] ApplicationStatus? status = null)
        {
            var query = _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Include(sa => sa.ReviewedByManager)
                .Include(sa => sa.ApprovedByAdmin)
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

        // GET: api/admin/applications/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetApplication(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .Include(sa => sa.ReviewedByManager)
                .Include(sa => sa.ApprovedByAdmin)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            return Ok(MapToDto(application));
        }

        // PUT: api/admin/applications/{id}/approve
        [HttpPut("{id}/approve")]
        public async Task<IActionResult> ApproveApplication(int id)
        {
            var application = await _context.StudentApplications
                .Include(sa => sa.ApplicationUser)
                .FirstOrDefaultAsync(sa => sa.Id == id);

            if (application == null)
                return NotFound();

            if (application.Status != ApplicationStatus.UnderReview && application.Status != ApplicationStatus.Pending)
            {
                return BadRequest(new { message = "Only pending or under review applications can be approved" });
            }

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            application.Status = ApplicationStatus.Approved;
            application.ApprovedByAdminId = adminId;
            application.ApprovedByAdminAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} approved by Admin {AdminId}", id, adminId);

            return Ok(MapToDto(application));
        }

        // PUT: api/admin/applications/{id}/reject
        [HttpPut("{id}/reject")]
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

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            application.Status = ApplicationStatus.Rejected;
            application.RejectionReason = dto.Reason;
            application.ApprovedByAdminId = adminId;
            application.ApprovedByAdminAt = DateTime.UtcNow;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} rejected by Admin {AdminId}", id, adminId);

            return Ok(MapToDto(application));
        }

        // PUT: api/admin/applications/{id}/mark-incomplete
        [HttpPut("{id}/mark-incomplete")]
        public async Task<IActionResult> MarkIncomplete(int id, [FromBody] ApplicationActionDto dto)
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

            application.Status = ApplicationStatus.Incomplete;
            application.RejectionReason = dto.Reason;
            application.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Application {ApplicationId} marked Incomplete by Admin", id);

            return Ok(MapToDto(application));
        }

        private StudentApplicationDto MapToDto(StudentApplication application)
        {
            List<string>? documentUrls = null;
            if (!string.IsNullOrEmpty(application.ProofDocuments))
            {
                try
                {
                    documentUrls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(application.ProofDocuments);
                }
                catch { }
            }

            List<string>? galleryImageUrls = null;
            if (!string.IsNullOrEmpty(application.GalleryImages))
            {
                try
                {
                    galleryImageUrls = System.Text.Json.JsonSerializer.Deserialize<List<string>>(application.GalleryImages);
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
    }
}

