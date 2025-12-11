using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.Data;
using StudentCharityHub.Models;
using System.Security.Claims;
using static StudentCharityHub.PermissionCatalogLocal;

namespace StudentCharityHub.Controllers.Api.Admin
{
    [Route("api/admin/permissions")]
    [ApiController]
    [Authorize(Roles = "Admin")] // Temporarily use role-based auth until permission claims are seeded
    public class PermissionsController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _context;
        private const string PermissionClaimType = "permission";

        public PermissionsController(UserManager<ApplicationUser> userManager, ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserPermissions(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "User not found" });

            var claims = await _userManager.GetClaimsAsync(user);
            var permissionClaims = claims.Where(c => c.Type == PermissionClaimType).Select(c => c.Value).ToList();

            return Ok(new
            {
                user = new { user.Id, user.FirstName, user.LastName, user.Email },
                permissions = PermissionSeeder.AllPermissions.ToList(),
                userPermissions = permissionClaims
            });
        }

        public class UpdatePermissionsRequest
        {
            public List<string> PermissionsToAdd { get; set; } = new();
            public List<string> PermissionsToRemove { get; set; } = new();
        }

        [HttpPost("{userId}")]
        public async Task<IActionResult> UpdateUserPermissions(string userId, [FromBody] UpdatePermissionsRequest request)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound(new { message = "User not found" });

            var existingClaims = await _userManager.GetClaimsAsync(user);
            var existingPermissions = existingClaims.Where(c => c.Type == PermissionClaimType).Select(c => c.Value).ToHashSet();

            var added = new List<string>();
            var removed = new List<string>();

            foreach (var perm in request.PermissionsToAdd.Distinct())
            {
                if (!existingPermissions.Contains(perm))
                {
                    await _userManager.AddClaimAsync(user, new Claim(PermissionClaimType, perm));
                    added.Add(perm);
                }
            }

            foreach (var perm in request.PermissionsToRemove.Distinct())
            {
                var claim = existingClaims.FirstOrDefault(c => c.Type == PermissionClaimType && c.Value == perm);
                if (claim != null)
                {
                    await _userManager.RemoveClaimAsync(user, claim);
                    removed.Add(perm);
                }
            }

            // Audit log
            if (added.Any() || removed.Any())
            {
                var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";
                var changes = $"Added: {string.Join(',', added)} | Removed: {string.Join(',', removed)}";
                _context.PermissionAuditLogs.Add(new PermissionAuditLog
                {
                    AdminUserId = adminId,
                    TargetUserId = user.Id,
                    Changes = changes,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                added,
                removed
            });
        }

        [HttpGet("audit")]
        public async Task<IActionResult> GetAuditLog()
        {
            var logs = await _context.PermissionAuditLogs
                .OrderByDescending(l => l.CreatedAt)
                .Take(100)
                .ToListAsync();

            return Ok(logs);
        }
    }
}

