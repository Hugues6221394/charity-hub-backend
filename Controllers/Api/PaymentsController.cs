using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            ApplicationDbContext context,
            INotificationService notificationService,
            IConfiguration configuration,
            ILogger<PaymentsController> logger)
        {
            _context = context;
            _notificationService = notificationService;
            _configuration = configuration;
            _logger = logger;
        }

        // PayPal Integration
        [HttpPost("paypal/create-order")]
        public async Task<IActionResult> CreatePayPalOrder([FromBody] CreatePaymentDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                var student = await _context.Students.FindAsync(dto.StudentId);
                if (student == null)
                    return NotFound(new { message = "Student not found" });

                // Create donation record
                var donation = new Donation
                {
                    DonorId = userId,
                    StudentId = dto.StudentId,
                    Amount = dto.Amount,
                    PaymentMethod = "PayPal",
                    Status = "Pending",
                    TransactionId = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Donations.Add(donation);
                await _context.SaveChangesAsync();

                // In production, integrate with PayPal SDK
                // For now, return a mock order ID
                var orderId = $"PAYPAL_{donation.TransactionId}";

                _logger.LogInformation("PayPal order created: {OrderId} for donation {DonationId}", orderId, donation.Id);

                return Ok(new { orderId, donationId = donation.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating PayPal order");
                return StatusCode(500, new { message = "Failed to create payment order" });
            }
        }

        [HttpPost("paypal/capture-order")]
        public async Task<IActionResult> CapturePayPalOrder([FromBody] CapturePaymentDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                // Find donation by transaction ID
                var donation = await _context.Donations
                    .Include(d => d.Student)
                    .FirstOrDefaultAsync(d => d.TransactionId == dto.OrderId.Replace("PAYPAL_", ""));

                if (donation == null)
                    return NotFound(new { message = "Donation not found" });

                // Update donation status
                donation.Status = "Completed";
                donation.CompletedAt = DateTime.UtcNow;

                // Update student's raised amount
                var student = donation.Student;
                if (student != null)
                {
                    student.AmountRaised += donation.Amount;
                    student.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                // Send notifications
                await _notificationService.SendNotificationAsync(
                    donation.DonorId,
                    "Donation Successful",
                    $"Your donation of ${donation.Amount:F2} to {student?.FullName} has been completed successfully.",
                    "Success",
                    $"/donations/{donation.Id}"
                );

                if (student != null)
                {
                    await _notificationService.SendNotificationAsync(
                        student.ApplicationUserId,
                        "New Donation Received",
                        $"You received a donation of ${donation.Amount:F2}!",
                        "Success",
                        $"/student/dashboard"
                    );
                }

                _logger.LogInformation("PayPal payment captured for donation {DonationId}", donation.Id);

                return Ok(new { message = "Payment completed successfully", donationId = donation.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error capturing PayPal payment");
                return StatusCode(500, new { message = "Failed to complete payment" });
            }
        }

        // MTN Mobile Money Integration
        [HttpPost("mtn/initiate")]
        public async Task<IActionResult> InitiateMTNPayment([FromBody] MTNPaymentDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userId == null)
                    return Unauthorized();

                var student = await _context.Students.FindAsync(dto.StudentId);
                if (student == null)
                    return NotFound(new { message = "Student not found" });

                // Create donation record
                var donation = new Donation
                {
                    DonorId = userId,
                    StudentId = dto.StudentId,
                    Amount = dto.Amount,
                    PaymentMethod = "MTN Mobile Money",
                    Status = "Pending",
                    TransactionId = Guid.NewGuid().ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                _context.Donations.Add(donation);
                await _context.SaveChangesAsync();

                // In production, integrate with MTN Mobile Money API
                // For now, simulate the request
                var transactionId = $"MTN_{donation.TransactionId}";

                _logger.LogInformation("MTN payment initiated: {TransactionId} for donation {DonationId}", transactionId, donation.Id);

                return Ok(new { 
                    transactionId, 
                    donationId = donation.Id,
                    message = "Payment request sent to your phone. Please approve to complete the transaction."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating MTN payment");
                return StatusCode(500, new { message = "Failed to initiate payment" });
            }
        }

        [HttpGet("mtn/status/{transactionId}")]
        public async Task<IActionResult> GetMTNPaymentStatus(string transactionId)
        {
            try
            {
                var donation = await _context.Donations
                    .Include(d => d.Student)
                    .FirstOrDefaultAsync(d => d.TransactionId == transactionId.Replace("MTN_", ""));

                if (donation == null)
                    return NotFound(new { message = "Transaction not found" });

                // In production, check with MTN API
                // For demo purposes, auto-complete after 30 seconds
                var timeSinceCreation = DateTime.UtcNow - donation.CreatedAt;
                if (timeSinceCreation.TotalSeconds > 30 && donation.Status == "Pending")
                {
                    donation.Status = "Completed";
                    donation.CompletedAt = DateTime.UtcNow;

                    // Update student's raised amount
                    if (donation.Student != null)
                    {
                        donation.Student.AmountRaised += donation.Amount;
                        donation.Student.UpdatedAt = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync();

                    // Send notifications
                    await _notificationService.SendNotificationAsync(
                        donation.DonorId,
                        "Donation Successful",
                        $"Your MTN Mobile Money donation of ${donation.Amount:F2} has been completed.",
                        "Success",
                        $"/donations/{donation.Id}"
                    );

                    if (donation.Student != null)
                    {
                        await _notificationService.SendNotificationAsync(
                            donation.Student.ApplicationUserId,
                            "New Donation Received",
                            $"You received a donation of ${donation.Amount:F2}!",
                            "Success",
                            $"/student/dashboard"
                        );
                    }
                }

                return Ok(new { 
                    status = donation.Status.ToUpper(),
                    amount = donation.Amount,
                    transactionId = $"MTN_{donation.TransactionId}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking MTN payment status");
                return StatusCode(500, new { message = "Failed to check payment status" });
            }
        }

        // Webhook handler for MTN callbacks (production)
        [HttpPost("mtn/webhook")]
        [AllowAnonymous]
        public async Task<IActionResult> MTNWebhook([FromBody] MTNWebhookDto dto)
        {
            try
            {
                // Verify webhook signature in production
                var donation = await _context.Donations
                    .Include(d => d.Student)
                    .FirstOrDefaultAsync(d => d.TransactionId == dto.TransactionId);

                if (donation == null)
                    return NotFound();

                donation.Status = dto.Status == "SUCCESSFUL" ? "Completed" : "Failed";
                donation.CompletedAt = DateTime.UtcNow;

                if (donation.Status == "Completed" && donation.Student != null)
                {
                    donation.Student.AmountRaised += donation.Amount;
                    donation.Student.UpdatedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MTN webhook");
                return StatusCode(500);
            }
        }

        // Get donation history
        [HttpGet("my-donations")]
        public async Task<IActionResult> GetMyDonations()
        {
            try
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
                        d.Amount,
                        d.PaymentMethod,
                        d.Status,
                        d.CreatedAt,
                        d.CompletedAt,
                        StudentName = d.Student != null ? d.Student.FullName : "Unknown",
                        StudentId = d.StudentId
                    })
                    .ToListAsync();

                return Ok(donations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching donations");
                return StatusCode(500, new { message = "Failed to fetch donations" });
            }
        }
    }

    // DTOs
    public class CreatePaymentDto
    {
        public int StudentId { get; set; }
        public decimal Amount { get; set; }
    }

    public class CapturePaymentDto
    {
        public string OrderId { get; set; } = string.Empty;
        public int StudentId { get; set; }
    }

    public class MTNPaymentDto
    {
        public int StudentId { get; set; }
        public decimal Amount { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public class MTNWebhookDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
