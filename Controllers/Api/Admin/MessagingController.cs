using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api.Admin
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class MessagingController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;
        private readonly ILogger<MessagingController> _logger;

        public MessagingController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService,
            ILogger<MessagingController> logger)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
            _logger = logger;
        }

        // GET: api/admin/messaging/users-for-messaging
        [HttpGet("users-for-messaging")]
        public async Task<IActionResult> GetUsersForMessaging([FromQuery] string? role = null, [FromQuery] string? search = null)
        {
            try
            {
                var query = _userManager.Users.AsQueryable();

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

                    if (userRole != null && (string.IsNullOrEmpty(role) || userRole == role))
                    {
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

                return Ok(userDtos.OrderBy(u => ((dynamic)u).firstName).ThenBy(u => ((dynamic)u).lastName));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users for messaging");
                return StatusCode(500, new { message = "Error fetching users" });
            }
        }

        // POST: api/admin/messaging/send
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
        {
            var senderId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (senderId == null) return Unauthorized();

            var sender = await _userManager.FindByIdAsync(senderId);
            var receiver = await _userManager.FindByIdAsync(dto.ReceiverId);

            if (sender == null || receiver == null)
            {
                return NotFound(new { message = "Sender or receiver not found." });
            }

            var message = new Message
            {
                SenderId = senderId,
                ReceiverId = dto.ReceiverId,
                StudentId = dto.StudentId,
                Content = dto.Content,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Send email notification
            var subject = $"New Message from Admin: {sender.FirstName} {sender.LastName}";
            var body = $"Hello {receiver.FirstName},\n\n" +
                       $"You have received a new message from the Admin ({sender.FirstName} {sender.LastName}):\n\n" +
                       $"\"{dto.Content}\"\n\n" +
                       $"Please log in to the Student Charity Hub to view and respond to your messages.\n\n" +
                       $"Best regards,\nStudent Charity Hub Team";

            await _notificationService.SendEmailNotificationAsync(receiver.Email!, subject, body);
            await _notificationService.SendNotificationAsync(receiver.Id, "New Message", $"You have a new message from Admin: {sender.FirstName} {sender.LastName}", "Message", $"/messages/conversation/{senderId}");

            _logger.LogInformation("Admin {AdminId} sent message to {ReceiverId}.", senderId, dto.ReceiverId);

            return Ok(new { message = "Message sent successfully", id = message.Id });
        }

        // POST: api/admin/messaging/message-user
        [HttpPost("message-user")]
        public async Task<IActionResult> MessageUser([FromBody] MessageUserDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var receiver = await _userManager.FindByIdAsync(dto.UserId);
            if (receiver == null)
                return NotFound(new { message = "User not found" });

            var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var admin = await _userManager.FindByIdAsync(adminId);
            if (admin == null)
                return Unauthorized();

            // Create message
            var message = new Message
            {
                SenderId = adminId,
                ReceiverId = dto.UserId,
                StudentId = dto.StudentId,
                Content = dto.Message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Send email notification
            var emailSubject = $"Message from Student Charity Hub Administrator";
            var emailBody = $"Dear {receiver.FirstName},\n\n" +
                          $"You have received a message from {admin.FirstName} {admin.LastName} (Administrator):\n\n" +
                          $"{dto.Message}\n\n" +
                          $"Please log in to your account to respond.\n\n" +
                          $"Best regards,\nStudent Charity Hub Team";

            if (!string.IsNullOrEmpty(receiver.Email))
            {
                await _notificationService.SendEmailNotificationAsync(receiver.Email, emailSubject, emailBody);
            }

            // Send in-app notification
            await _notificationService.SendNotificationAsync(
                dto.UserId,
                "New Message from Administrator",
                $"You have received a message from {admin.FirstName} {admin.LastName}",
                "Info",
                $"/messages/conversation/{adminId}"
            );

            _logger.LogInformation("Admin {AdminId} sent message to user {UserId}", adminId, dto.UserId);

            return Ok(new { message = "Message sent successfully", messageId = message.Id });
        }
    }

    public class MessageUserDto
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public int? StudentId { get; set; }

        [Required]
        [StringLength(1000, MinimumLength = 10)]
        public string Message { get; set; } = string.Empty;
    }
}

