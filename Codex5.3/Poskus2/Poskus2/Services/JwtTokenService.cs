using Microsoft.IdentityModel.Tokens;
using Poskus2.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Poskus2.Services
{
    public class JwtTokenService
    {
        private readonly string _secret;
        public string Issuer { get; }
        public string Audience { get; }
        public int DurationMinutes { get; }

        public JwtTokenService(IConfiguration configuration)
        {
            var jwtSection = configuration.GetSection("Jwt");
            _secret = jwtSection["Secret"] ?? throw new InvalidOperationException("Missing Jwt:Secret.");
            Issuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Missing Jwt:Issuer.");
            Audience = jwtSection["Audience"] ?? throw new InvalidOperationException("Missing Jwt:Audience.");

            if (!int.TryParse(jwtSection["DurationMinutes"], out var durationMinutes) || durationMinutes <= 0)
            {
                throw new InvalidOperationException("Missing or invalid Jwt:DurationMinutes.");
            }

            DurationMinutes = durationMinutes;
        }

        public TokenResult GenerateToken(AppUser user)
        {
            var now = DateTimeOffset.UtcNow;
            var expiresAt = now.AddMinutes(DurationMinutes);
            var jti = Guid.NewGuid().ToString("N");

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.email),
                new(JwtRegisteredClaimNames.Jti, jti),
                new(ClaimTypes.Name, user.fullName),
                new("country", user.country)
            };

            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: now.UtcDateTime,
                expires: expiresAt.UtcDateTime,
                signingCredentials: credentials);

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
            return new TokenResult(tokenString, jti, expiresAt);
        }
    }

    public record TokenResult(string Token, string Jti, DateTimeOffset ExpiresAtUtc);
}
