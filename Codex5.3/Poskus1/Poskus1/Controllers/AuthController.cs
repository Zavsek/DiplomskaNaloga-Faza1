using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Configuration;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;
using Poskus1.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Poskus1.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly JwtOptions _jwtOptions;

        public AuthController(AppDbContext dbContext, IOptions<JwtOptions> jwtOptions)
        {
            _dbContext = dbContext;
            _jwtOptions = jwtOptions.Value;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            if (!IsAdult(request.dateOfBirth))
            {
                return BadRequest(new { message = "Registracija je dovoljena samo polnoletnim uporabnikom." });
            }

            var normalizedEmail = request.email.Trim().ToLowerInvariant();
            var userExists = await _dbContext.Users.AnyAsync(u => u.email == normalizedEmail);
            if (userExists)
            {
                return Conflict(new { message = "Uporabnik s tem email naslovom že obstaja." });
            }

            var (hash, salt) = PasswordService.HashPassword(request.password);
            var user = new AppUser
            {
                email = normalizedEmail,
                passwordHash = hash,
                passwordSalt = salt,
                dateOfBirth = DateTime.SpecifyKind(request.dateOfBirth.Date, DateTimeKind.Utc),
                fullName = request.fullName.Trim(),
                country = request.country.Trim()
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Registracija uspešna.",
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
            if (request.loginPayload is null)
            {
                return BadRequest(new { message = "Objekt loginPayload je obvezen." });
            }

            var normalizedEmail = request.loginPayload.email.Trim().ToLowerInvariant();
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.email == normalizedEmail);
            if (user is null || !PasswordService.VerifyPassword(request.loginPayload.password, user.passwordHash, user.passwordSalt))
            {
                return Unauthorized(new { message = "Neveljaven email ali geslo." });
            }

            var nowUtc = DateTime.UtcNow;
            var expiresAtUtc = nowUtc.AddMinutes(_jwtOptions.DurationMinutes);
            var jwtId = Guid.NewGuid().ToString("N");
            var token = GenerateJwtToken(user, jwtId, nowUtc, expiresAtUtc);

            user.currentJwtId = jwtId;
            user.currentJwtExpiresAtUtc = expiresAtUtc;
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                token,
                tokenType = "Bearer",
                expiresAtUtc,
                user = new
                {
                    user.id,
                    user.email,
                    user.fullName,
                    user.country
                }
            });
        }

        [Authorize]
        [HttpGet("validate")]
        public async Task<IActionResult> ValidateToken()
        {
            var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(subClaim, out var userId))
            {
                return Unauthorized(new { valid = false, message = "Token ne vsebuje veljavnega uporabniškega identifikatorja." });
            }

            var jwtId = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrWhiteSpace(jwtId))
            {
                return Unauthorized(new { valid = false, message = "Token ne vsebuje JTI." });
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.id == userId);
            if (user is null)
            {
                return Unauthorized(new { valid = false, message = "Uporabnik ne obstaja." });
            }

            var isCurrentTokenValid =
                user.currentJwtId == jwtId &&
                user.currentJwtExpiresAtUtc.HasValue &&
                user.currentJwtExpiresAtUtc.Value > DateTime.UtcNow;

            if (!isCurrentTokenValid)
            {
                return Unauthorized(new { valid = false, message = "Token je neveljaven ali razveljavljen zaradi nove prijave." });
            }

            var issuedAtUtc = ReadUnixClaim(User.FindFirstValue(JwtRegisteredClaimNames.Iat));
            var expiresAtUtc = ReadUnixClaim(User.FindFirstValue(JwtRegisteredClaimNames.Exp));

            return Ok(new
            {
                valid = true,
                tokenInfo = new
                {
                    jwtId,
                    userId,
                    email = User.FindFirstValue(JwtRegisteredClaimNames.Email),
                    fullName = User.FindFirstValue(JwtRegisteredClaimNames.UniqueName),
                    issuer = _jwtOptions.Issuer,
                    audience = _jwtOptions.Audience,
                    issuedAtUtc,
                    expiresAtUtc
                }
            });
        }

        private string GenerateJwtToken(AppUser user, string jwtId, DateTime nowUtc, DateTime expiresAtUtc)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.id.ToString()),
                new(ClaimTypes.NameIdentifier, user.id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.email),
                new(JwtRegisteredClaimNames.UniqueName, user.fullName),
                new(JwtRegisteredClaimNames.Jti, jwtId),
                new(JwtRegisteredClaimNames.Iat, ToUnix(nowUtc).ToString(), ClaimValueTypes.Integer64)
            };

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                notBefore: nowUtc,
                expires: expiresAtUtc,
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static bool IsAdult(DateTime dateOfBirth)
        {
            var today = DateTime.UtcNow.Date;
            var age = today.Year - dateOfBirth.Year;


            return age >= 18;
        }

        private static long ToUnix(DateTime dateTimeUtc)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dateTimeUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }

        private static DateTime? ReadUnixClaim(string? value)
        {
            return long.TryParse(value, out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
                : null;
        }
    }
}
