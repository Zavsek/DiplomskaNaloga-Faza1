using Microsoft.EntityFrameworkCore;
using Poskus3.Data;

namespace Poskus3.Services
{
    public class QuizTimeoutWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly GameWebSocketManager _wsManager;
        private readonly ILogger<QuizTimeoutWorker> _logger;

        public QuizTimeoutWorker(IServiceProvider serviceProvider, GameWebSocketManager wsManager, ILogger<QuizTimeoutWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _wsManager = wsManager;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    var now = DateTime.UtcNow;

                    var expiredSessions = await dbContext.QuizSessions
                        .Where(s => !s.isFinished && s.endTime <= now)
                        .ToListAsync(stoppingToken);

                    if (expiredSessions.Any())
                    {
                        foreach (var session in expiredSessions)
                        {
                            session.isFinished = true;
                        }

                        await dbContext.SaveChangesAsync(stoppingToken);

                        foreach (var session in expiredSessions)
                        {
                            await _wsManager.NotifyTimeoutAsync(session.userId, session.quizId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing QuizTimeoutWorker.");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}