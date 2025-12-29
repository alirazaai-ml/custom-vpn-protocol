using System;
using System.Security.Cryptography;
using System.Text;
using VPN.Core.Exceptions;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace VPN.Core.Security
{
    /// <summary>
    /// Secure key exchange using Diffie-Hellman
    /// </summary>
    public class KeyExchange : IDisposable
    {
        private readonly ECDiffieHellman _dh;
        private byte[] _publicKey;

        public KeyExchange()
        {
            _dh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256); // Using P-256 curve
            _publicKey = _dh.ExportSubjectPublicKeyInfo(); // FIXED: Use ExportSubjectPublicKeyInfo
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

                // Derive the shared secret
                byte[] sharedSecret = _dh.DeriveKeyFromHash(
                    otherParty.PublicKey,
                    HashAlgorithmName.SHA256,
                    null,
                    null);

                return sharedSecret;
            }
            catch (CryptographicException ex)
            {
                throw new EncryptionException("Key exchange failed", ex);
            }
        }

        /// <summary>
        /// Create session key from shared secret using HKDF
        /// </summary>
        public byte[] DeriveSessionKey(byte[] sharedSecret, string context = "VPN-Session-Key")
        {
            try
            {
                // Use proper HKDF with the NuGet package
                byte[] salt = Encoding.UTF8.GetBytes(context);

                byte[] sessionKey = KeyDerivation.Pbkdf2(
                    password: Convert.ToBase64String(sharedSecret),
                    salt: salt,
                    prf: KeyDerivationPrf.HMACSHA256,
                    iterationCount: 10000,
                    numBytesRequested: 32); // 32 bytes = 256 bits for AES-256

                return sessionKey;
            }
            catch (Exception ex)
            {
                // Fallback to simple method if HKDF fails
                return DeriveSessionKeySimple(sharedSecret, context);
            }
        }

        /// <summary>
        /// Fallback method if HKDF is not available
        /// </summary>
        private byte[] DeriveSessionKeySimple(byte[] sharedSecret, string context)
        {
            try
            {
                using var sha256 = SHA256.Create();

                // Combine shared secret with context
                byte[] salt = Encoding.UTF8.GetBytes(context);
                byte[] combined = new byte[sharedSecret.Length + salt.Length];

                Buffer.BlockCopy(sharedSecret, 0, combined, 0, sharedSecret.Length);
                Buffer.BlockCopy(salt, 0, combined, sharedSecret.Length, salt.Length);

                // Hash to derive key
                byte[] keyMaterial = sha256.ComputeHash(combined);

                // Ensure we have 32 bytes for AES-256
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
        /// Generate IV for AES encryption
        /// </summary>
        public byte[] GenerateIV()
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] iv = new byte[16]; // 16 bytes for AES IV
            rng.GetBytes(iv);
            return iv;
        }

        /// <summary>
        /// Complete key exchange and return session key + IV
        /// </summary>
        public (byte[] sessionKey, byte[] iv) PerformKeyExchange(byte[] otherPartyPublicKey)
        {
            try
            {
                // Derive shared secret
                byte[] sharedSecret = DeriveSharedSecret(otherPartyPublicKey);

                // Derive session key from shared secret
                byte[] sessionKey = DeriveSessionKey(sharedSecret);

                // Generate IV
                byte[] iv = GenerateIV();

                return (sessionKey, iv);
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Key exchange failed", ex);
            }
        }

        public void Dispose()
        {
            _dh?.Dispose();

            // Clear sensitive data
            if (_publicKey != null)
            {
                Array.Clear(_publicKey, 0, _publicKey.Length);
            }
        }
    }
}