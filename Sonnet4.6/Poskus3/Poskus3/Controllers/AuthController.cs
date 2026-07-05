using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using Poskus3.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;

namespace Poskus3.Controllers
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

            var age = DateTime.UtcNow.Year - dto.dateOfBirth.Year;
            if (dto.dateOfBirth.Date > DateTime.UtcNow.AddYears(-age).Date) age--;
            if (age < 18)
                return BadRequest(new { message = "Registracija je dovoljena samo polnoletnim uporabnikom." });

            var exists = await _db.Users.AnyAsync(u => u.email == dto.email.ToLower());
            if (exists)
                return Conflict(new { message = "Uporabnik s tem e-poštnim naslovom že obstaja." });

            var user = new User
            {
                email = dto.email.ToLower(),
                passwordHash = HashPassword(dto.password),
                dateOfBirth = dto.dateOfBirth.Date.ToUniversalTime(),
                fullName = dto.fullName,
                country = dto.country
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return StatusCode(201, new
            {
                message = "Registracija uspešna.",
                userId = user.id,
                email = user.email,
                fullName = user.fullName
            });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _db.Users.FirstOrDefaultAsync(u => u.email == dto.loginPayload.email.ToLower());
            if (user == null || !VerifyPassword(dto.loginPayload.password, user.passwordHash))
                return Unauthorized(new { message = "Napačen e-poštni naslov ali geslo." });

            var (token, jti) = _jwtService.GenerateToken(user);

            user.currentTokenJti = jti;
            await _db.SaveChangesAsync();

            return Ok(new
            {
                token,
                message = "Prijava uspešna."
            });
        }

        private static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromBase64String(parts[0]);
            var expectedHash = Convert.FromBase64String(parts[1]);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32);
            return CryptographicOperations.FixedTimeEquals(hash, expectedHash);
        }

        [HttpPost("validate")]
        public async Task<IActionResult> Validate()
        {
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (authHeader == null || !authHeader.StartsWith("Bearer "))
                return Unauthorized(new { valid = false, message = "Token ni bil posredovan." });

            var token = authHeader["Bearer ".Length..].Trim();

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
                return Unauthorized(new { valid = false, message = "Token ni veljaven ali je potekel." });

            var decoded = _jwtService.DecodeToken(token);
            if (decoded == null)
                return Unauthorized(new { valid = false, message = "Token ni mogoče prebrati." });

            var jti = decoded.Id;
            var subClaim = decoded.Subject;
            if (!int.TryParse(subClaim, out var userId))
                return Unauthorized(new { valid = false, message = "Token vsebuje napačne podatke." });

            var user = await _db.Users.FindAsync(userId);
            if (user == null || user.currentTokenJti != jti)
                return Unauthorized(new { valid = false, message = "Token je bil razveljavljen." });

            return Ok(new
            {
                valid = true,
                userId = user.id,
                email = user.email,
                fullName = user.fullName,
                country = user.country,
                issuedAt = decoded.IssuedAt,
                expiresAt = decoded.ValidTo
            });
        }
    }
}
