using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Poskus2.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _config;

        public AuthController(AppDbContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (dto == null) return BadRequest("Invalid request.");

            // Check age
            var age = DateTime.Today.Year - dto.dateOfBirth.Year;
            if (dto.dateOfBirth.Date > DateTime.Today.AddYears(-age)) age--;

            if (age < 18)
            {
                return BadRequest("User must be at least 18 years old.");
            }

            if (await _context.Users.AnyAsync(u => u.Email == dto.email))
            {
                return Conflict("Email is already registered.");
            }

            var user = new User
            {
                Email = dto.email,
                PasswordHash = HashPassword(dto.password),
                DateOfBirth = DateTime.SpecifyKind(dto.dateOfBirth.Date, DateTimeKind.Utc),
                FullName = dto.fullName,
                Country = dto.country
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful" });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request?.loginPayload == null) return BadRequest("Invalid payload.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.loginPayload.email);
            if (user == null || !VerifyPassword(request.loginPayload.password, user.PasswordHash))
            {
                return Unauthorized("Invalid email or password.");
            }

            // Invalidate previous JWT by generating a new ActiveTokenId
            var tokenId = Guid.NewGuid().ToString();
            user.ActiveTokenId = tokenId;
            await _context.SaveChangesAsync();

            var jwtSection = _config.GetSection("Jwt");
            var secret = jwtSection["Secret"];
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var durationInMinutes = jwtSection.GetValue<int>("DurationInMinutes", 60);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, tokenId),
                new Claim("FullName", user.FullName)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(durationInMinutes),
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            return Ok(new { token = tokenString });
        }

        [Authorize]
        [HttpGet("validate")]
        public IActionResult Validate()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                            ?? User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? User.FindFirst(JwtRegisteredClaimNames.Email)?.Value;

            return Ok(new
            {
                message = "Token is valid",
                userId = userIdStr,
                email = email,
                jti = jti
            });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(password);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string hash)
        {
            return HashPassword(password) == hash;
        }
    }
}
