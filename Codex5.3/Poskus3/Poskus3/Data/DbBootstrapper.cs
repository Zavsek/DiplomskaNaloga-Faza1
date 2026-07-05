using Microsoft.EntityFrameworkCore;

namespace Poskus3.Data
{
    public static class DbBootstrapper
    {
        public static async Task EnsureAuthAndGameTablesAsync(AppDbContext dbContext)
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS users (
                    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    email character varying(255) NOT NULL,
                    "passwordHash" text NOT NULL,
                    "passwordSalt" text NOT NULL,
                    "dateOfBirth" date NOT NULL,
                    "fullName" character varying(255) NOT NULL,
                    country character varying(100) NOT NULL,
                    "currentTokenJti" character varying(128),
                    "currentTokenExpiresAtUtc" timestamp with time zone
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_email" ON users (email);
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS game_sessions (
                    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    "userId" integer NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                    "quizId" integer NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
                    "startedAtUtc" timestamp with time zone NOT NULL,
                    "lastInteractionAtUtc" timestamp with time zone,
                    "expiresAtUtc" timestamp with time zone NOT NULL,
                    "completedAtUtc" timestamp with time zone,
                    "completionReason" character varying(64)
                );
                ALTER TABLE game_sessions ADD COLUMN IF NOT EXISTS "lastInteractionAtUtc" timestamp with time zone;
                CREATE INDEX IF NOT EXISTS "IX_game_sessions_userId_quizId_completedAtUtc" ON game_sessions ("userId", "quizId", "completedAtUtc");
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS game_session_answers (
                    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    "gameSessionId" integer NOT NULL REFERENCES game_sessions(id) ON DELETE CASCADE,
                    "questionId" integer NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
                    "selectedAnswer" char(1) NOT NULL,
                    "correctionCount" integer NOT NULL,
                    "answeredAtUtc" timestamp with time zone NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_game_session_answers_gameSessionId_questionId" ON game_session_answers ("gameSessionId", "questionId");
                """);

            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS answer_submissions (
                    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    "gameSessionId" integer NOT NULL REFERENCES game_sessions(id) ON DELETE CASCADE,
                    "questionId" integer NOT NULL REFERENCES questions(id) ON DELETE CASCADE,
                    "selectedAnswer" char(1) NOT NULL,
                    "submittedAtUtc" timestamp with time zone NOT NULL,
                    "responseTimeMs" integer NOT NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_answer_submissions_gameSessionId_submittedAtUtc_id" ON answer_submissions ("gameSessionId", "submittedAtUtc", id);
                """);
        }
    }
}
