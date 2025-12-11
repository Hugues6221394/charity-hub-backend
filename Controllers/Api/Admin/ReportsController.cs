using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using System.Text;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api.Admin
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
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

        // GET: api/admin/reports/students
        [HttpGet("students")]
        public async Task<IActionResult> GetStudentsReport([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var query = _context.Students.AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(s => s.CreatedAt >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(s => s.CreatedAt <= endDate.Value);

                var students = await query
                    .Include(s => s.ApplicationUser)
                    .Select(s => new
                    {
                        s.Id,
                        s.FullName,
                        s.Age,
                        s.Location,
                        s.AcademicBackground,
                        s.DreamCareer,
                        s.FundingGoal,
                        s.AmountRaised,
                        FundingProgress = s.FundingGoal > 0 ? (s.AmountRaised / s.FundingGoal) * 100 : 0,
                        s.IsVisible,
                        s.CreatedAt,
                        Email = s.ApplicationUser.Email
                    })
                    .ToListAsync();

                return Ok(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating students report");
                return StatusCode(500, new { message = "Error generating report" });
            }
        }

        // GET: api/admin/reports/donors
        [HttpGet("donors")]
        public async Task<IActionResult> GetDonorsReport([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var query = _context.Donations
                    .Where(d => d.Status == "Completed")
                    .AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(d => d.CreatedAt >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(d => d.CreatedAt <= endDate.Value);

                var donors = await query
                    .Include(d => d.Donor)
                    .GroupBy(d => d.DonorId)
                    .Select(g => new
                    {
                        DonorId = g.Key,
                        DonorName = g.First().Donor.FirstName + " " + g.First().Donor.LastName,
                        DonorEmail = g.First().Donor.Email,
                        TotalDonated = g.Sum(d => d.Amount),
                        DonationCount = g.Count(),
                        StudentsSupported = g.Select(d => d.StudentId).Distinct().Count(),
                        FirstDonation = g.Min(d => d.CreatedAt),
                        LastDonation = g.Max(d => d.CreatedAt)
                    })
                    .ToListAsync();

                return Ok(donors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating donors report");
                return StatusCode(500, new { message = "Error generating report" });
            }
        }

        // GET: api/admin/reports/donations
        [HttpGet("donations")]
        public async Task<IActionResult> GetDonationsReport([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var query = _context.Donations
                    .Where(d => d.Status == "Completed")
                    .AsQueryable();

                if (startDate.HasValue)
                    query = query.Where(d => d.CreatedAt >= startDate.Value);
                if (endDate.HasValue)
                    query = query.Where(d => d.CreatedAt <= endDate.Value);

                var donations = await query
                    .Include(d => d.Student)
                    .Include(d => d.Donor)
                    .Select(d => new
                    {
                        d.Id,
                        d.Amount,
                        d.PaymentMethod,
                        d.Status,
                        d.TransactionId,
                        d.CreatedAt,
                        d.CompletedAt,
                        StudentName = d.Student.FullName,
                        DonorName = d.Donor.FirstName + " " + d.Donor.LastName,
                        DonorEmail = d.Donor.Email
                    })
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();

                return Ok(donations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating donations report");
                return StatusCode(500, new { message = "Error generating report" });
            }
        }

        // GET: api/admin/reports/student-progress/{studentId}
        [HttpGet("student-progress/{studentId}")]
        public async Task<IActionResult> GetStudentProgressReport(int studentId)
        {
            try
            {
                var student = await _context.Students
                    .Include(s => s.ApplicationUser)
                    .Include(s => s.ProgressReports)
                    .Include(s => s.Donations.Where(d => d.Status == "Completed"))
                    .FirstOrDefaultAsync(s => s.Id == studentId);

                if (student == null)
                    return NotFound(new { message = "Student not found" });

                var report = new
                {
                    Student = new
                    {
                        student.Id,
                        student.FullName,
                        student.Age,
                        student.Location,
                        student.AcademicBackground,
                        student.DreamCareer,
                        student.FundingGoal,
                        student.AmountRaised,
                        FundingProgress = student.FundingGoal > 0 ? (student.AmountRaised / student.FundingGoal) * 100 : 0,
                        student.CreatedAt
                    },
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
                    Donations = student.Donations
                        .OrderByDescending(d => d.CreatedAt)
                        .Select(d => new
                        {
                            d.Id,
                            d.Amount,
                            d.PaymentMethod,
                            d.CreatedAt
                        }),
                    TotalDonations = student.Donations.Count,
                    TotalDonors = student.Donations.Select(d => d.DonorId).Distinct().Count()
                };

                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating student progress report");
                return StatusCode(500, new { message = "Error generating report" });
            }
        }

        // GET: api/admin/reports/export-csv
        [HttpGet("export-csv")]
        public async Task<IActionResult> ExportCSV([FromQuery] string type, [FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var csv = new StringBuilder();
                string filename;

                switch (type.ToLower())
                {
                    case "students":
                        var students = await GetStudentsReportData(startDate, endDate);
                        csv.AppendLine("Id,FullName,Age,Location,AcademicBackground,DreamCareer,FundingGoal,AmountRaised,FundingProgress,IsVisible,CreatedAt,Email");
                        foreach (var s in students)
                        {
                            csv.AppendLine($"{s.Id},\"{s.FullName}\",{s.Age},\"{s.Location}\",\"{s.AcademicBackground}\",\"{s.DreamCareer}\",{s.FundingGoal},{s.AmountRaised},{s.FundingProgress:F2},{s.IsVisible},{s.CreatedAt:yyyy-MM-dd},{s.Email}");
                        }
                        filename = $"students-report-{DateTime.UtcNow:yyyyMMdd}.csv";
                        break;

                    case "donors":
                        var donors = await GetDonorsReportData(startDate, endDate);
                        csv.AppendLine("DonorId,DonorName,DonorEmail,TotalDonated,DonationCount,StudentsSupported,FirstDonation,LastDonation");
                        foreach (var d in donors)
                        {
                            csv.AppendLine($"{d.DonorId},\"{d.DonorName}\",{d.DonorEmail},{d.TotalDonated},{d.DonationCount},{d.StudentsSupported},{d.FirstDonation:yyyy-MM-dd},{d.LastDonation:yyyy-MM-dd}");
                        }
                        filename = $"donors-report-{DateTime.UtcNow:yyyyMMdd}.csv";
                        break;

                    case "donations":
                        var donations = await GetDonationsReportData(startDate, endDate);
                        csv.AppendLine("Id,Amount,PaymentMethod,Status,TransactionId,CreatedAt,CompletedAt,StudentName,DonorName,DonorEmail");
                        foreach (var d in donations)
                        {
                            var completedAt = d.CompletedAt.HasValue ? d.CompletedAt.Value.ToString("yyyy-MM-dd") : "";
                            csv.AppendLine($"{d.Id},{d.Amount},{d.PaymentMethod},{d.Status},{d.TransactionId},{d.CreatedAt:yyyy-MM-dd},{completedAt},\"{d.StudentName}\",\"{d.DonorName}\",{d.DonorEmail}");
                        }
                        filename = $"donations-report-{DateTime.UtcNow:yyyyMMdd}.csv";
                        break;

                    default:
                        return BadRequest(new { message = "Invalid report type" });
                }

                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                return File(bytes, "text/csv", filename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting CSV");
                return StatusCode(500, new { message = "Error exporting report" });
            }
        }

        private async Task<List<StudentReportData>> GetStudentsReportData(DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Students.Include(s => s.ApplicationUser).AsQueryable();
            if (startDate.HasValue) query = query.Where(s => s.CreatedAt >= startDate.Value);
            if (endDate.HasValue) query = query.Where(s => s.CreatedAt <= endDate.Value);

            var data = await query.Select(s => new
            {
                s.Id,
                s.FullName,
                s.Age,
                s.Location,
                s.AcademicBackground,
                s.DreamCareer,
                s.FundingGoal,
                s.AmountRaised,
                FundingProgress = s.FundingGoal > 0 ? (s.AmountRaised / s.FundingGoal) * 100 : 0,
                s.IsVisible,
                s.CreatedAt,
                Email = s.ApplicationUser.Email
            }).ToListAsync();

            return data.Select(d => new StudentReportData
            {
                Id = d.Id,
                FullName = d.FullName,
                Age = d.Age,
                Location = d.Location ?? "",
                AcademicBackground = d.AcademicBackground ?? "",
                DreamCareer = d.DreamCareer ?? "",
                FundingGoal = d.FundingGoal,
                AmountRaised = d.AmountRaised,
                FundingProgress = d.FundingProgress,
                IsVisible = d.IsVisible,
                CreatedAt = d.CreatedAt,
                Email = d.Email ?? ""
            }).ToList();
        }

        private async Task<List<DonorReportData>> GetDonorsReportData(DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Donations.Where(d => d.Status == "Completed").Include(d => d.Donor).AsQueryable();
            if (startDate.HasValue) query = query.Where(d => d.CreatedAt >= startDate.Value);
            if (endDate.HasValue) query = query.Where(d => d.CreatedAt <= endDate.Value);

            var data = await query.GroupBy(d => d.DonorId).Select(g => new
            {
                DonorId = g.Key,
                DonorName = g.First().Donor.FirstName + " " + g.First().Donor.LastName,
                DonorEmail = g.First().Donor.Email,
                TotalDonated = g.Sum(d => d.Amount),
                DonationCount = g.Count(),
                StudentsSupported = g.Select(d => d.StudentId).Distinct().Count(),
                FirstDonation = g.Min(d => d.CreatedAt),
                LastDonation = g.Max(d => d.CreatedAt)
            }).ToListAsync();

            return data.Select(d => new DonorReportData
            {
                DonorId = d.DonorId,
                DonorName = d.DonorName,
                DonorEmail = d.DonorEmail ?? "",
                TotalDonated = d.TotalDonated,
                DonationCount = d.DonationCount,
                StudentsSupported = d.StudentsSupported,
                FirstDonation = d.FirstDonation,
                LastDonation = d.LastDonation
            }).ToList();
        }

        private async Task<List<DonationReportData>> GetDonationsReportData(DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Donations.Where(d => d.Status == "Completed").Include(d => d.Student).Include(d => d.Donor).AsQueryable();
            if (startDate.HasValue) query = query.Where(d => d.CreatedAt >= startDate.Value);
            if (endDate.HasValue) query = query.Where(d => d.CreatedAt <= endDate.Value);

            var data = await query.Select(d => new
            {
                d.Id,
                d.Amount,
                d.PaymentMethod,
                d.Status,
                d.TransactionId,
                d.CreatedAt,
                d.CompletedAt,
                StudentName = d.Student.FullName,
                DonorName = d.Donor.FirstName + " " + d.Donor.LastName,
                DonorEmail = d.Donor.Email
            }).OrderByDescending(d => d.CreatedAt).ToListAsync();

            return data.Select(d => new DonationReportData
            {
                Id = d.Id,
                Amount = d.Amount,
                PaymentMethod = d.PaymentMethod ?? "",
                Status = d.Status ?? "",
                TransactionId = d.TransactionId ?? "",
                CreatedAt = d.CreatedAt,
                CompletedAt = d.CompletedAt,
                StudentName = d.StudentName,
                DonorName = d.DonorName,
                DonorEmail = d.DonorEmail ?? ""
            }).ToList();
        }
    }

    public class StudentReportData
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public int Age { get; set; }
        public string Location { get; set; } = "";
        public string AcademicBackground { get; set; } = "";
        public string DreamCareer { get; set; } = "";
        public decimal FundingGoal { get; set; }
        public decimal AmountRaised { get; set; }
        public decimal FundingProgress { get; set; }
        public bool IsVisible { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Email { get; set; } = "";
    }

    public class DonorReportData
    {
        public string DonorId { get; set; } = "";
        public string DonorName { get; set; } = "";
        public string DonorEmail { get; set; } = "";
        public decimal TotalDonated { get; set; }
        public int DonationCount { get; set; }
        public int StudentsSupported { get; set; }
        public DateTime FirstDonation { get; set; }
        public DateTime LastDonation { get; set; }
    }

    public class DonationReportData
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string Status { get; set; } = "";
        public string TransactionId { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string StudentName { get; set; } = "";
        public string DonorName { get; set; } = "";
        public string DonorEmail { get; set; } = "";
    }
}

