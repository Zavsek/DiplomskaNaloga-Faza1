using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using Poskus3.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus3.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly JwtTokenService _jwtTokenService;

        public AuthController(AppDbContext dbContext, JwtTokenService jwtTokenService)
        {
            _dbContext = dbContext;
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            var normalizedEmail = request.email.Trim().ToLowerInvariant();

            var exists = await _dbContext.Users.AnyAsync(u => u.email == normalizedEmail);
            if (exists)
            {
                return Conflict(new { message = "User with this email already exists." });
            }

            if (!IsAdult(request.dateOfBirth))
            {
                return BadRequest(new { message = "Only adults can register." });
            }

            var (hash, salt) = PasswordHasher.HashPassword(request.password);
            var user = new User
            {
                email = normalizedEmail,
                passwordHash = hash,
                passwordSalt = salt,
                dateOfBirth = request.dateOfBirth,
                fullName = request.fullName.Trim(),
                country = request.country.Trim()
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Registration successful.",
                user = new
                {
                    user.id,
                    user.email,
                    user.fullName,
                    user.country,
                    user.dateOfBirth
                }
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            var normalizedEmail = request.loginPayload.email.Trim().ToLowerInvariant();
            var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.email == normalizedEmail);

            if (user is null || !PasswordHasher.VerifyPassword(request.loginPayload.password, user.passwordHash, user.passwordSalt))
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var tokenData = _jwtTokenService.GenerateToken(user);

            // Keep exactly one active token per user by replacing stored jti on each login.
            user.currentTokenJti = tokenData.jti;
            user.currentTokenExpiresAtUtc = tokenData.expiresAtUtc;
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                token = tokenData.token,
                expiresAtUtc = tokenData.expiresAtUtc
            });
        }

        [Authorize]
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateToken()
        {
            var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            var jtiClaim = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var emailClaim = User.FindFirstValue(JwtRegisteredClaimNames.Email);
            var fullNameClaim = User.FindFirstValue("fullName");
            var countryClaim = User.FindFirstValue("country");

            if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
            {
                return Unauthorized(new { isValid = false, message = "Token claims are incomplete." });
            }

            var user = await _dbContext.Users.SingleOrDefaultAsync(u => u.id == userId);
            if (user is null || user.currentTokenJti != jtiClaim)
            {
                return Unauthorized(new { isValid = false, message = "Token is not active anymore." });
            }

            if (user.currentTokenExpiresAtUtc is null || user.currentTokenExpiresAtUtc <= DateTime.UtcNow)
            {
                return Unauthorized(new { isValid = false, message = "Token has expired." });
            }

            var expValue = User.FindFirstValue(JwtRegisteredClaimNames.Exp);
            var expiresAt = long.TryParse(expValue, out var expSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(expSeconds).UtcDateTime
                : user.currentTokenExpiresAtUtc.Value;
            var issuedAt = User.FindFirstValue(JwtRegisteredClaimNames.Iat) is string iatValue &&
                           long.TryParse(iatValue, out var iatSeconds)
                ? DateTimeOffset.FromUnixTimeSeconds(iatSeconds).UtcDateTime
                : (DateTime?)null;

            return Ok(new
            {
                isValid = true,
                tokenInfo = new
                {
                    userId,
                    email = emailClaim,
                    fullName = fullNameClaim,
                    country = countryClaim,
                    jti = jtiClaim,
                    issuedAtUtc = issuedAt,
                    expiresAtUtc = expiresAt
                }
            });
        }

        private static bool IsAdult(DateOnly dateOfBirth)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return dateOfBirth.AddYears(18) <= today;
        }
    }
}
