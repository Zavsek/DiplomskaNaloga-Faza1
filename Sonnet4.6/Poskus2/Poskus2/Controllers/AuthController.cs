using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using Poskus2.Services;
using System.Security.Cryptography;
using System.Text;

namespace Poskus2.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly JwtService _jwtService;

        public AuthController(AppDbContext db, JwtService jwtService)
        {
            _db = db;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dto.dateOfBirth.Year;
            if (dto.dateOfBirth > today.AddYears(-age)) age--;

            if (age < 18)
                return BadRequest(new { message = "Registracija je dovoljena samo polnoletnim uporabnikom." });

            var exists = await _db.Users.AnyAsync(u => u.email == dto.email.ToLower());
            if (exists)
                return Conflict(new { message = "Uporabnik s tem e-poštnim naslovom že obstaja." });

            var user = new User
            {
                email = dto.email.ToLower(),
                passwordHash = HashPassword(dto.password),
                fullName = dto.fullName,
                dateOfBirth = dto.dateOfBirth,
                country = dto.country
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Created($"/api/users/{user.id}", new { message = "Registracija uspešna.", userId = user.id });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.email == dto.loginPayload.email.ToLower());
            if (user == null || user.passwordHash != HashPassword(dto.loginPayload.password))
                return Unauthorized(new { message = "Napačen e-poštni naslov ali geslo." });

            var (token, jti, issuedAt, expiresAt) = _jwtService.GenerateToken(user.id, user.email, user.fullName);

            user.activeTokenJti = jti;
            user.tokenIssuedAt = issuedAt;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                token,
                expiresAt,
                tokenType = "Bearer"
            });
        }

        [HttpGet("validate")]
        public async Task<IActionResult> Validate()
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { valid = false, message = "Manjka Bearer token v glavi Authorization." });

            var rawToken = authHeader["Bearer ".Length..].Trim();
            var info = _jwtService.ValidateToken(rawToken);

            if (!info.valid)
                return Unauthorized(info);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.email == info.email);
            if (user == null || user.activeTokenJti != info.jti)
            {
                return Unauthorized(new TokenValidationInfo
                {
                    valid = false,
                    message = "Token je bil razveljavljen (nova prijava je prepisala ta token)."
                });
            }

            return Ok(info);
        }

        private static string HashPassword(string password)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}
