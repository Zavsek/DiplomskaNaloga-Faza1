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
        public DbSet<QuizSession> QuizSessions { get; set; }
        public DbSet<QuizAnswer> QuizAnswers { get; set; }

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
        }
    }
}
