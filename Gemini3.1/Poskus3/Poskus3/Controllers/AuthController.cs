using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using Poskus3.Helpers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Poskus3.Controllers
{
    [ApiController]
    [Route("api")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var age = DateTime.Today.Year - dto.dateOfBirth.Year;
            if (dto.dateOfBirth.Date > DateTime.Today.AddYears(-age)) age--;

            if (age < 18)
            {
                return BadRequest(new { message = "Only adult users (18+) can register." });
            }

            var emailExists = await _context.Users.AnyAsync(u => u.email == dto.email);
            if (emailExists)
            {
                return Conflict(new { message = "Email is already taken." });
            }

            var user = new User
            {
                email = dto.email,
                passwordHash = PasswordHelper.HashPassword(dto.password),
                dateOfBirth = DateTime.SpecifyKind(dto.dateOfBirth.Date, DateTimeKind.Utc),
                fullName = dto.fullName,
                country = dto.country
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid || request.loginPayload == null)
            {
                return BadRequest("Invalid payload.");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.email == request.loginPayload.email);
            if (user == null || !PasswordHelper.VerifyPassword(request.loginPayload.password, user.passwordHash))
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var newJti = Guid.NewGuid().ToString();
            user.currentJwtId = newJti;
            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user, newJti);

            return Ok(new { token });
        }

        [Authorize]
        [HttpGet("validate")]
        public async Task<IActionResult> Validate()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            return Ok(new
            {
                message = "Token is valid.",
                userId = userId,
                email = email,
                jti = jti
            });
        }

        private string GenerateJwtToken(User user, string jti)
        {
            var jwtSection = _configuration.GetSection("Jwt");
            var secret = jwtSection["Secret"];
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var durationMinutes = jwtSection.GetValue<int>("DurationMinutes", 60);

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.id.ToString()),
                new Claim(ClaimTypes.Email, user.email),
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(durationMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}