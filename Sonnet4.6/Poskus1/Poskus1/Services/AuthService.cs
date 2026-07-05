using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Poskus1.Services
{
    public class AuthService
    {
        private readonly AppDbContext _db;
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _durationMinutes;

        public AuthService(AppDbContext db, IConfiguration config)
        {
            _db = db;
            var jwt = config.GetSection("Jwt");
            _secret = jwt["Secret"]!;
            _issuer = jwt["Issuer"]!;
            _audience = jwt["Audience"]!;
            _durationMinutes = int.Parse(jwt["DurationMinutes"] ?? "60");
        }

        public async Task<(bool success, string message)> RegisterAsync(RegisterDto dto)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var age = today.Year - dto.dateOfBirth.Year;
            if (dto.dateOfBirth.AddYears(age) > today) age--;

            if (age < 18)
                return (false, "Registracija je dovoljena samo polnoletnim uporabnikom.");

            var exists = await _db.Users.AnyAsync(u => u.email == dto.email.ToLower());
            if (exists)
                return (false, "Uporabnik s tem e-poštnim naslovom že obstaja.");

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

            return (true, "Registracija uspešna.");
        }

        public async Task<(bool success, string? token, string message)> LoginAsync(LoginPayloadDto payload)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.email == payload.email.ToLower());
            if (user == null || !VerifyPassword(payload.password, user.passwordHash))
                return (false, null, "Napačen e-poštni naslov ali geslo.");

            var jti = Guid.NewGuid().ToString();
            var issuedAt = DateTime.UtcNow;
            var token = GenerateToken(user, jti, issuedAt);

            user.activeTokenJti = jti;
            user.tokenIssuedAt = issuedAt;
            await _db.SaveChangesAsync();

            return (true, token, "Prijava uspešna.");
        }

        public async Task<(bool valid, object? info)> ValidateTokenAsync(string rawToken)
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));

            TokenValidationParameters validationParams = new()
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                IssuerSigningKey = key,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                var principal = handler.ValidateToken(rawToken, validationParams, out var validatedToken);
                var jwtToken = (JwtSecurityToken)validatedToken;

                var jti = jwtToken.Id;
                var email = principal.FindFirstValue(ClaimTypes.Email);

                var user = await _db.Users.FirstOrDefaultAsync(u => u.email == email);
                if (user == null || user.activeTokenJti != jti)
                    return (false, null);

                return (true, new
                {
                    email = user.email,
                    fullName = user.fullName,
                    issuedAt = jwtToken.IssuedAt,
                    expires = jwtToken.ValidTo,
                    jti
                });
            }
            catch
            {
                return (false, null);
            }
        }

        private string GenerateToken(User user, string jti, DateTime issuedAt)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.id.ToString()),
                new Claim(ClaimTypes.Email, user.email),
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim("fullName", user.fullName)
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: issuedAt,
                expires: issuedAt.AddMinutes(_durationMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string HashPassword(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32
            );
            return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string password, string storedHash)
        {
            var parts = storedHash.Split(':');
            if (parts.Length != 2) return false;

            var salt = Convert.FromBase64String(parts[0]);
            var expected = Convert.FromBase64String(parts[1]);

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32
            );

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
