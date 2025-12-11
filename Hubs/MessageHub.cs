using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace StudentCharityHub.Hubs
{
    [Authorize]
    public class MessageHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user_{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinConversation(string otherUserId)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                // Create a unique group for this conversation
                var groupName = GetConversationGroupName(userId, otherUserId);
                await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            }
        }

        private string GetConversationGroupName(string userId1, string userId2)
        {
            // Create consistent group name regardless of order
            var ids = new[] { userId1, userId2 }.OrderBy(id => id).ToArray();
            return $"conversation_{ids[0]}_{ids[1]}";
        }
    }
}

