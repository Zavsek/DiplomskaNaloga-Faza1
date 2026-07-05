using System.Security.Cryptography;

namespace Poskus1.Services
{
    public static class PasswordService
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 100_000;

        public static (string hash, string salt) HashPassword(string password)
        {
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);
            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        public static bool VerifyPassword(string password, string passwordHash, string passwordSalt)
        {
            byte[] saltBytes;
            byte[] storedHashBytes;

            try
            {
                saltBytes = Convert.FromBase64String(passwordSalt);
                storedHashBytes = Convert.FromBase64String(passwordHash);
            }
            catch (FormatException)
            {
                return false;
            }

            var calculatedHash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, storedHashBytes.Length);
            return CryptographicOperations.FixedTimeEquals(calculatedHash, storedHashBytes);
        }
    }
}
