using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;

namespace StudentCharityHub.Controllers.Api.Admin
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class AnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AnalyticsController> _logger;

        public AnalyticsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<AnalyticsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: api/admin/analytics/overview
        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview([FromQuery] string period = "month")
        {
            try
            {
                var now = DateTime.UtcNow;
                DateTime startDate = period switch
                {
                    "week" => now.AddDays(-7),
                    "month" => now.AddMonths(-1),
                    "year" => now.AddYears(-1),
                    _ => DateTime.MinValue
                };

                // Total users
                var totalUsers = await _userManager.Users.CountAsync();
                var newUsers = await _userManager.Users
                    .Where(u => u.CreatedAt >= startDate)
                    .CountAsync();

                // Student statistics
                var totalStudents = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.Approved)
                    .CountAsync();
                var activeStudents = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.Approved && a.IsPostedAsStudent)
                    .CountAsync();

                // Donation statistics
                var totalDonations = await _context.Donations
                    .Where(d => d.Status == "Completed")
                    .SumAsync(d => (decimal?)d.Amount) ?? 0;
                
                var periodDonations = await _context.Donations
                    .Where(d => d.Status == "Completed" && d.CreatedAt >= startDate)
                    .SumAsync(d => (decimal?)d.Amount) ?? 0;

                var previousPeriodStart = period switch
                {
                    "week" => now.AddDays(-14),
                    "month" => now.AddMonths(-2),
                    "year" => now.AddYears(-2),
                    _ => DateTime.MinValue
                };

                var previousPeriodDonations = await _context.Donations
                    .Where(d => d.Status == "Completed" && 
                           d.CreatedAt >= previousPeriodStart && 
                           d.CreatedAt < startDate)
                    .SumAsync(d => (decimal?)d.Amount) ?? 0;

                var donationGrowth = previousPeriodDonations > 0
                    ? ((periodDonations - previousPeriodDonations) / previousPeriodDonations) * 100
                    : 0;

                // Average donation
                var donationCount = await _context.Donations
                    .Where(d => d.Status == "Completed")
                    .CountAsync();
                var averageDonation = donationCount > 0 ? totalDonations / donationCount : 0;

                // Donor count
                var totalDonors = await _userManager.Users
                    .Where(u => u.UserName != null)
                    .ToListAsync();
                var donorCount = 0;
                foreach (var user in totalDonors)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    if (roles.Contains("Donor"))
                        donorCount++;
                }

                var result = new
                {
                    totalUsers,
                    newUsers,
                    totalStudents,
                    activeStudents,
                    totalDonations,
                    periodDonations,
                    donationGrowth = Math.Round(donationGrowth, 2),
                    averageDonation = Math.Round(averageDonation, 2),
                    totalDonors = donorCount,
                    period
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching analytics overview");
                return StatusCode(500, new { message = "Error fetching analytics" });
            }
        }

        // GET: api/admin/analytics/donations
        [HttpGet("donations")]
        public async Task<IActionResult> GetDonationTrends([FromQuery] string period = "month")
        {
            try
            {
                var now = DateTime.UtcNow;
                var startDate = period switch
                {
                    "week" => now.AddDays(-7),
                    "month" => now.AddMonths(-6),
                    "year" => now.AddYears(-1),
                    _ => now.AddMonths(-6)
                };

                var donations = await _context.Donations
                    .Where(d => d.Status == "Completed" && d.CreatedAt >= startDate)
                    .GroupBy(d => new { d.CreatedAt.Year, d.CreatedAt.Month })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,
                        total = g.Sum(d => d.Amount),
                        count = g.Count()
                    })
                    .OrderBy(x => x.year)
                    .ThenBy(x => x.month)
                    .ToListAsync();

                return Ok(donations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching donation trends");
                return StatusCode(500, new { message = "Error fetching donation trends" });
            }
        }

        // GET: api/admin/analytics/users
        [HttpGet("users")]
        public async Task<IActionResult> GetUserGrowth([FromQuery] string period = "month")
        {
            try
            {
                var now = DateTime.UtcNow;
                var startDate = period switch
                {
                    "week" => now.AddDays(-7),
                    "month" => now.AddMonths(-6),
                    "year" => now.AddYears(-1),
                    _ => now.AddMonths(-6)
                };

                var users = await _userManager.Users
                    .Where(u => u.CreatedAt >= startDate)
                    .GroupBy(u => new { u.CreatedAt.Year, u.CreatedAt.Month })
                    .Select(g => new
                    {
                        year = g.Key.Year,
                        month = g.Key.Month,
                        count = g.Count()
                    })
                    .OrderBy(x => x.year)
                    .ThenBy(x => x.month)
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user growth");
                return StatusCode(500, new { message = "Error fetching user growth" });
            }
        }

        // GET: api/admin/analytics/applications
        [HttpGet("applications")]
        public async Task<IActionResult> GetApplicationStats()
        {
            try
            {
                var total = await _context.StudentApplications.CountAsync();
                var pending = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.Pending)
                    .CountAsync();
                var underReview = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.UnderReview)
                    .CountAsync();
                var approved = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.Approved)
                    .CountAsync();
                var rejected = await _context.StudentApplications
                    .Where(a => a.Status == ApplicationStatus.Rejected)
                    .CountAsync();

                return Ok(new
                {
                    total,
                    pending,
                    underReview,
                    approved,
                    rejected
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching application stats");
                return StatusCode(500, new { message = "Error fetching application stats" });
            }
        }
    }
}
