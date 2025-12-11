using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using System.Text;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api.Manager
{
    [Route("api/manager/[controller]")]
    [ApiController]
    [Authorize(Roles = "Manager")]
    public class ReportsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            ApplicationDbContext context,
            ILogger<ReportsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: api/manager/reports/applications
        [HttpGet("applications")]
        public async Task<IActionResult> GetApplicationsReport([FromQuery] ApplicationStatus? status = null, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var query = _context.StudentApplications
                    .Include(sa => sa.ApplicationUser)
                    .AsQueryable();

                if (status.HasValue)
                    query = query.Where(sa => sa.Status == status.Value);
                if (startDate.HasValue)
                    query = query.Where(sa => sa.SubmittedAt >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(sa => sa.SubmittedAt <= endDate.Value);

                var applications = await query
                    .OrderByDescending(sa => sa.SubmittedAt)
                    .Select(sa => new
                    {
                        sa.Id,
                        sa.FullName,
                        sa.Age,
                        sa.Email,
                        sa.PhoneNumber,
                        sa.CurrentResidency,
                        sa.FieldOfStudy,
                        sa.RequestedFundingAmount,
                        sa.Status,
                        sa.SubmittedAt,
                        ReviewedAt = sa.ReviewedByManagerAt,
                        ApprovedAt = sa.ApprovedByAdminAt,
                        ManagerNotes = sa.RejectionReason
                    })
                    .ToListAsync();

                return Ok(applications);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating applications report");
                return StatusCode(500, new { message = "Error generating report" });
            }
        }

        // GET: api/manager/reports/export-csv
        [HttpGet("export-csv")]
        public async Task<IActionResult> ExportCSV([FromQuery] ApplicationStatus? status = null, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var query = _context.StudentApplications
                    .Include(sa => sa.ApplicationUser)
                    .AsQueryable();

                if (status.HasValue)
                    query = query.Where(sa => sa.Status == status.Value);
                if (startDate.HasValue)
                    query = query.Where(sa => sa.SubmittedAt >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(sa => sa.SubmittedAt <= endDate.Value);

                var applications = await query
                    .OrderByDescending(sa => sa.SubmittedAt)
                    .Select(sa => new
                    {
                        sa.Id,
                        sa.FullName,
                        sa.Age,
                        sa.Email,
                        sa.PhoneNumber,
                        sa.CurrentResidency,
                        sa.FieldOfStudy,
                        sa.RequestedFundingAmount,
                        sa.Status,
                        sa.SubmittedAt,
                        ReviewedAt = sa.ReviewedByManagerAt,
                        ApprovedAt = sa.ApprovedByAdminAt,
                        ManagerNotes = sa.RejectionReason ?? ""
                    })
                    .ToListAsync();

                var csv = new StringBuilder();
                csv.AppendLine("Id,FullName,Age,Email,PhoneNumber,CurrentResidency,FieldOfStudy,RequestedFundingAmount,Status,SubmittedAt,ReviewedAt,ApprovedAt,ManagerNotes");
                
                foreach (var app in applications)
                {
                    var reviewedAt = app.ReviewedAt.HasValue ? app.ReviewedAt.Value.ToString("yyyy-MM-dd") : "";
                    var approvedAt = app.ApprovedAt.HasValue ? app.ApprovedAt.Value.ToString("yyyy-MM-dd") : "";
                    csv.AppendLine($"{app.Id},\"{app.FullName}\",{app.Age},{app.Email},{app.PhoneNumber},\"{app.CurrentResidency}\",\"{app.FieldOfStudy}\",{app.RequestedFundingAmount},{app.Status},{app.SubmittedAt:yyyy-MM-dd},{reviewedAt},{approvedAt},\"{app.ManagerNotes}\"");
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                var filename = $"applications-report-{DateTime.UtcNow:yyyyMMdd}.csv";
                return File(bytes, "text/csv", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting CSV");
                return StatusCode(500, new { message = "Error exporting report" });
            }
        }
    }
}

