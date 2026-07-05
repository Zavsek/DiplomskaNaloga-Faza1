using Microsoft.IdentityModel.Tokens;
using Poskus2.DTOs;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Poskus2.Services
{
    public class JwtService
    {
        private readonly string _secret;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _durationMinutes;
        private readonly SymmetricSecurityKey _signingKey;

        public JwtService(string secret, string issuer, string audience, int durationMinutes)
        {
            _secret = secret;
            _issuer = issuer;
            _audience = audience;
            _durationMinutes = durationMinutes;
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        }

        public (string token, string jti, DateTime issuedAt, DateTime expiresAt) GenerateToken(int userId, string email, string fullName)
        {
            var jti = Guid.NewGuid().ToString();
            var issuedAt = DateTime.UtcNow;
            var expiresAt = issuedAt.AddMinutes(_durationMinutes);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim("fullName", fullName)
            };

            var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: issuedAt,
                expires: expiresAt,
                signingCredentials: credentials
            );

            return (new JwtSecurityTokenHandler().WriteToken(token), jti, issuedAt, expiresAt);
        }

        public TokenValidationInfo ValidateToken(string rawToken)
        {
            var handler = new JwtSecurityTokenHandler();

            try
            {
                handler.ValidateToken(rawToken, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _issuer,
                    ValidAudience = _audience,
                    IssuerSigningKey = _signingKey,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                var jwt = (JwtSecurityToken)validatedToken;

                return new TokenValidationInfo
                {
                    valid = true,
                    jti = jwt.Id,
                    email = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Email)?.Value,
                    fullName = jwt.Claims.FirstOrDefault(c => c.Type == "fullName")?.Value,
                    issuedAt = jwt.IssuedAt,
                    expiresAt = jwt.ValidTo
                };
            }
            catch (SecurityTokenExpiredException)
            {
                return new TokenValidationInfo { valid = false, message = "Token je potekel." };
            }
            catch (Exception)
            {
                return new TokenValidationInfo { valid = false, message = "Token ni veljaven." };
            }
        }

        public string? ExtractJti(string rawToken)
        {
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(rawToken);
                return jwt.Id;
            }
            catch
            {
                return null;
            }
        }
    }
}
