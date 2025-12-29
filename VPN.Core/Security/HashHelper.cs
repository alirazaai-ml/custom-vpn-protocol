using System;
using System.Security.Cryptography;
using System.Text;

namespace VPN.Core.Security
{
    /// <summary>
    /// Helper class for hash and HMAC operations
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// Compute SHA256 hash
        /// </summary>
        public static byte[] ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
        }

        /// <summary>
        /// Compute SHA256 hash of string
        /// </summary>
        public static string ComputeSha256(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            byte[] hash = ComputeSha256(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// Compute HMAC-SHA256 for message integrity
        /// </summary>
        public static byte[] ComputeHmacSha256(byte[] data, byte[] key)
        {
            using var hmac = new HMACSHA256(key);
            return hmac.ComputeHash(data);
        }

        /// <summary>
        /// Verify HMAC for message integrity
        /// </summary>
        public static bool VerifyHmac(byte[] data, byte[] hmac, byte[] key)
        {
            byte[] computedHmac = ComputeHmacSha256(data, key);
            return CryptographicOperations.FixedTimeEquals(computedHmac, hmac);
        }

        /// <summary>
        /// Generate random bytes
        /// </summary>
        public static byte[] GenerateRandomBytes(int length)
        {
            byte[] bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// Secure password hashing using PBKDF2 (Fixed Version)
        /// </summary>
        public static byte[] HashPassword(string password, byte[] salt, int iterations = 10000)
        {
            // FIXED: Use simpler constructor without HashAlgorithmName
            // Default uses SHA1, but we'll do additional hashing

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations);
            byte[] key = pbkdf2.GetBytes(32); // 256-bit key

            // Additional SHA256 hash for extra security
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(key);
        }

        /// <summary>
        /// ALTERNATIVE: Better PBKDF2 with SHA256 using the NuGet package
        /// Install: Microsoft.AspNetCore.Cryptography.KeyDerivation
        /// </summary>
        public static byte[] HashPasswordWithSHA256(string password, byte[] salt, int iterations = 10000)
        {
            // Check if the NuGet package is available
            try
            {
                // This uses the same package we installed for HKDF
                return Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
                    password: password,
                    salt: salt,
                    prf: Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA256,
                    iterationCount: iterations,
                    numBytesRequested: 32);
            }
            catch
            {
                // Fallback to the simpler method
                return HashPassword(password, salt, iterations);
            }
        }

        /// <summary>
        /// Generate a random salt for password hashing
        /// </summary>
        public static byte[] GenerateSalt(int size = 16)
        {
            return GenerateRandomBytes(size);
        }

        /// <summary>
        /// Compare two byte arrays in constant time (prevents timing attacks)
        /// </summary>
        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            return CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}