using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StudentCharityHub.DTOs;
using StudentCharityHub.Models;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FirstName = model.FirstName,
                LastName = model.LastName,
                CreatedAt = DateTime.UtcNow,
                EmailConfirmed = true // Auto-confirm for now
            };

            var result = await _userManager.CreateAsync(user, model.Password);

            if (!result.Succeeded)
            {
                return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
            }

            // Assign role
            var role = model.Role == "Student" ? "Student" : "Donor";
            await _userManager.AddToRoleAsync(user, role);

            _logger.LogInformation("User {Email} registered successfully with role {Role}", user.Email, role);

            // Generate token
            var token = await GenerateJwtToken(user);

            return Ok(new LoginResponseDto
            {
                Token = token.Token,
                RefreshToken = token.RefreshToken,
                Expiration = token.Expiration,
                User = await GetUserDto(user)
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            if (!user.IsActive)
            {
                return Unauthorized(new { message = "Your account has been deactivated" });
            }

            var result = await _signInManager.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                return Unauthorized(new { message = "Account locked due to multiple failed login attempts" });
            }

            if (!result.Succeeded)
            {
                return Unauthorized(new { message = "Invalid email or password" });
            }

            // Check if 2FA is enabled
            if (await _userManager.GetTwoFactorEnabledAsync(user))
            {
                return Ok(new { requiresTwoFactor = true, userId = user.Id });
            }

            _logger.LogInformation("User {Email} logged in successfully", user.Email);

            var token = await GenerateJwtToken(user);

            return Ok(new LoginResponseDto
            {
                Token = token.Token,
                RefreshToken = token.RefreshToken,
                Expiration = token.Expiration,
                User = await GetUserDto(user)
            });
        }

        [HttpPost("verify-2fa")]
        public async Task<IActionResult> VerifyTwoFactor([FromBody] TwoFactorVerifyDto model)
        {
            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null)
            {
                return Unauthorized(new { message = "Invalid request" });
            }

            var code = model.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user,
                _userManager.Options.Tokens.AuthenticatorTokenProvider,
                code);

            if (!isValid)
            {
                return Unauthorized(new { message = "Invalid authentication code" });
            }

            var token = await GenerateJwtToken(user);

            return Ok(new LoginResponseDto
            {
                Token = token.Token,
                RefreshToken = token.RefreshToken,
                Expiration = token.Expiration,
                User = await GetUserDto(user)
            });
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetCurrentUser()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            return Ok(await GetUserDto(user));
        }

        [Authorize]
        [HttpPost("logout")]
        public IActionResult Logout()
        {
            // With JWT, logout is handled client-side by removing the token
            // Optionally, implement token blacklisting here
            return Ok(new { message = "Logged out successfully" });
        }

        [Authorize]
        [HttpPut("update-profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;
            if (!string.IsNullOrEmpty(dto.PhoneNumber))
            {
                user.PhoneNumber = dto.PhoneNumber;
            }

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return BadRequest(new { message = "Failed to update profile", errors = result.Errors });
            }

            return Ok(new { message = "Profile updated successfully" });
        }

        [Authorize]
        [HttpPut("notification-preferences")]
        public async Task<IActionResult> UpdateNotificationPreferences([FromBody] NotificationPreferencesDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            // Store preferences as JSON in a custom field or use Identity's UserClaims
            // For now, we'll use a simple approach - store in UserClaims
            // Remove existing notification preference claims
            var existingClaims = (await _userManager.GetClaimsAsync(user))
                .Where(c => c.Type.StartsWith("NotificationPreference:"))
                .ToList();

            foreach (var claim in existingClaims)
            {
                await _userManager.RemoveClaimAsync(user, claim);
            }

            // Add new preference claims
            await _userManager.AddClaimAsync(user, new Claim("NotificationPreference:EmailNotifications", dto.EmailNotifications.ToString() ?? "True"));
            await _userManager.AddClaimAsync(user, new Claim("NotificationPreference:NewApplications", dto.NewApplications.ToString() ?? "True"));
            await _userManager.AddClaimAsync(user, new Claim("NotificationPreference:NewDonations", dto.NewDonations.ToString() ?? "True"));
            await _userManager.AddClaimAsync(user, new Claim("NotificationPreference:NewMessages", dto.NewMessages.ToString() ?? "True"));
            await _userManager.AddClaimAsync(user, new Claim("NotificationPreference:WeeklyReports", dto.WeeklyReports.ToString() ?? "False"));

            _logger.LogInformation("User {UserId} updated notification preferences", userId);

            return Ok(new { message = "Notification preferences updated successfully" });
        }

        [Authorize]
        [HttpGet("notification-preferences")]
        public async Task<IActionResult> GetNotificationPreferences()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userId == null)
                return Unauthorized();

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var claims = await _userManager.GetClaimsAsync(user);
            var preferences = new NotificationPreferencesDto
            {
                EmailNotifications = claims.FirstOrDefault(c => c.Type == "NotificationPreference:EmailNotifications")?.Value == "True" || true,
                NewApplications = claims.FirstOrDefault(c => c.Type == "NotificationPreference:NewApplications")?.Value == "True" || true,
                NewDonations = claims.FirstOrDefault(c => c.Type == "NotificationPreference:NewDonations")?.Value == "True" || true,
                NewMessages = claims.FirstOrDefault(c => c.Type == "NotificationPreference:NewMessages")?.Value == "True" || true,
                WeeklyReports = claims.FirstOrDefault(c => c.Type == "NotificationPreference:WeeklyReports")?.Value == "True" || false
            };

            return Ok(preferences);
        }

        private async Task<(string Token, string RefreshToken, DateTime Expiration)> GenerateJwtToken(ApplicationUser user)
        {
            var jwtSettings = _configuration.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey not configured");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var roles = await _userManager.GetRolesAsync(user);
            var userClaims = await _userManager.GetClaimsAsync(user);
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Add all user claims (including permission claims)
            claims.AddRange(userClaims);

            var expirationMinutes = int.Parse(jwtSettings["ExpirationMinutes"] ?? "60");
            var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"],
                audience: jwtSettings["Audience"],
                claims: claims,
                expires: expiration,
                signingCredentials: credentials
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            var refreshToken = Guid.NewGuid().ToString(); // Simplified refresh token

            return (tokenString, refreshToken, expiration);
        }

        private async Task<UserDto> GetUserDto(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);
            return new UserDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                ProfilePictureUrl = user.ProfilePictureUrl,
                Role = roles.FirstOrDefault() ?? "User",
                TwoFactorEnabled = await _userManager.GetTwoFactorEnabledAsync(user)
            };
        }
    }

    public class TwoFactorVerifyDto
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Code { get; set; } = string.Empty;
    }

    public class UpdateProfileDto
    {
        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }
    }

    public class NotificationPreferencesDto
    {
        public bool EmailNotifications { get; set; } = true;
        public bool NewApplications { get; set; } = true;
        public bool NewDonations { get; set; } = true;
        public bool NewMessages { get; set; } = true;
        public bool WeeklyReports { get; set; } = false;
    }
}
