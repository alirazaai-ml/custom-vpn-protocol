using System;
using System.Collections.Generic;
using System.Text;

using System.Security.Cryptography;
using VPN.Core.Exceptions;

namespace VPN.Core.Security
{
    /// <summary>
    /// AES encryption/decryption implementation
    /// </summary>
    public class AesEncryption
    {
        private readonly Aes _aes;

        public AesEncryption()
        {
            _aes = Aes.Create();
            _aes.Mode = CipherMode.CBC;      // Cipher Block Chaining
            _aes.Padding = PaddingMode.PKCS7; // PKCS7 padding
        }

        /// <summary>
        /// Encrypt data using AES
        /// </summary>
        public byte[] Encrypt(byte[] data, byte[] key, byte[] iv)
        {
            try
            {
                ValidateKeyAndIV(key, iv);

                _aes.Key = key;
                _aes.IV = iv;

                using var encryptor = _aes.CreateEncryptor();
                return encryptor.TransformFinalBlock(data, 0, data.Length);
            }
            catch (CryptographicException ex)
            {
                throw new EncryptionException("AES encryption failed", ex);
            }
        }

        /// <summary>
        /// Decrypt data using AES
        /// </summary>
        public byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv)
        {
            try
            {
                ValidateKeyAndIV(key, iv);

                _aes.Key = key;
                _aes.IV = iv;

                using var decryptor = _aes.CreateDecryptor();
                return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
            }
            catch (CryptographicException ex)
            {
                throw new EncryptionException("AES decryption failed", ex);
            }
        }

        /// <summary>
        /// Generate random key for AES-256 (32 bytes = 256 bits)
        /// </summary>
        public byte[] GenerateKey()
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] key = new byte[32]; // 256 bits
            rng.GetBytes(key);
            return key;
        }

        /// <summary>
        /// Generate random IV (16 bytes for AES)
        /// </summary>
        public byte[] GenerateIV()
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] iv = new byte[16];
            rng.GetBytes(iv);
            return iv;
        }

        private void ValidateKeyAndIV(byte[] key, byte[] iv)
        {
            if (key == null || key.Length != 32)
                throw new EncryptionException("Invalid key size. Must be 32 bytes for AES-256.");

            if (iv == null || iv.Length != 16)
                throw new EncryptionException("Invalid IV size. Must be 16 bytes for AES.");
        }

        public void Dispose()
        {
            _aes?.Dispose();
        }
    }
}
