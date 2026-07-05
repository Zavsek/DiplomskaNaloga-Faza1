using System.Security.Cryptography;

namespace Poskus3.Services
{
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static (string hash, string salt) HashPassword(string password)
        {
            var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, KeySize);
            return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
        }

        public static bool VerifyPassword(string password, string hash, string salt)
        {
            byte[] hashBytes;
            byte[] saltBytes;

            try
            {
                hashBytes = Convert.FromBase64String(hash);
                saltBytes = Convert.FromBase64String(salt);
            }
            catch (FormatException)
            {
                return false;
            }

            var computedHash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, Iterations, HashAlgorithmName.SHA256, hashBytes.Length);
            return CryptographicOperations.FixedTimeEquals(computedHash, hashBytes);
        }
    }
}
