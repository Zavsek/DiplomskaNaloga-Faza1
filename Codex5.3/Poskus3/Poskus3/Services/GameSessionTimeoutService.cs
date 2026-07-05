using Microsoft.EntityFrameworkCore;
using Poskus3.Data;

namespace Poskus3.Services
{
    public class GameSessionTimeoutService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GameSessionTimeoutService> _logger;

        public GameSessionTimeoutService(IServiceScopeFactory scopeFactory, ILogger<GameSessionTimeoutService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var now = DateTime.UtcNow;

                    await dbContext.GameSessions
                        .Where(gs => gs.completedAtUtc == null && gs.expiresAtUtc <= now)
                        .ExecuteUpdateAsync(update => update
                            .SetProperty(gs => gs.completedAtUtc, now)
                            .SetProperty(gs => gs.completionReason, "TimeExpired"), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed while expiring quiz sessions.");
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
