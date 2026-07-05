using Microsoft.EntityFrameworkCore;
using Poskus3.Entities;

namespace Poskus3.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<GameSessionAnswer> GameSessionAnswers { get; set; }
        public DbSet<AnswerSubmission> AnswerSubmissions { get; set; }

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
                .HasIndex(gs => new { gs.userId, gs.quizId, gs.completedAtUtc });

            modelBuilder.Entity<GameSessionAnswer>()
                .HasOne(gsa => gsa.gameSession)
                .WithMany(gs => gs.answers)
                .HasForeignKey(gsa => gsa.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameSessionAnswer>()
                .HasOne(gsa => gsa.question)
                .WithMany()
                .HasForeignKey(gsa => gsa.questionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<GameSessionAnswer>()
                .HasIndex(gsa => new { gsa.gameSessionId, gsa.questionId })
                .IsUnique();

            modelBuilder.Entity<AnswerSubmission>()
                .HasOne(s => s.gameSession)
                .WithMany(gs => gs.submissions)
                .HasForeignKey(s => s.gameSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AnswerSubmission>()
                .HasOne(s => s.question)
                .WithMany()
                .HasForeignKey(s => s.questionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AnswerSubmission>()
                .HasIndex(s => new { s.gameSessionId, s.submittedAtUtc, s.id });
        }
    }
}
