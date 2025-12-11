using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StudentCharityHub.Models;
using StudentCharityHub.Repositories;
using StudentCharityHub.Services;
using StudentCharityHub.Extensions;
using System.Security.Claims;

namespace StudentCharityHub.Controllers
{
    public class DonationController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentService _paymentService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<DonationController> _logger;

        public DonationController(
            IUnitOfWork unitOfWork,
            UserManager<ApplicationUser> userManager,
            IPaymentService paymentService,
            INotificationService notificationService,
            ILogger<DonationController> logger)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _paymentService = paymentService;
            _notificationService = notificationService;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Create(int studentId)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(studentId);
            if (student == null || !student.IsVisible)
            {
                return NotFound();
            }

            ViewBag.Student = student;
            // Pre-populate StudentId so it binds on POST
            var model = new Donation
            {
                StudentId = studentId
            };
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Donation donation, string paymentMethod, string? phoneNumber)
        {
            _logger.LogInformation("Donation form submitted for student {StudentId} via {PaymentMethod}.", donation.StudentId, paymentMethod);

            // Map non-bound fields before validation
            donation.PaymentMethod = paymentMethod;

            // These are set server-side, so remove from ModelState before validation
            ModelState.Remove(nameof(donation.DonorId));
            ModelState.Remove(nameof(donation.Donor));
            ModelState.Remove(nameof(donation.Student));
            ModelState.Remove(nameof(donation.PaymentLogs));

            if (donation.StudentId <= 0)
            {
                ModelState.AddModelError(nameof(donation.StudentId), "Invalid student selection.");
            }

            if (ModelState.IsValid)
            {
                // Determine donor (logged-in user or guest)
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    var guest = await _userManager.FindByEmailAsync("guest@studentcharityhub.com");
                    if (guest == null)
                    {
                        guest = new ApplicationUser
                        {
                            UserName = "guest@studentcharityhub.com",
                            Email = "guest@studentcharityhub.com",
                            FirstName = "Guest",
                            LastName = "Donor",
                            EmailConfirmed = false,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        var createGuestResult = await _userManager.CreateAsync(guest);
                        if (!createGuestResult.Succeeded)
                        {
                            _logger.LogError("Failed to create guest donor user: {Errors}", string.Join(", ", createGuestResult.Errors.Select(e => e.Description)));
                            ModelState.AddModelError(string.Empty, "Unable to process donation at the moment. Please try again later.");
                            ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                            return View(donation);
                        }
                    }

                    donation.DonorId = guest.Id;
                    _logger.LogInformation("Processing anonymous donation using guest donor user {GuestId}.", guest.Id);
                }
                else
                {
                    donation.DonorId = userId;

                    // Prevent a student from donating to themselves
                    var selfStudent = await _unitOfWork.Students.FirstOrDefaultAsync(
                        s => s.Id == donation.StudentId && s.ApplicationUserId == userId);
                    if (selfStudent != null)
                    {
                        ModelState.AddAllErrors(new Dictionary<string, string>
                        {
                            { "", "You cannot donate to your own student profile." }
                        });
                        ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                        return View(donation);
                    }
                }

                donation.Status = "Pending";
                donation.CreatedAt = DateTime.UtcNow;

                await _unitOfWork.Donations.AddAsync(donation);
                await _unitOfWork.SaveChangesAsync();

                // Process payment
                var returnUrl = Url.Action("PayPalSuccess", "Donation", new { donationId = donation.Id }, Request.Scheme);
                var cancelUrl = Url.Action("PayPalCancel", "Donation", new { donationId = donation.Id }, Request.Scheme);

                Services.PaymentResult result;
                if (paymentMethod == "PayPal")
                {
                    result = await _paymentService.ProcessPayPalPaymentAsync(donation, returnUrl!, cancelUrl!);
                }
                else if (paymentMethod == "MTNMobileMoney")
                {
                    if (string.IsNullOrEmpty(phoneNumber))
                    {
                        ModelState.AddModelError("", "Phone number is required for MTN Mobile Money.");
                        ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                        return View(donation);
                    }
                    result = await _paymentService.ProcessMTNMobileMoneyPaymentAsync(donation, phoneNumber);
                }
                else
                {
                    ModelState.AddModelError("", "Invalid payment method.");
                    ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                    return View(donation);
                }

                if (result.Success && !string.IsNullOrEmpty(result.PaymentUrl))
                {
                    return Redirect(result.PaymentUrl);
                }
                else if (result.Success)
                {
                    // Payment processed synchronously (e.g., MTN Mobile Money)
                    donation.Status = "Completed";
                    donation.CompletedAt = DateTime.UtcNow;
                    donation.TransactionId = result.TransactionId;
                    _unitOfWork.Donations.Update(donation);

                    // Update student amount raised
                    var student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                    if (student != null)
                    {
                        student.AmountRaised += donation.Amount;
                        _unitOfWork.Students.Update(student);
                    }

                    await _unitOfWork.SaveChangesAsync();

                    // Generate receipt
                    var receiptUrl = await _paymentService.GenerateReceiptAsync(donation);

                    // Send notifications
                    await _notificationService.NotifyDonationConfirmationAsync(donation);

                    TempData["SuccessMessage"] = "Donation completed successfully!";
                    return RedirectToAction("Details", new { id = donation.Id });
                }
                else
                {
                    ModelState.AddModelError("", result.ErrorMessage ?? "Payment processing failed.");
                    ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                    return View(donation);
                }
            }

            // If we reach here, ModelState was invalid
            if (!ModelState.IsValid)
            {
                foreach (var entry in ModelState)
                {
                    foreach (var error in entry.Value.Errors)
                    {
                        _logger.LogWarning("Donation Create ModelState error on '{Key}': {ErrorMessage}", entry.Key, error.ErrorMessage);
                    }
                }
            }

            ViewBag.Student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
            return View(donation);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var donation = await _unitOfWork.Donations.GetByIdAsync(id);
            if (donation == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (donation.DonorId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            return View(donation);
        }

        [HttpGet]
        public async Task<IActionResult> PayPalSuccess(int donationId)
        {
            var donation = await _unitOfWork.Donations.GetByIdAsync(donationId);
            if (donation == null)
            {
                return NotFound();
            }

            // Verify payment with PayPal
            var transactionId = Request.Query["token"].ToString();
            if (!string.IsNullOrEmpty(transactionId))
            {
                var result = await _paymentService.VerifyPaymentAsync(transactionId, "PayPal");
                if (result.Success)
                {
                    donation.Status = "Completed";
                    donation.CompletedAt = DateTime.UtcNow;
                    donation.TransactionId = transactionId;
                    _unitOfWork.Donations.Update(donation);

                    // Update student amount raised
                    var student = await _unitOfWork.Students.GetByIdAsync(donation.StudentId);
                    if (student != null)
                    {
                        student.AmountRaised += donation.Amount;
                        _unitOfWork.Students.Update(student);
                    }

                    await _unitOfWork.SaveChangesAsync();

                    // Generate receipt
                    await _paymentService.GenerateReceiptAsync(donation);

                    // Send notifications
                    await _notificationService.NotifyDonationConfirmationAsync(donation);

                    TempData["SuccessMessage"] = "Donation completed successfully!";
                }
            }

            return RedirectToAction("Details", new { id = donationId });
        }

        [HttpGet]
        public async Task<IActionResult> PayPalCancel(int donationId)
        {
            var donation = await _unitOfWork.Donations.GetByIdAsync(donationId);
            if (donation != null)
            {
                donation.Status = "Cancelled";
                _unitOfWork.Donations.Update(donation);
                await _unitOfWork.SaveChangesAsync();
            }

            TempData["ErrorMessage"] = "Payment was cancelled.";
            return RedirectToAction("Create", new { studentId = donation?.StudentId ?? 0 });
        }

        [Authorize(Roles = "Donor")]
        [HttpGet]
        public async Task<IActionResult> MyDonations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var donations = await _unitOfWork.Donations.FindAsync(d => d.DonorId == userId);
            return View(donations.OrderByDescending(d => d.CreatedAt).ToList());
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var donations = await _unitOfWork.Donations.GetAllAsync();
            return View(donations.OrderByDescending(d => d.CreatedAt).ToList());
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var donation = await _unitOfWork.Donations.GetByIdAsync(id);
            if (donation == null)
            {
                return NotFound();
            }

            _unitOfWork.Donations.Remove(donation);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Donation deleted successfully.";
            return RedirectToAction("Index");
        }
    }
}

