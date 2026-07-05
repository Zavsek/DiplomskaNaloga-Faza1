using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs.Auth;
using Poskus2.Entities;
using Poskus2.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus2.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly PasswordHasher _passwordHasher;
        private readonly JwtTokenService _jwtTokenService;

        public AuthController(
            AppDbContext dbContext,
            PasswordHasher passwordHasher,
            JwtTokenService jwtTokenService)
        {
            _dbContext = dbContext;
            _passwordHasher = passwordHasher;
            _jwtTokenService = jwtTokenService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            var normalizedEmail = request.email.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(normalizedEmail) ||
                string.IsNullOrWhiteSpace(request.password) ||
                string.IsNullOrWhiteSpace(request.fullName) ||
                string.IsNullOrWhiteSpace(request.country))
            {
                return BadRequest(new { message = "All registration fields are required." });
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var minimumBirthDate = today.AddYears(-18);
            if (request.dateOfBirth > minimumBirthDate)
            {
                return BadRequest(new { message = "User must be at least 18 years old." });
            }

            var alreadyExists = await _dbContext.Users.AnyAsync(u => u.email == normalizedEmail);
            if (alreadyExists)
            {
                return Conflict(new { message = "Email is already registered." });
            }

            var (hash, salt) = _passwordHasher.HashPassword(request.password);
            var user = new AppUser
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
                    user.country
                }
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
        {
            var payload = request.loginPayload;
            if (payload is null)
            {
                return BadRequest(new { message = "loginPayload is required." });
            }

            var normalizedEmail = payload.email.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail) || string.IsNullOrWhiteSpace(payload.password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.email == normalizedEmail);
            if (user is null || !_passwordHasher.VerifyPassword(payload.password, user.passwordHash, user.passwordSalt))
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var tokenResult = _jwtTokenService.GenerateToken(user);

            user.currentTokenJti = tokenResult.Jti;
            user.currentTokenExpiresAtUtc = tokenResult.ExpiresAtUtc;
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                token = tokenResult.Token,
                expiresAtUtc = tokenResult.ExpiresAtUtc,
                tokenType = "Bearer"
            });
        }

        [Authorize]
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateToken()
        {
            var userIdClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var jtiClaim = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
            {
                return Unauthorized(new { valid = false, message = "Invalid token claims." });
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.id == userId);

            if (user is null ||
                user.currentTokenJti != jtiClaim ||
                user.currentTokenExpiresAtUtc is null ||
                user.currentTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return Unauthorized(new { valid = false, message = "Token is no longer valid." });
            }

            return Ok(new
            {
                valid = true,
                user = new
                {
                    user.id,
                    user.email,
                    user.fullName,
                    user.country
                },
                token = new
                {
                    jti = user.currentTokenJti,
                    expiresAtUtc = user.currentTokenExpiresAtUtc,
                    issuer = _jwtTokenService.Issuer,
                    audience = _jwtTokenService.Audience
                }
            });
        }
    }
}
