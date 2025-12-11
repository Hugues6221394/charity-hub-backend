using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StudentCharityHub.Models;
using StudentCharityHub.Repositories;
using StudentCharityHub.Services;
using StudentCharityHub.ViewModels;
using System.Security.Claims;

namespace StudentCharityHub.Controllers
{
    [Authorize]
    public class MessagesController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;
        private readonly ILogger<MessagesController> _logger;

        public MessagesController(
            IUnitOfWork unitOfWork,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            ILogger<MessagesController> logger)
        {
            _unitOfWork = unitOfWork;
            _userManager = userManager;
            _notificationService = notificationService;
            _logger = logger;
        }

        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> ContactAdmin()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var student = await _unitOfWork.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == userId);
            if (student == null)
            {
                TempData["ErrorMessage"] = "You must have a student profile to contact the admin.";
                return RedirectToAction("MyProfile", "Student");
            }

            ViewBag.Student = student;
            return View(new SendMessageViewModel { StudentId = student.Id });
        }

        [Authorize(Roles = "Student")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ContactAdmin(SendMessageViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var student = await _unitOfWork.Students.FirstOrDefaultAsync(s => s.ApplicationUserId == userId);
            if (student == null)
            {
                TempData["ErrorMessage"] = "You must have a student profile to contact the admin.";
                return RedirectToAction("MyProfile", "Student");
            }

            ViewBag.Student = student;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            var admin = admins.FirstOrDefault();
            if (admin == null)
            {
                ModelState.AddModelError(string.Empty, "No admin accounts are available.");
                return View(model);
            }

            var message = new Message
            {
                SenderId = userId,
                ReceiverId = admin.Id,
                StudentId = student.Id,
                Content = model.Content,
                IsRead = false,
                IsModerated = true,
                IsApproved = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);
            await _unitOfWork.SaveChangesAsync();

            await _notificationService.NotifyNewMessageAsync(admin.Id, message);

            TempData["SuccessMessage"] = "Your message has been sent to the admin.";
            return RedirectToAction("MyProfile", "Student");
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> ContactStudent(int studentId)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(studentId);
            if (student == null)
            {
                return NotFound();
            }

            ViewBag.Student = student;
            return View(new SendMessageViewModel { StudentId = studentId });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ContactStudent(SendMessageViewModel model)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(model.StudentId);
            if (student == null)
            {
                return NotFound();
            }

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminId == null) return RedirectToAction("Login", "Account");

            ViewBag.Student = student;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var message = new Message
            {
                SenderId = adminId,
                ReceiverId = student.ApplicationUserId,
                StudentId = student.Id,
                Content = model.Content,
                IsRead = false,
                IsModerated = true,
                IsApproved = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);
            await _unitOfWork.SaveChangesAsync();

            await _notificationService.NotifyNewMessageAsync(student.ApplicationUserId, message);

            TempData["SuccessMessage"] = "Message sent to student.";
            return RedirectToAction("Students", "Admin");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var sentMessages = await _unitOfWork.Messages.FindAsync(m => m.SenderId == userId);
            var receivedMessages = await _unitOfWork.Messages.FindAsync(m => m.ReceiverId == userId);

            // Load sender/receiver information
            var sentWithUsers = new List<Message>();
            foreach (var msg in sentMessages)
            {
                msg.Receiver = await _userManager.FindByIdAsync(msg.ReceiverId);
                if (msg.StudentId > 0)
                {
                    msg.Student = await _unitOfWork.Students.GetByIdAsync(msg.StudentId.Value);
                }
                sentWithUsers.Add(msg);
            }

            var receivedWithUsers = new List<Message>();
            foreach (var msg in receivedMessages)
            {
                msg.Sender = await _userManager.FindByIdAsync(msg.SenderId);
                if (msg.StudentId > 0)
                {
                    msg.Student = await _unitOfWork.Students.GetByIdAsync(msg.StudentId.Value);
                }
                receivedWithUsers.Add(msg);
            }

            ViewBag.SentMessages = sentWithUsers.OrderByDescending(m => m.CreatedAt).ToList();
            ViewBag.ReceivedMessages = receivedWithUsers.OrderByDescending(m => m.CreatedAt).ToList();

            return View();
        }

        [Authorize(Roles = "Donor")]
        [HttpGet]
        public async Task<IActionResult> Create(int studentId)
        {
            // Only donors can initiate messages
            var student = await _unitOfWork.Students.GetByIdAsync(studentId);
            if (student == null)
            {
                return NotFound();
            }

            ViewBag.Student = student;
            return View(new SendMessageViewModel { StudentId = studentId });
        }

        [Authorize(Roles = "Donor")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SendMessageViewModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return RedirectToAction("Login", "Account");

            var student = await _unitOfWork.Students.GetByIdAsync(model.StudentId);
            if (student == null)
            {
                return NotFound();
            }

            ViewBag.Student = student;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var message = new Message
            {
                SenderId = userId,
                ReceiverId = student.ApplicationUserId,
                StudentId = student.Id,
                Content = model.Content,
                IsRead = false,
                IsModerated = false,
                IsApproved = false,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Messages.AddAsync(message);
            await _unitOfWork.SaveChangesAsync();

            // Send notification
            await _notificationService.NotifyNewMessageAsync(message.ReceiverId, message);

            TempData["SuccessMessage"] = "Message sent. It will be reviewed by admin before delivery.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Reply(int messageId)
        {
            var originalMessage = await _unitOfWork.Messages.GetByIdAsync(messageId);
            if (originalMessage == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (originalMessage.ReceiverId != userId && originalMessage.SenderId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Load navigation properties
            originalMessage.Sender = await _userManager.FindByIdAsync(originalMessage.SenderId);
            originalMessage.Receiver = await _userManager.FindByIdAsync(originalMessage.ReceiverId);
            if (originalMessage.StudentId > 0)
            {
                originalMessage.Student = await _unitOfWork.Students.GetByIdAsync(originalMessage.StudentId.Value);
            }

            ViewBag.OriginalMessage = originalMessage;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int messageId, Message reply)
        {
            var originalMessage = await _unitOfWork.Messages.GetByIdAsync(messageId);
            if (originalMessage == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (originalMessage.ReceiverId != userId && originalMessage.SenderId != userId)
            {
                return Forbid();
            }

            if (ModelState.IsValid)
            {
                reply.SenderId = userId;
                reply.ReceiverId = originalMessage.SenderId == userId ? originalMessage.ReceiverId : originalMessage.SenderId;
                reply.StudentId = originalMessage.StudentId;
                reply.IsRead = false;
                reply.IsModerated = false;
                reply.IsApproved = false;
                reply.CreatedAt = DateTime.UtcNow;

                await _unitOfWork.Messages.AddAsync(reply);
                await _unitOfWork.SaveChangesAsync();

                // Send notification
                await _notificationService.NotifyNewMessageAsync(reply.ReceiverId, reply);

                TempData["SuccessMessage"] = "Reply sent. It will be reviewed by admin before delivery.";
                return RedirectToAction("Index");
            }

            ViewBag.OriginalMessage = originalMessage;
            return View(reply);
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (message.SenderId != userId && message.ReceiverId != userId && !User.IsInRole("Admin"))
            {
                return Forbid();
            }

            // Load navigation properties
            var sender = await _userManager.FindByIdAsync(message.SenderId);
            var receiver = await _userManager.FindByIdAsync(message.ReceiverId);

            ViewBag.Sender = sender;
            ViewBag.Receiver = receiver;

            if (message.StudentId > 0)
            {
                var student = await _unitOfWork.Students.GetByIdAsync(message.StudentId.Value);
                ViewBag.Student = student;
            }

            // Mark as read if receiver
            if (message.ReceiverId == userId && !message.IsRead)
            {
                message.IsRead = true;
                message.ReadAt = DateTime.UtcNow;
                _unitOfWork.Messages.Update(message);
                await _unitOfWork.SaveChangesAsync();
            }

            return View(message);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Moderate()
        {
            var messages = await _unitOfWork.Messages.FindAsync(m => !m.IsModerated);
            return View(messages.OrderByDescending(m => m.CreatedAt).ToList());
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMessage(int id)
        {
            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            message.IsModerated = true;
            message.IsApproved = true;
            _unitOfWork.Messages.Update(message);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Message approved.";
            return RedirectToAction("Moderate");
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectMessage(int id, string? moderatorNotes)
        {
            var message = await _unitOfWork.Messages.GetByIdAsync(id);
            if (message == null)
            {
                return NotFound();
            }

            message.IsModerated = true;
            message.IsApproved = false;
            message.ModeratorNotes = moderatorNotes;
            _unitOfWork.Messages.Update(message);
            await _unitOfWork.SaveChangesAsync();

            TempData["SuccessMessage"] = "Message rejected.";
            return RedirectToAction("Moderate");
        }
    }
}



