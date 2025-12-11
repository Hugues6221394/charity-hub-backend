using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using StudentCharityHub.Services;
using StudentCharityHub.Hubs;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<MessageHub> _messageHub;
        private readonly INotificationService _notificationService;

        public MessagesController(
            ApplicationDbContext context,
            IHubContext<MessageHub> messageHub,
            INotificationService notificationService)
        {
            _context = context;
            _messageHub = messageHub;
            _notificationService = notificationService;
        }

        // GET: api/Messages/conversations
        [HttpGet("conversations")]
        public async Task<IActionResult> GetConversations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var messages = await _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .Where(m => m.SenderId == userId || m.ReceiverId == userId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            var conversations = messages
                .GroupBy(m => m.SenderId == userId ? m.ReceiverId : m.SenderId)
                .Select(g => {
                    var otherUser = g.First().SenderId == userId ? g.First().Receiver : g.First().Sender;
                    var lastMessage = g.First();
                    return new
                    {
                        Id = otherUser.Id, // Use UserID as ConversationID
                        ParticipantName = $"{otherUser.FirstName} {otherUser.LastName}",
                        ParticipantEmail = otherUser.Email,
                        LastMessage = lastMessage.Content,
                        LastMessageDate = lastMessage.CreatedAt,
                        UnreadCount = g.Count(m => m.ReceiverId == userId && !m.IsRead)
                    };
                })
                .ToList();

            return Ok(conversations);
        }

        // GET: api/Messages/conversation/{userId}
        [HttpGet("conversation/{otherUserId}")]
        public async Task<IActionResult> GetConversation(string otherUserId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null) return Unauthorized();

            var messages = await _context.Messages
                .Include(m => m.Sender)
                .Include(m => m.Receiver)
                .Where(m => (m.SenderId == userId && m.ReceiverId == otherUserId) ||
                            (m.SenderId == otherUserId && m.ReceiverId == userId))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id,
                    m.Content,
                    m.CreatedAt,
                    IsSentByMe = m.SenderId == userId,
                    m.IsRead,
                    SenderName = $"{m.Sender.FirstName} {m.Sender.LastName}",
                    ReceiverName = $"{m.Receiver.FirstName} {m.Receiver.LastName}"
                })
                .ToListAsync();

            // Mark messages as read
            var unreadMessages = await _context.Messages
                .Where(m => m.ReceiverId == userId && m.SenderId == otherUserId && !m.IsRead)
                .ToListAsync();

            foreach (var msg in unreadMessages)
            {
                msg.IsRead = true;
                msg.ReadAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            return Ok(messages);
        }

        // POST: api/Messages/send
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            // Get sender and receiver
            var sender = await _context.Users.FindAsync(userId);
            var receiver = await _context.Users.FindAsync(dto.ReceiverId);

            if (sender == null || receiver == null)
                return NotFound(new { message = "User not found" });

            // Check if sender is a student trying to message a donor
            var senderRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToListAsync();

            var receiverRoles = await _context.UserRoles
                .Where(ur => ur.UserId == dto.ReceiverId)
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToListAsync();

            // Students can only reply to donors, not initiate conversations
            if (senderRoles.Contains("Student") && receiverRoles.Contains("Donor"))
            {
                // Check if there's an existing conversation (donor must have messaged first)
                var existingMessage = await _context.Messages
                    .AnyAsync(m => m.SenderId == dto.ReceiverId && m.ReceiverId == userId);

                if (!existingMessage)
                {
                    return Forbid("Students can only reply to messages from donors, not initiate conversations");
                }
            }

            var message = new Message
            {
                SenderId = userId,
                ReceiverId = dto.ReceiverId,
                StudentId = dto.StudentId,
                Content = dto.Content,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // Send real-time message via SignalR
            var conversationGroup = GetConversationGroupName(userId, dto.ReceiverId);
            await _messageHub.Clients.Group(conversationGroup).SendAsync("ReceiveMessage", new
            {
                message.Id,
                message.SenderId,
                message.ReceiverId,
                message.Content,
                message.CreatedAt,
                message.IsRead,
                SenderName = $"{sender.FirstName} {sender.LastName}"
            });

            // Also send to user-specific group for notifications
            await _messageHub.Clients.Group($"user_{dto.ReceiverId}").SendAsync("NewMessage", new
            {
                message.Id,
                SenderName = $"{sender.FirstName} {sender.LastName}",
                message.Content,
                message.CreatedAt
            });

            // Send notification
            await _notificationService.NotifyNewMessageAsync(dto.ReceiverId, message);

            return Ok(new { message = "Message sent successfully", id = message.Id });
        }

        private string GetConversationGroupName(string userId1, string userId2)
        {
            var ids = new[] { userId1, userId2 }.OrderBy(id => id).ToArray();
            return $"conversation_{ids[0]}_{ids[1]}";
        }

        // PUT: api/Messages/{id}/read
        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var message = await _context.Messages.FindAsync(id);

            if (message == null)
                return NotFound();

            if (message.ReceiverId != userId)
                return Forbid();

            message.IsRead = true;
            message.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Message marked as read" });
        }

        // DELETE: api/Messages/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteMessage(int id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var message = await _context.Messages.FindAsync(id);
            if (message == null)
                return NotFound();

            // Only allow deletion if user is the sender or receiver
            // Admins can delete any message
            var userRoles = await _context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Join(_context.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .ToListAsync();

            if (message.SenderId != userId && message.ReceiverId != userId && !userRoles.Contains("Admin"))
            {
                return Forbid("You can only delete your own messages");
            }

            _context.Messages.Remove(message);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Message deleted successfully" });
        }
    }

    public class SendMessageDto
    {
        [Required]
        public string ReceiverId { get; set; } = string.Empty;

        public int? StudentId { get; set; }

        [Required]
        [StringLength(1000)]
        public string Content { get; set; } = string.Empty;
    }
}
