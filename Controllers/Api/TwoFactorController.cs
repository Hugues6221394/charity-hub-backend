using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using StudentCharityHub.Models;

namespace StudentCharityHub.Controllers.Api
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TwoFactorController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<TwoFactorController> _logger;

        public TwoFactorController(
            UserManager<ApplicationUser> userManager,
            ILogger<TwoFactorController> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        // POST: api/twofactor/setup
        [HttpPost("setup")]
        public async Task<IActionResult> Setup()
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                // Generate authenticator key
                await _userManager.ResetAuthenticatorKeyAsync(user);
                var key = await _userManager.GetAuthenticatorKeyAsync(user);

                // Generate QR code
                var qrCodeUri = GenerateQrCodeUri(user.Email!, key!);
                var qrCodeImage = GenerateQrCode(qrCodeUri);

                return Ok(new
                {
                    key,
                    qrCodeImage,
                    qrCodeUri
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting up 2FA");
                return StatusCode(500, new { message = "Error setting up 2FA" });
            }
        }

        // POST: api/twofactor/enable
        [HttpPost("enable")]
        public async Task<IActionResult> Enable([FromBody] Enable2FADto model)
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                // Verify the code
                var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    model.Code);

                if (!isValid)
                    return BadRequest(new { message = "Invalid verification code" });

                // Enable 2FA
                await _userManager.SetTwoFactorEnabledAsync(user, true);

                // Generate recovery codes
                var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

                _logger.LogInformation("2FA enabled for user {UserId}", userId);

                return Ok(new
                {
                    message = "Two-factor authentication enabled successfully",
                    recoveryCodes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling 2FA");
                return StatusCode(500, new { message = "Error enabling 2FA" });
            }
        }

        // POST: api/twofactor/disable
        [HttpPost("disable")]
        public async Task<IActionResult> Disable()
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                await _userManager.SetTwoFactorEnabledAsync(user, false);
                await _userManager.ResetAuthenticatorKeyAsync(user);

                _logger.LogInformation("2FA disabled for user {UserId}", userId);

                return Ok(new { message = "Two-factor authentication disabled successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disabling 2FA");
                return StatusCode(500, new { message = "Error disabling 2FA" });
            }
        }

        // GET: api/twofactor/recovery-codes
        [HttpGet("recovery-codes")]
        public async Task<IActionResult> GetRecoveryCodes()
        {
            try
            {
                var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userId))
                    return Unauthorized();

                var user = await _userManager.FindByIdAsync(userId);
                if (user == null)
                    return NotFound(new { message = "User not found" });

                var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

                return Ok(new { recoveryCodes });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating recovery codes");
                return StatusCode(500, new { message = "Error generating recovery codes" });
            }
        }

        private string GenerateQrCodeUri(string email, string key)
        {
            const string appName = "StudentCharityHub";
            return $"otpauth://totp/{Uri.EscapeDataString(appName)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(appName)}";
        }

        private string GenerateQrCode(string text)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeBytes = qrCode.GetGraphic(20);
            return Convert.ToBase64String(qrCodeBytes);
        }
    }

    public class Enable2FADto
    {
        public string Code { get; set; } = string.Empty;
    }
}
