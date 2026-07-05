using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.Entities;

namespace Poskus1.Services
{
    public class QuizSessionTimeoutService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public QuizSessionTimeoutService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await MarkExpiredSessionsAsync(stoppingToken);
                }
                catch
                {
                    // Namenoma pogoltnemo izjemo, da servis ostane živ.
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task MarkExpiredSessionsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var nowUtc = DateTime.UtcNow;

            var expiredSessions = await dbContext.GameSessions
                .Where(gs => gs.status == GameSessionStatus.InProgress && gs.expiresAtUtc <= nowUtc)
                .ToListAsync(cancellationToken);

            if (expiredSessions.Count == 0)
            {
                return;
            }

            foreach (var session in expiredSessions)
            {
                session.status = GameSessionStatus.Completed;
                session.completedAtUtc = nowUtc;
                session.completionReason = "TimeExpired";
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
