using Microsoft.EntityFrameworkCore;
using Poskus1.Entities;

namespace Poskus1.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
        public DbSet<Quiz> Quizzes { get; set; }
        public DbSet<Question> Questions { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
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

            modelBuilder.Entity<UserAnswer>()
                .HasOne(ua => ua.session)
                .WithMany(gs => gs.answers)
                .HasForeignKey(ua => ua.sessionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserAnswer>()
                .HasOne(ua => ua.question)
                .WithMany()
                .HasForeignKey(ua => ua.questionId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<UserAnswer>()
                .HasIndex(ua => new { ua.sessionId, ua.questionId })
                .IsUnique();

            modelBuilder.Entity<ChatMessage>()
                .HasOne(cm => cm.user)
                .WithMany()
                .HasForeignKey(cm => cm.userId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChatMessage>()
                .HasIndex(cm => cm.sentAt);
        }
    }
}
