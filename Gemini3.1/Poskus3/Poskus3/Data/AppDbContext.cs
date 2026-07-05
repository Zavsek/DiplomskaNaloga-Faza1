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
        public DbSet<QuizSession> QuizSessions { get; set; }
        public DbSet<QuizSessionAnswer> QuizSessionAnswers { get; set; }
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

            modelBuilder.Entity<QuizSessionAnswer>()
                .HasIndex(a => new { a.sessionId, a.questionId })
                .IsUnique();
        }
    }
}
