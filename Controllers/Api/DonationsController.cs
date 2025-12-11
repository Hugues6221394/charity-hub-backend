using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class DonationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<DonationsController> _logger;

        public DonationsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            INotificationService notificationService,
            ILogger<DonationsController> logger)
        {
            _context = context;
            _userManager = userManager;
            _paymentService = paymentService;
            _notificationService = notificationService;
            _logger = logger;
        }

        // POST: api/Donations (Guest donation - no auth required)
        [HttpPost("guest")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateGuestDonation([FromBody] CreateGuestDonationDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var student = await _context.Students.FindAsync(dto.StudentId);
            if (student == null || !student.IsVisible)
                return NotFound(new { message = "Student not found or not visible" });

            // Create or get guest donor - register as "Unknown"
            var guestEmail = dto.DonorEmail ?? $"unknown_{Guid.NewGuid()}@studentcharityhub.com";
            var guest = await _context.Users.FirstOrDefaultAsync(u => u.Email == guestEmail);
            
            if (guest == null)
            {
                guest = new ApplicationUser
                {
                    UserName = guestEmail,
                    Email = guestEmail,
                    FirstName = dto.DonorFirstName ?? "Unknown",
                    LastName = dto.DonorLastName ?? "Donor",
                    EmailConfirmed = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                var createResult = await _userManager.CreateAsync(guest);
                if (!createResult.Succeeded)
                {
                    return BadRequest(new { message = "Failed to process donation" });
                }
            }

            var donation = new Donation
            {
                StudentId = dto.StudentId,
                DonorId = guest.Id,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();

            // Process payment
            PaymentResult result;
            if (dto.PaymentMethod.ToLower() == "paypal")
            {
                // For PayPal, we'll process it via the frontend PayPal buttons
                // Just return the donation ID for the frontend to use
                return Ok(new
                {
                    id = donation.Id,
                    status = donation.Status,
                    message = "Donation created. Please complete payment via PayPal."
                });
            }
            else if (dto.PaymentMethod.ToLower() == "mtn")
            {
                result = await _paymentService.ProcessMTNMobileMoneyPaymentAsync(donation, dto.PhoneNumber ?? string.Empty);
                
                if (result.Success)
                {
                    donation.Status = "Completed";
                    donation.TransactionId = result.TransactionId;
                    student.AmountRaised += donation.Amount;
                    await _context.SaveChangesAsync();

                    // Notify student
                    await _notificationService.SendNotificationAsync(
                        student.ApplicationUserId,
                        "New Donation Received",
                        $"You received a donation of ${donation.Amount} from {guest.FirstName} {guest.LastName}",
                        "Success",
                        $"/students/{student.Id}"
                    );

                    // Notify all admins
                    var admins = await _userManager.GetUsersInRoleAsync("Admin");
                    foreach (var admin in admins)
                    {
                        await _notificationService.SendNotificationAsync(
                            admin.Id,
                            "New Donation Completed",
                            $"A donation of ${donation.Amount} was made to {student.FullName} by {guest.FirstName} {guest.LastName}",
                            "Success",
                            "/admin/donations"
                        );
                    }
                }
                
                return Ok(new
                {
                    id = donation.Id,
                    status = donation.Status,
                    transactionId = result.TransactionId,
                    message = result.Success ? "Donation processed successfully" : result.ErrorMessage
                });
            }
            else
            {
                return BadRequest(new { message = "Invalid payment method" });
            }
        }

        // POST: api/Donations
        [HttpPost]
        [Authorize(Roles = "Donor")]
        public async Task<IActionResult> CreateDonation([FromBody] CreateDonationDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var student = await _context.Students.FindAsync(dto.StudentId);
            if (student == null)
                return NotFound(new { message = "Student not found" });

            var donation = new Donation
            {
                StudentId = dto.StudentId,
                DonorId = userId,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                Status = "Pending",
                IsRecurring = dto.IsRecurring,
                CreatedAt = DateTime.UtcNow
            };

            _context.Donations.Add(donation);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Donation {DonationId} created by donor {DonorId} for student {StudentId}",
                donation.Id, userId, dto.StudentId);

            return CreatedAtAction(nameof(GetDonation), new { id = donation.Id }, new
            {
                donation.Id,
                donation.StudentId,
                donation.Amount,
                donation.PaymentMethod,
                donation.Status,
                donation.CreatedAt
            });
        }

        // GET: api/Donations/{id}
        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<IActionResult> GetDonation(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var donation = await _context.Donations
                .Include(d => d.Student)
                .Include(d => d.Donor)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (donation == null)
                return NotFound();

            // Allow anonymous access to check donation status (for guest donations)
            // But restrict full details to donor, student, or admin
            if (userId == null)
            {
                // For anonymous users, only return basic status info
                return Ok(new
                {
                    id = donation.Id,
                    status = donation.Status,
                    amount = donation.Amount,
                    createdAt = donation.CreatedAt
                });
            }

            // For authenticated users, check permissions
            if (!User.IsInRole("Admin") &&
                donation.DonorId != userId &&
                donation.Student.ApplicationUserId != userId)
            {
                return Forbid();
            }

            return Ok(new
            {
                donation.Id,
                donation.StudentId,
                StudentName = donation.Student.FullName,
                donation.Amount,
                donation.PaymentMethod,
                donation.TransactionId,
                donation.Status,
                donation.IsRecurring,
                donation.NextRecurringDate,
                donation.ReceiptUrl,
                donation.CreatedAt,
                donation.CompletedAt
            });
        }

        // GET: api/Donations/my-donations
        [HttpGet("my-donations")]
        [Authorize(Roles = "Donor")]
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

            var totalDonated = donations
                .Where(d => d.Status == "Completed")
                .Sum(d => d.Amount);

            var studentsSupported = donations
                .Where(d => d.Status == "Completed")
                .Select(d => d.StudentId)
                .Distinct()
                .Count();

            return Ok(new
            {
                donations,
                totalDonated,
                studentsSupported,
                totalDonations = donations.Count
            });
        }

        // PUT: api/Donations/{id}/complete (for guest donations after PayPal approval)
        [HttpPut("{id}/complete")]
        [AllowAnonymous]
        public async Task<IActionResult> CompleteGuestDonation(int id)
        {
            var donation = await _context.Donations
                .Include(d => d.Student)
                .ThenInclude(s => s.ApplicationUser)
                .Include(d => d.Donor)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (donation == null)
                return NotFound();

            if (donation.Status == "Completed")
                return Ok(new { message = "Donation already completed", donation });

            // Complete the donation
            donation.Status = "Completed";
            donation.CompletedAt = DateTime.UtcNow;
            
            // Update student's raised amount
            if (donation.Student != null)
            {
                donation.Student.AmountRaised += donation.Amount;
                donation.Student.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Notify student
            if (donation.Student != null)
            {
                var donorName = donation.Donor != null ? $"{donation.Donor.FirstName} {donation.Donor.LastName}" : "Unknown Donor";
                await _notificationService.SendNotificationAsync(
                    donation.Student.ApplicationUserId,
                    "New Donation Received",
                    $"You received a donation of ${donation.Amount} from {donorName}",
                    "Success",
                    $"/students/{donation.Student.Id}"
                );

                // Notify all admins
                var admins = await _userManager.GetUsersInRoleAsync("Admin");
                foreach (var admin in admins)
                {
                    await _notificationService.SendNotificationAsync(
                        admin.Id,
                        "New Donation Completed",
                        $"A donation of ${donation.Amount} was made to {donation.Student.FullName} by {donorName}",
                        "Success",
                        "/admin/donations"
                    );
                }
            }

            return Ok(new { message = "Donation completed successfully", donation });
        }

        // POST: api/Donations/{id}/process-payment
        [HttpPost("{id}/process-payment")]
        [Authorize(Roles = "Donor")]
        public async Task<IActionResult> ProcessPayment(int id, [FromBody] ProcessPaymentDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var donation = await _context.Donations
                .Include(d => d.Student)
                .ThenInclude(s => s.ApplicationUser)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (donation == null)
                return NotFound();

            if (donation.DonorId != userId)
                return Forbid();

            if (donation.Status != "Pending")
                return BadRequest(new { message = "Donation has already been processed" });

            try
            {
                PaymentResult result;

                // Process payment based on method
                if (donation.PaymentMethod.ToLower() == "paypal")
                {
                    result = await _paymentService.ProcessPayPalPaymentAsync(
                        donation,
                        dto.ReturnUrl ?? "https://localhost:7000/donation/success",
                        dto.CancelUrl ?? "https://localhost:7000/donation/cancel");
                }
                else if (donation.PaymentMethod.ToLower() == "mtn")
                {
                    result = await _paymentService.ProcessMTNMobileMoneyPaymentAsync(
                        donation,
                        dto.PhoneNumber ?? string.Empty);
                }
                else
                {
                    return BadRequest(new { message = "Invalid payment method" });
                }

                if (result.Success)
                {
                    donation.Status = "Completed";
                    donation.TransactionId = result.TransactionId;
                    donation.CompletedAt = DateTime.UtcNow;

                    // Update student's raised amount
                    donation.Student.AmountRaised += donation.Amount;

                    // Create payment log
                    var paymentLog = new PaymentLog
                    {
                        StudentId = donation.StudentId,
                        DonationId = donation.Id,
                        Amount = donation.Amount,
                        PaymentMethod = donation.PaymentMethod,
                        TransactionId = result.TransactionId ?? string.Empty,
                        Status = "Success",
                        CompletedAt = DateTime.UtcNow
                    };
                    _context.PaymentLogs.Add(paymentLog);

                    await _context.SaveChangesAsync();

                    // Notify student
                    var donor = await _userManager.FindByIdAsync(donation.DonorId);
                    if (donor != null && donation.Student != null && donation.Student.ApplicationUser != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            donation.Student.ApplicationUserId,
                            "New Donation Received",
                            $"You received a donation of ${donation.Amount} from {donor.FirstName} {donor.LastName}",
                            "Success",
                            $"/students/{donation.Student.Id}"
                        );

                        // Send email notification
                        if (!string.IsNullOrEmpty(donation.Student.ApplicationUser.Email))
                        {
                            await _notificationService.SendEmailNotificationAsync(
                                donation.Student.ApplicationUser.Email,
                                "New Donation Received!",
                                $"You have received a donation of ${donation.Amount} from {donor.FirstName} {donor.LastName}!");
                        }
                    }

                    // Notify all admins
                    var admins = await _userManager.GetUsersInRoleAsync("Admin");
                    foreach (var admin in admins)
                    {
                        var donorName = donor != null ? $"{donor.FirstName} {donor.LastName}" : "Guest Donor";
                        var studentName = donation.Student?.FullName ?? "Unknown Student";
                        await _notificationService.SendNotificationAsync(
                            admin.Id,
                            "New Donation Completed",
                            $"A donation of ${donation.Amount} was made to {studentName} by {donorName}",
                            "Success",
                            "/admin/donations"
                        );
                    }

                    _logger.LogInformation("Payment processed successfully for donation {DonationId}", id);

                    return Ok(new
                    {
                        message = "Payment processed successfully",
                        transactionId = result.TransactionId,
                        donation.Status,
                        paymentUrl = result.PaymentUrl
                    });
                }
                else
                {
                    donation.Status = "Failed";
                    
                    // Create failed payment log
                    var paymentLog = new PaymentLog
                    {
                        StudentId = donation.StudentId,
                        DonationId = donation.Id,
                        Amount = donation.Amount,
                        PaymentMethod = donation.PaymentMethod,
                        TransactionId = result.TransactionId ?? "FAILED",
                        Status = "Failed",
                        ErrorMessage = result.ErrorMessage
                    };
                    _context.PaymentLogs.Add(paymentLog);
                    
                    await _context.SaveChangesAsync();

                    return BadRequest(new { message = result.ErrorMessage ?? "Payment processing failed" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment for donation {DonationId}", id);
                return StatusCode(500, new { message = "An error occurred while processing payment" });
            }
        }

        // GET: api/Donations/statistics
        [HttpGet("statistics")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetStatistics()
        {
            var totalDonations = await _context.Donations
                .Where(d => d.Status == "Completed")
                .SumAsync(d => d.Amount);

            var donationCount = await _context.Donations
                .Where(d => d.Status == "Completed")
                .CountAsync();

            var uniqueDonors = await _context.Donations
                .Where(d => d.Status == "Completed")
                .Select(d => d.DonorId)
                .Distinct()
                .CountAsync();

            var studentsSupported = await _context.Donations
                .Where(d => d.Status == "Completed")
                .Select(d => d.StudentId)
                .Distinct()
                .CountAsync();

            var recentDonations = await _context.Donations
                .Include(d => d.Student)
                .Include(d => d.Donor)
                .Where(d => d.Status == "Completed")
                .OrderByDescending(d => d.CompletedAt)
                .Take(10)
                .Select(d => new
                {
                    d.Id,
                    DonorName = $"{d.Donor.FirstName} {d.Donor.LastName}",
                    StudentName = d.Student.FullName,
                    d.Amount,
                    d.CompletedAt
                })
                .ToListAsync();

            return Ok(new
            {
                totalDonations,
                donationCount,
                uniqueDonors,
                studentsSupported,
                averageDonation = donationCount > 0 ? totalDonations / donationCount : 0,
                recentDonations
            });
        }
    }

    public class CreateDonationDto
    {
        [Required]
        public int StudentId { get; set; }

        [Required]
        [Range(1, 999999)]
        public decimal Amount { get; set; }

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;

        public bool IsRecurring { get; set; } = false;
    }

    public class ProcessPaymentDto
    {
        public string? PhoneNumber { get; set; }
        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }
    }

    public class CreateGuestDonationDto
    {
        [Required]
        public int StudentId { get; set; }

        [Required]
        [Range(1, 999999)]
        public decimal Amount { get; set; }

        [Required]
        public string PaymentMethod { get; set; } = string.Empty;

        public string? Message { get; set; }

        public string? DonorFirstName { get; set; }

        public string? DonorLastName { get; set; }

        [EmailAddress]
        public string? DonorEmail { get; set; }

        public string? PhoneNumber { get; set; }
    }
}
