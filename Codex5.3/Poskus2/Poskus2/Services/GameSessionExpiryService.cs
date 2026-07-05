using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.Entities;

namespace Poskus2.Services
{
    public class GameSessionExpiryService : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly QuizProgressNotifier _quizProgressNotifier;

        public GameSessionExpiryService(
            IServiceScopeFactory scopeFactory,
            QuizProgressNotifier quizProgressNotifier)
        {
            _scopeFactory = scopeFactory;
            _quizProgressNotifier = quizProgressNotifier;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var affectedQuizIds = await ExpireSessionsAsync(stoppingToken);
                foreach (var quizId in affectedQuizIds)
                {
                    await _quizProgressNotifier.BroadcastProgressAsync(quizId, stoppingToken);
                }

                await Task.Delay(SweepInterval, stoppingToken);
            }
        }

        private async Task<List<int>> ExpireSessionsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;

            var sessionsToExpire = await dbContext.GameSessions
                .Where(gs => gs.status == GameSessionStatus.InProgress && gs.endsAtUtc <= now)
                .ToListAsync(cancellationToken);

            if (sessionsToExpire.Count == 0)
            {
                return new List<int>();
            }

            foreach (var session in sessionsToExpire)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            return sessionsToExpire.Select(s => s.quizId).Distinct().ToList();
        }
    }
}
