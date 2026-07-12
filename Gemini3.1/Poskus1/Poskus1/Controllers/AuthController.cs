using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;
using Poskus1.Helpers;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Poskus1.Controllers
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
        public async Task<IActionResult> Register([FromBody] RegisterDto request)
        {
            if (string.IsNullOrWhiteSpace(request.email) || string.IsNullOrWhiteSpace(request.password))
            {
                return BadRequest(new { message = "Email and password are required." });
            }

            // Check if user is 18 or older
            var today = DateTime.Today;
            var age = today.Year - request.dateOfBirth.Year;
            if (request.dateOfBirth.Date > today.AddYears(-age)) age--;

            if (age < 18)
            {
                return BadRequest(new { message = "User must be at least 18 years old to register." });
            }

            if (await _context.Users.AnyAsync(u => u.Email.ToLower() == request.email.ToLower()))
            {
                return BadRequest(new { message = "Email is already in use." });
            }

            var user = new User
            {
                Email = request.email,
                PasswordHash = PasswordHelper.HashPassword(request.password),
                DateOfBirth = DateTime.SpecifyKind(request.dateOfBirth.Date, DateTimeKind.Utc),
                FullName = request.fullName,
                Country = request.country
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request.loginPayload == null || 
                string.IsNullOrWhiteSpace(request.loginPayload.email) || 
                string.IsNullOrWhiteSpace(request.loginPayload.password))
            {
                return BadRequest(new { message = "Invalid login payload." });
            }

            var email = request.loginPayload.email;
            var password = request.loginPayload.password;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
            if (user == null || !PasswordHelper.VerifyPassword(password, user.PasswordHash))
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            var jwtSection = _configuration.GetSection("Jwt");
            var secret = jwtSection["Secret"] ?? "DefaultSuperSecretKeyThatIsVeryLongAndSecure123!";
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var durationInMinutes = jwtSection.GetValue<int>("DurationInMinutes", 60);

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jti = Guid.NewGuid().ToString();

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(durationInMinutes),
                signingCredentials: creds
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            // Update user's active token to invalidate previous ones
            user.ActiveTokenId = jti;
            await _context.SaveChangesAsync();

            return Ok(new { token = tokenString });
        }

        [Authorize]
        [HttpGet("validate")]
        public IActionResult Validate()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = User.FindFirstValue(ClaimTypes.Email);

            return Ok(new 
            { 
                message = "Token is valid.",
                userId = userId,
                email = email
            });
        }
    }
}
