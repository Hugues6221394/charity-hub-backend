using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentCharityHub.DTOs;
using StudentCharityHub.Models;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/admin/[controller]")]
    [ApiController]
    [Authorize(Policy = "Users.View")] // Permission-based: allows Admin and Managers with users.view permission
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly ILogger<UsersController> _logger;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            ILogger<UsersController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _logger = logger;
        }

        // GET: api/admin/users
        [HttpGet]
        public async Task<IActionResult> GetAllUsers([FromQuery] string? role = null)
        {
            try
            {
                var users = await _userManager.Users.ToListAsync();
                var userDtos = new List<object>();

                foreach (var user in users)
                {
                    var roles = await _userManager.GetRolesAsync(user);
                    var userRole = roles.FirstOrDefault() ?? "User";

                    if (role == null || userRole == role)
                    {
                        userDtos.Add(new
                        {
                            id = user.Id,
                            firstName = user.FirstName,
                            lastName = user.LastName,
                            email = user.Email,
                            role = userRole,
                            isActive = user.IsActive,
                            createdAt = user.CreatedAt,
                            emailConfirmed = user.EmailConfirmed
                        });
                    }
                }

                return Ok(userDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users");
                return StatusCode(500, new { message = "Error fetching users" });
            }
        }

        // GET: api/admin/users/{id}
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                var roles = await _userManager.GetRolesAsync(user);

                return Ok(new
                {
                    id = user.Id,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    email = user.Email,
                    phoneNumber = user.PhoneNumber,
                    role = roles.FirstOrDefault() ?? "User",
                    isActive = user.IsActive,
                    createdAt = user.CreatedAt,
                    emailConfirmed = user.EmailConfirmed,
                    twoFactorEnabled = user.TwoFactorEnabled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user {UserId}", id);
                return StatusCode(500, new { message = "Error fetching user" });
            }
        }

        // POST: api/admin/users
        [HttpPost]
        [Authorize(Policy = "Users.Manage")] // Only users with manage permission can create
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto model)
        {
            try
            {
                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    EmailConfirmed = true,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (!result.Succeeded)
                {
                    return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                }

                // Assign role
                if (!string.IsNullOrEmpty(model.Role))
                {
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                _logger.LogInformation("Admin created user {Email} with role {Role}", user.Email, model.Role);

                return Ok(new { message = "User created successfully", userId = user.Id });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user");
                return StatusCode(500, new { message = "Error creating user" });
            }
        }

        // PUT: api/admin/users/{id}
        [HttpPut("{id}")]
        [Authorize(Policy = "Users.Manage")] // Only users with manage permission can update
        public async Task<IActionResult> UpdateUser(string id, [FromBody] UpdateUserDto model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.Email = model.Email;
                user.UserName = model.Email;

                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                {
                    return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                }

                // Update role if changed
                if (!string.IsNullOrEmpty(model.Role))
                {
                    var currentRoles = await _userManager.GetRolesAsync(user);
                    await _userManager.RemoveFromRolesAsync(user, currentRoles);
                    await _userManager.AddToRoleAsync(user, model.Role);
                }

                _logger.LogInformation("Admin updated user {UserId}", id);

                return Ok(new { message = "User updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user {UserId}", id);
                return StatusCode(500, new { message = "Error updating user" });
            }
        }

        // DELETE: api/admin/users/{id}
        [HttpDelete("{id}")]
        [Authorize(Policy = "Users.Manage")] // Only users with manage permission can delete
        public async Task<IActionResult> DeleteUser(string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                var result = await _userManager.DeleteAsync(user);

                if (!result.Succeeded)
                {
                    return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                }

                _logger.LogInformation("Admin deleted user {UserId}", id);

                return Ok(new { message = "User deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserId}", id);
                return StatusCode(500, new { message = "Error deleting user" });
            }
        }

        // POST: api/admin/users/{id}/reset-password
        [HttpPost("{id}/reset-password")]
        [Authorize(Policy = "Users.Manage")] // Only users with manage permission can reset passwords
        public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordDto model)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

                if (!result.Succeeded)
                {
                    return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                }

                _logger.LogInformation("Admin reset password for user {UserId}", id);

                return Ok(new { message = "Password reset successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for user {UserId}", id);
                return StatusCode(500, new { message = "Error resetting password" });
            }
        }

        // POST: api/admin/users/{id}/toggle-active
        [HttpPost("{id}/toggle-active")]
        [Authorize(Policy = "Users.Manage")] // Only users with manage permission can toggle active status
        public async Task<IActionResult> ToggleActive(string id)
        {
            try
            {
                var user = await _userManager.FindByIdAsync(id);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                user.IsActive = !user.IsActive;
                var result = await _userManager.UpdateAsync(user);

                if (!result.Succeeded)
                {
                    return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
                }

                _logger.LogInformation("Admin toggled active status for user {UserId} to {IsActive}", id, user.IsActive);

                return Ok(new { message = $"User {(user.IsActive ? "activated" : "deactivated")} successfully", isActive = user.IsActive });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling active status for user {UserId}", id);
                return StatusCode(500, new { message = "Error updating user status" });
            }
        }
    }

    // DTOs
    public class CreateUserDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Student";
    }

    public class UpdateUserDto
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Role { get; set; }
    }

    public class ResetPasswordDto
    {
        public string NewPassword { get; set; } = string.Empty;
    }
}
