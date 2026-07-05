using Microsoft.EntityFrameworkCore;
using Poskus2.Entities;

namespace Poskus2.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<AppUser> Users { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<GameAnswer> GameAnswers { get; set; }
        public DbSet<GameAnswerAttempt> GameAnswerAttempts { get; set; }

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

            modelBuilder.Entity<AppUser>()
                .HasIndex(u => u.email)
                .IsUnique();

            modelBuilder.Entity<GameSession>()
                .HasOne(gs => gs.user)
                .WithMany()
                .HasForeignKey(gs => gs.userId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameSession>()
                .HasOne(gs => gs.quiz)
                .WithMany()
                .HasForeignKey(gs => gs.quizId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameSession>()
                .HasIndex(gs => new { gs.userId, gs.quizId, gs.status });

            modelBuilder.Entity<GameAnswer>()
                .HasOne(ga => ga.gameSession)
                .WithMany(gs => gs.answers)
                .HasForeignKey(ga => ga.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswer>()
                .HasOne(ga => ga.question)
                .WithMany()
                .HasForeignKey(ga => ga.questionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswer>()
                .HasIndex(ga => new { ga.gameSessionId, ga.questionId })
                .IsUnique();

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasOne(gaa => gaa.gameSession)
                .WithMany()
                .HasForeignKey(gaa => gaa.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasOne(gaa => gaa.question)
                .WithMany()
                .HasForeignKey(gaa => gaa.questionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasIndex(gaa => new { gaa.gameSessionId, gaa.submittedAtUtc });
        }
    }
}
