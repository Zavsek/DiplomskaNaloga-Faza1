using Microsoft.EntityFrameworkCore;
using Poskus2.Entities;

namespace Poskus2.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<UserGameSession> UserGameSessions { get; set; }
        public DbSet<UserAnswer> UserAnswers { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Question>()
                .HasOne(q => q.quiz)
                .WithMany(qz => qz.questions)
                .HasForeignKey(q => q.quizId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Question>()
                .HasIndex(q => new { q.quizId, q.id });

            modelBuilder.Entity<User>()
                .HasIndex(u => u.email)
                .IsUnique();

            modelBuilder.Entity<UserGameSession>()
                .HasIndex(ugs => new { ugs.gameSessionId, ugs.userId })
                .IsUnique();

            modelBuilder.Entity<UserAnswer>()
                .HasIndex(ua => new { ua.userGameSessionId, ua.questionId })
                .IsUnique();
        }
    }
}
