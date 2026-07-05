using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using System.Threading;
using System.Threading.Tasks;

namespace Poskus1.Services
{
    public class QuizTimerService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly WebSocketManagerService _wsManager;

        public QuizTimerService(IServiceProvider serviceProvider, WebSocketManagerService wsManager)
        {
            _serviceProvider = serviceProvider;
            _wsManager = wsManager;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var now = DateTime.UtcNow;
                        
                        var expiredSessions = await db.QuizSessions
                            .Where(s => !s.IsCompleted && s.ExpiresAt <= now)
                            .ToListAsync(stoppingToken);

                        if (expiredSessions.Any())
                        {
                            foreach (var session in expiredSessions)
                            {
                                session.IsCompleted = true;
                                await _wsManager.SendTimeoutMessageAsync(session.UserId, session.QuizId);
                            }

                            await db.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch
                {
                    // Ignore exceptions to keep the background service running
                }

                await Task.Delay(1000, stoppingToken); // Check every second
            }
        }
    }
}
