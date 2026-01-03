using System;
using System.Security.Cryptography;
using System.Text;
using VPN.Core.Exceptions;

namespace VPN.Core.Security
{
    /// <summary>
    /// Secure key exchange using ECDH (Elliptic Curve Diffie-Hellman)
    /// </summary>
    public class KeyExchange : IDisposable
    {
        private readonly ECDiffieHellman _dh;
        private readonly byte[] _publicKey;

        public KeyExchange()
        {
            _dh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); // P-256 curve (256-bit security)
            _publicKey = _dh.ExportSubjectPublicKeyInfo();
        }

        /// <summary>
        /// Get our public key to send to other party
        /// </summary>
        public byte[] GetPublicKey() => _publicKey;

        /// <summary>
        /// Derive shared secret from other party's public key
        /// </summary>
        public byte[] DeriveSharedSecret(byte[] otherPartyPublicKey)
        {
            try
            {
                // Import the other party's public key
                using var otherParty = ECDiffieHellman.Create();
                otherParty.ImportSubjectPublicKeyInfo(otherPartyPublicKey, out _);

                // Derive the shared secret using SHA256 hash
                return _dh.DeriveKeyFromHash(
                    otherParty.PublicKey,
                    HashAlgorithmName.SHA256,
                    null,
                    null);
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Failed to derive shared secret", ex);
            }
        }

        /// <summary>
        /// Derive session key from shared secret using HKDF
        /// </summary>
        public byte[] DeriveSessionKey(byte[] sharedSecret, byte[] salt = null, string info = "VPN-Session-Key")
        {
            try
            {
                // Use HKDF to derive a secure session key from shared secret
                // HKDF: HMAC-based Extract-and-Expand Key Derivation Function

                // Step 1: Extract (if needed, but ECDH already gives good entropy)
                // We'll skip extract phase as ECDH shared secret already has good entropy

                // Step 2: Expand
                using var hmac = new HMACSHA256(sharedSecret);

                // Generate salt if not provided
                if (salt == null || salt.Length == 0)
                {
                    salt = new byte[32];
                    using var rng = RandomNumberGenerator.Create();
                    rng.GetBytes(salt);
                }

                // HKDF Expand: T(0) = empty, T(i) = HMAC-Hash(PRK, T(i-1) | info | 0x01)
                byte[] infoBytes = Encoding.UTF8.GetBytes(info);
                byte[] counter = new byte[] { 0x01 }; // First iteration

                byte[] dataToHash = CombineArrays(
                    infoBytes,
                    counter);

                // First HMAC with salt as key
                using var hmacWithSalt = new HMACSHA256(salt);
                byte[] prk = hmacWithSalt.ComputeHash(sharedSecret);

                // Expand using PRK
                using var hmacWithPrk = new HMACSHA256(prk);
                byte[] result = hmacWithPrk.ComputeHash(dataToHash);

                // Take first 32 bytes for AES-256 key
                byte[] sessionKey = new byte[32];
                int bytesToCopy = Math.Min(result.Length, 32);
                Buffer.BlockCopy(result, 0, sessionKey, 0, bytesToCopy);

                return sessionKey;
            }
            catch (Exception ex)
            {
                // Fallback to simple KDF if HKDF fails
                return DeriveSessionKeySimple(sharedSecret, info);
            }
        }

        /// <summary>
        /// Simple KDF fallback using SHA256
        /// </summary>
        private byte[] DeriveSessionKeySimple(byte[] sharedSecret, string info)
        {
            try
            {
                using var sha256 = SHA256.Create();

                // Combine shared secret with context info
                byte[] infoBytes = Encoding.UTF8.GetBytes(info);
                byte[] combined = new byte[sharedSecret.Length + infoBytes.Length];

                Buffer.BlockCopy(sharedSecret, 0, combined, 0, sharedSecret.Length);
                Buffer.BlockCopy(infoBytes, 0, combined, sharedSecret.Length, infoBytes.Length);

                // Hash multiple times for better key derivation
                byte[] keyMaterial = sha256.ComputeHash(combined);
                keyMaterial = sha256.ComputeHash(keyMaterial); // Second round

                // Ensure 32 bytes for AES-256
                byte[] sessionKey = new byte[32];
                int bytesToCopy = Math.Min(keyMaterial.Length, 32);
                Buffer.BlockCopy(keyMaterial, 0, sessionKey, 0, bytesToCopy);

                return sessionKey;
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Session key derivation failed", ex);
            }
        }

        /// <summary>
        /// Complete key exchange: derive session key from other party's public key
        /// </summary>
        public byte[] DeriveSessionKeyFromExchange(byte[] otherPartyPublicKey)
        {
            try
            {
                // 1. Derive shared secret using ECDH
                byte[] sharedSecret = DeriveSharedSecret(otherPartyPublicKey);

                // 2. Derive session key from shared secret using KDF
                byte[] sessionKey = DeriveSessionKey(sharedSecret);

                // DEBUG: Log key info
                // Console.WriteLine($"[KEY EXCHANGE] Shared secret: {BitConverter.ToString(sharedSecret).Replace("-", "").Substring(0, 16)}...");
                // Console.WriteLine($"[KEY EXCHANGE] Session key: {BitConverter.ToString(sessionKey).Replace("-", "").Substring(0, 16)}...");

                return sessionKey;
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Key exchange failed", ex);
            }
        }

        /// <summary>
        /// Verify that a public key is valid
        /// </summary>
        public bool ValidatePublicKey(byte[] publicKey)
        {
            try
            {
                using var testDh = ECDiffieHellman.Create();
                testDh.ImportSubjectPublicKeyInfo(publicKey, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private byte[] CombineArrays(params byte[][] arrays)
        {
            int totalLength = 0;
            foreach (var array in arrays)
            {
                if (array != null)
                    totalLength += array.Length;
            }

            byte[] result = new byte[totalLength];
            int offset = 0;

            foreach (var array in arrays)
            {
                if (array != null && array.Length > 0)
                {
                    Buffer.BlockCopy(array, 0, result, offset, array.Length);
                    offset += array.Length;
                }
            }

            return result;
        }

        public void Dispose()
        {
            _dh?.Dispose();
        }
    }
}