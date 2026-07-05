using Microsoft.EntityFrameworkCore;
using Poskus2.Data;

namespace Poskus2.Services
{
    public class QuizTimerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly QuizWebSocketManager _wsManager;
        private readonly ILogger<QuizTimerService> _logger;

        public QuizTimerService(IServiceProvider serviceProvider, QuizWebSocketManager wsManager, ILogger<QuizTimerService> logger)
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
                        .Where(s => !s.IsFinished && s.EndTime <= now)
                        .ToListAsync(stoppingToken);

                    foreach (var session in expiredSessions)
                    {
                        session.IsFinished = true;
                        await _wsManager.SendTimeUpAsync(session.QuizId, session.UserId);
                    }

                    if (expiredSessions.Any())
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in QuizTimerService");
                }

                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}