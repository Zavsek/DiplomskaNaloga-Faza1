using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/statistics")]
    public class StatisticsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IUserStatisticsService _statisticsService;

        public StatisticsController(AppDbContext dbContext, IUserStatisticsService statisticsService)
        {
            _dbContext = dbContext;
            _statisticsService = statisticsService;
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyStatistics(CancellationToken cancellationToken)
        {
            var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(subClaim, out var userId))
            {
                return Unauthorized(new { message = "Neveljaven uporabniški identifikator v tokenu." });
            }

            var jwtId = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrWhiteSpace(jwtId))
            {
                return Unauthorized(new { message = "Token nima identifikatorja (JTI)." });
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.id == userId, cancellationToken);
            if (user is null)
            {
                return Unauthorized(new { message = "Uporabnik ne obstaja." });
            }

            var isCurrentTokenValid =
                user.currentJwtId == jwtId &&
                user.currentJwtExpiresAtUtc.HasValue &&
                user.currentJwtExpiresAtUtc.Value > DateTime.UtcNow;

            if (!isCurrentTokenValid)
            {
                return Unauthorized(new { message = "Token je razveljavljen zaradi nove prijave ali je potekel." });
            }

            var statistics = await _statisticsService.BuildUserStatisticsAsync(userId, cancellationToken);
            return Ok(statistics);
        }
    }
}
