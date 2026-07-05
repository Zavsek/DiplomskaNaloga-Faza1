using Microsoft.EntityFrameworkCore;
using Poskus1.Entities;

namespace Poskus1.Data
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

            modelBuilder.Entity<GameSession>()
                .HasOne(gs => gs.user)
                .WithMany()
                .HasForeignKey(gs => gs.userId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameSession>()
                .HasOne(gs => gs.quiz)
                .WithMany()
                .HasForeignKey(gs => gs.quizId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GameSession>()
                .HasIndex(gs => new { gs.userId, gs.status });

            modelBuilder.Entity<GameSession>()
                .HasIndex(gs => new { gs.quizId, gs.status });

            modelBuilder.Entity<GameAnswer>()
                .HasOne(ga => ga.gameSession)
                .WithMany(gs => gs.answers)
                .HasForeignKey(ga => ga.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswer>()
                .HasOne(ga => ga.question)
                .WithMany()
                .HasForeignKey(ga => ga.questionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GameAnswer>()
                .HasIndex(ga => new { ga.gameSessionId, ga.questionId })
                .IsUnique();

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasOne(attempt => attempt.gameSession)
                .WithMany()
                .HasForeignKey(attempt => attempt.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasOne(attempt => attempt.question)
                .WithMany()
                .HasForeignKey(attempt => attempt.questionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<GameAnswerAttempt>()
                .HasIndex(attempt => new { attempt.gameSessionId, attempt.submittedAtUtc });
        }
    }
}
