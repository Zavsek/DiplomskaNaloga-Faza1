using Microsoft.IdentityModel.Tokens;
using Poskus3.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Poskus3.Services
{
    public class JwtTokenService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _durationMinutes;

        public JwtTokenService(IConfiguration configuration)
        {
            var jwtSection = configuration.GetSection("Jwt");
            _secret = jwtSection["Secret"] ?? throw new InvalidOperationException("JWT secret is missing.");
            _issuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("JWT issuer is missing.");
            _audience = jwtSection["Audience"] ?? throw new InvalidOperationException("JWT audience is missing.");
            _durationMinutes = int.TryParse(jwtSection["DurationMinutes"], out var duration) ? duration : 60;
        }

        public (string token, string jti, DateTime expiresAtUtc, DateTime issuedAtUtc) GenerateToken(User user)
        {
            var now = DateTime.UtcNow;
            var expiresAt = now.AddMinutes(_durationMinutes);
            var jti = Guid.NewGuid().ToString("N");

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.email),
                new(JwtRegisteredClaimNames.Jti, jti),
                new("fullName", user.fullName),
                new("country", user.country)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: now,
                expires: expiresAt,
                signingCredentials: creds);

            return (new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt, now);
        }
    }
}
