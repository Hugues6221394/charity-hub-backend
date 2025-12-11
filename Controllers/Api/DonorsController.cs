using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Donor")]
    public class DonorsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<DonorsController> _logger;

        public DonorsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ILogger<DonorsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        // GET: api/donors/my-students
        [HttpGet("my-students")]
        public async Task<IActionResult> GetMyStudents()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            // Get students the donor has actually donated to (completed donations only)
            var studentIds = await _context.Donations
                .Where(d => d.DonorId == userId && d.Status == "Completed")
                .Select(d => d.StudentId)
                .Distinct()
                .ToListAsync();

            var students = await _context.Students
                .Where(s => studentIds.Contains(s.Id) && s.IsVisible)
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
                    TotalDonated = _context.Donations
                        .Where(d => d.DonorId == userId && d.StudentId == s.Id && d.Status == "Completed")
                        .Sum(d => (decimal?)d.Amount) ?? 0,
                    s.CreatedAt
                })
                .OrderByDescending(s => s.TotalDonated)
                .ToListAsync();

            return Ok(students);
        }

        // GET: api/donors/my-stats
        [HttpGet("my-stats")]
        public async Task<IActionResult> GetMyStats()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var donations = await _context.Donations
                .Where(d => d.DonorId == userId && d.Status == "Completed")
                .ToListAsync();

            var totalDonated = donations.Sum(d => d.Amount);
            var studentsSponsored = donations.Select(d => d.StudentId).Distinct().Count();
            
            var thisMonth = DateTime.UtcNow.AddMonths(-1);
            var thisMonthDonations = donations
                .Where(d => d.CreatedAt >= thisMonth)
                .Sum(d => d.Amount);

            // Impact score: combination of total donated and students helped
            var impactScore = (int)(totalDonated / 10) + (studentsSponsored * 10);

            return Ok(new
            {
                totalDonated,
                studentsSponsored,
                thisMonthDonations,
                impactScore
            });
        }

        // GET: api/donors/my-donations
        [HttpGet("my-donations")]
        public async Task<IActionResult> GetMyDonations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var donations = await _context.Donations
                .Include(d => d.Student)
                .Where(d => d.DonorId == userId)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.StudentId,
                    StudentName = d.Student.FullName,
                    StudentPhoto = d.Student.PhotoUrl,
                    d.Amount,
                    d.PaymentMethod,
                    d.Status,
                    d.IsRecurring,
                    d.CreatedAt,
                    d.CompletedAt
                })
                .ToListAsync();

            return Ok(donations);
        }

        // GET: api/donors/monthly-trends
        [HttpGet("monthly-trends")]
        public async Task<IActionResult> GetMonthlyTrends()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);
            var donations = await _context.Donations
                .Where(d => d.DonorId == userId && d.Status == "Completed" && d.CreatedAt >= sixMonthsAgo)
                .GroupBy(d => new { d.CreatedAt.Year, d.CreatedAt.Month })
                .Select(g => new
                {
                    year = g.Key.Year,
                    month = g.Key.Month,
                    amount = g.Sum(d => d.Amount)
                })
                .OrderBy(x => x.year)
                .ThenBy(x => x.month)
                .ToListAsync();

            var monthNames = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
            var trends = donations.Select(d => new
            {
                month = $"{monthNames[d.month - 1]} {d.year}",
                amount = d.amount
            }).ToList();

            return Ok(trends);
        }

        // POST: api/donors/follow/{studentId}
        [HttpPost("follow/{studentId}")]
        public async Task<IActionResult> FollowStudent(int studentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var student = await _context.Students.FindAsync(studentId);
            if (student == null)
                return NotFound(new { message = "Student not found" });

            var existingFollow = await _context.Follows
                .FirstOrDefaultAsync(sf => sf.StudentId == studentId && sf.DonorId == userId);

            if (existingFollow != null)
            {
                return BadRequest(new { message = "Already following this student" });
            }

            var follow = new Follow
            {
                StudentId = studentId,
                DonorId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Follows.Add(follow);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Successfully followed student" });
        }

        // DELETE: api/donors/follow/{studentId}
        [HttpDelete("follow/{studentId}")]
        public async Task<IActionResult> UnfollowStudent(int studentId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var follow = await _context.Follows
                .FirstOrDefaultAsync(sf => sf.StudentId == studentId && sf.DonorId == userId);

            if (follow == null)
            {
                return NotFound(new { message = "Not following this student" });
            }

            _context.Follows.Remove(follow);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Successfully unfollowed student" });
        }

        // GET: api/donors/following
        [HttpGet("following")]
        public async Task<IActionResult> GetFollowing()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var following = await _context.Follows
                .Include(sf => sf.Student)
                .Where(sf => sf.DonorId == userId)
                .Select(sf => new
                {
                    sf.StudentId,
                    StudentName = sf.Student.FullName,
                    StudentPhoto = sf.Student.PhotoUrl,
                    sf.CreatedAt
                })
                .ToListAsync();

            return Ok(following);
        }

        // GET: api/donors/messaging/users-for-messaging
        [HttpGet("messaging/users-for-messaging")]
        public async Task<IActionResult> GetUsersForMessaging([FromQuery] string? role = null, [FromQuery] string? search = null)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                // Get students the donor has donated to
                var donatedStudentIds = await _context.Donations
                    .Where(d => d.DonorId == userId && d.Status == "Completed")
                    .Select(d => d.StudentId)
                    .Distinct()
                    .ToListAsync();

                var donatedStudentUserIds = await _context.Students
                    .Where(s => donatedStudentIds.Contains(s.Id))
                    .Select(s => s.ApplicationUserId)
                    .ToListAsync();

                // Get all Admins and Managers
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                var managers = await _userManager.GetUsersInRoleAsync("Manager");

                // Combine all allowed user IDs
                var allowedUserIds = donatedStudentUserIds
                    .Concat(admins.Select(a => a.Id))
                    .Concat(managers.Select(m => m.Id))
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
                            // For students, only include if donor has donated to them
                            if (userRole == "Student" && !donatedStudentUserIds.Contains(user.Id))
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
}

