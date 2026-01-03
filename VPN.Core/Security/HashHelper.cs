using System;
using System.Security.Cryptography;
using System.Text;

namespace VPN.Core.Security
{
    /// <summary>
    /// Production-grade cryptographic helper for VPN
    /// </summary>
    public static class HashHelper
    {
        // OWASP 2023 recommended settings
        private const int PASSWORD_ITERATIONS = 600000;
        private const int SALT_SIZE = 32; // 256 bits
        private const int KEY_SIZE = 32; // 256 bits for AES-256

        /// <summary>
        /// Compute SHA256 hash (for general purpose)
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
        /// Verify HMAC for message integrity (constant-time)
        /// </summary>
        public static bool VerifyHmac(byte[] data, byte[] hmac, byte[] key)
        {
            byte[] computedHmac = ComputeHmacSha256(data, key);
            return CryptographicOperations.FixedTimeEquals(computedHmac, hmac);
        }

        /// <summary>
        /// Generate cryptographically secure random bytes
        /// </summary>
        public static byte[] GenerateRandomBytes(int length)
        {
            if (length <= 0)
                throw new ArgumentException("Length must be positive", nameof(length));

            byte[] bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        /// <summary>
        /// PRODUCTION-GRADE: Secure password hashing using PBKDF2-HMAC-SHA512
        /// </summary>
        public static byte[] HashPassword(string password, byte[] salt)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            if (salt == null || salt.Length == 0)
                throw new ArgumentException("Salt cannot be empty", nameof(salt));

            // Use the installed NuGet package (already in your project)
            try
            {
                return Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
                    password: password,
                    salt: salt,
                    prf: Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA512,
                    iterationCount: PASSWORD_ITERATIONS,
                    numBytesRequested: KEY_SIZE);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to hash password. Ensure Microsoft.AspNetCore.Cryptography.KeyDerivation " +
                    "package is installed.", ex);
            }
        }

        /// <summary>
        /// Verify password against hash (constant-time comparison)
        /// </summary>
        public static bool VerifyPassword(string password, byte[] salt, byte[] expectedHash)
        {
            if (string.IsNullOrWhiteSpace(password))
                return false;

            if (salt == null || salt.Length == 0 || expectedHash == null || expectedHash.Length == 0)
                return false;

            byte[] actualHash = HashPassword(password, salt);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        /// <summary>
        /// Generate a cryptographically secure salt
        /// </summary>
        public static byte[] GenerateSalt()
        {
            return GenerateRandomBytes(SALT_SIZE);
        }

        /// <summary>
        /// Compare two byte arrays in constant time (prevents timing attacks)
        /// </summary>
        public static bool ConstantTimeEquals(byte[] a, byte[] b)
        {
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        /// <summary>
        /// Derive encryption key from password (for key encryption)
        /// </summary>
        public static byte[] DeriveEncryptionKey(string password, byte[] salt, int iterations = 100000)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            if (salt == null || salt.Length < 16)
                throw new ArgumentException("Salt must be at least 16 bytes", nameof(salt));

            return Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: Microsoft.AspNetCore.Cryptography.KeyDerivation.KeyDerivationPrf.HMACSHA512,
                iterationCount: iterations,
                numBytesRequested: 32); // 256-bit key for AES-256
        }

        /// <summary>
        /// Generate secure random password
        /// </summary>
        public static string GenerateSecurePassword(int length = 16)
        {
            if (length < 12)
                throw new ArgumentException("Password length must be at least 12 characters", nameof(length));

            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghjkmnpqrstuvwxyz";
            const string digits = "23456789";
            const string special = "!@#$%^&*";

            // Ensure at least one of each character type
            char[] password = new char[length];

            using (var rng = RandomNumberGenerator.Create())
            {
                // Fill with random characters
                for (int i = 0; i < length; i++)
                {
                    string charSet = i switch
                    {
                        0 => upper,
                        1 => lower,
                        2 => digits,
                        3 => special,
                        _ => upper + lower + digits + special
                    };

                    byte[] randomByte = new byte[1];
                    rng.GetBytes(randomByte);
                    password[i] = charSet[randomByte[0] % charSet.Length];
                }

                // Shuffle the password
                for (int i = 0; i < length; i++)
                {
                    byte[] randomBytes = new byte[4];
                    rng.GetBytes(randomBytes);
                    int j = Math.Abs(BitConverter.ToInt32(randomBytes, 0)) % length;

                    (password[i], password[j]) = (password[j], password[i]);
                }
            }

            return new string(password);
        }

        /// <summary>
        /// Validate password strength
        /// </summary>
        public static (bool IsValid, string Error) ValidatePasswordStrength(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return (false, "Password cannot be empty");

            if (password.Length < 12)
                return (false, "Password must be at least 12 characters");

            bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;

            foreach (char c in password)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (char.IsPunctuation(c) || char.IsSymbol(c)) hasSpecial = true;
            }

            var errors = new System.Collections.Generic.List<string>();
            if (!hasUpper) errors.Add("at least one uppercase letter");
            if (!hasLower) errors.Add("at least one lowercase letter");
            if (!hasDigit) errors.Add("at least one digit");
            if (!hasSpecial) errors.Add("at least one special character");

            if (errors.Count > 0)
                return (false, $"Password must contain: {string.Join(", ", errors)}");

            return (true, "Password is strong");
        }

        /// <summary>
        /// Create secure token for session IDs
        /// </summary>
        public static string GenerateSecureToken(int length = 32)
        {
            byte[] tokenBytes = GenerateRandomBytes(length);
            return Convert.ToBase64String(tokenBytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", ""); // URL-safe base64
        }

        /// <summary>
        /// PRODUCTION-GRADE: Hash password with PBKDF2-HMAC-SHA256 (fallback implementation)
        /// </summary>
        public static byte[] HashPasswordWithSHA256(string plainTextPassword, byte[] salt, int iterations)
        {
            if (string.IsNullOrWhiteSpace(plainTextPassword))
                throw new ArgumentException("Password cannot be empty", nameof(plainTextPassword));

            if (salt == null || salt.Length == 0)
                throw new ArgumentException("Salt cannot be empty", nameof(salt));

            if (iterations < 100000)
                throw new ArgumentException("Iteration count must be at least 100,000", nameof(iterations));

            // Use PBKDF2 with HMAC-SHA256
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                plainTextPassword,
                salt,
                iterations,
                HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(KEY_SIZE); // 32 bytes for 256-bit key
            }
        }
    }
}