using System;
using System.Security.Cryptography;
using VPN.Core.Exceptions;

namespace VPN.Core.Security
{
    /// <summary>
    /// AES-256 encryption implementation
    /// </summary>
    public class AesEncryption : IDisposable
    {
        private const int KEY_SIZE = 256; // AES-256
        private const int BLOCK_SIZE = 128; // AES block size

        public byte[] Encrypt(byte[] plaintext, byte[] key, byte[] iv)
        {
            if (plaintext == null || plaintext.Length == 0)
                throw new ArgumentException("Plaintext cannot be null or empty");

            if (key == null || key.Length != 32) // 32 bytes = 256 bits
                throw new EncryptionException("Key must be 32 bytes (256 bits) for AES-256");

            if (iv == null || iv.Length != 16) // 16 bytes = 128 bits
                throw new EncryptionException("IV must be 16 bytes (128 bits) for AES");

            using (var aes = Aes.Create())
            {
                aes.KeySize = KEY_SIZE;
                aes.BlockSize = BLOCK_SIZE;
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC; // Cipher Block Chaining
                aes.Padding = PaddingMode.PKCS7; // Standard padding

                using (var encryptor = aes.CreateEncryptor())
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cs.Write(plaintext, 0, plaintext.Length);
                        cs.FlushFinalBlock();
                        return ms.ToArray();
                    }
                }
            }
        }

        public byte[] Decrypt(byte[] ciphertext, byte[] key, byte[] iv)
        {
            if (ciphertext == null || ciphertext.Length == 0)
                throw new ArgumentException("Ciphertext cannot be null or empty");

            if (key == null || key.Length != 32)
                throw new EncryptionException("Key must be 32 bytes (256 bits) for AES-256");

            if (iv == null || iv.Length != 16)
                throw new EncryptionException("IV must be 16 bytes (128 bits) for AES");

            using (var aes = Aes.Create())
            {
                aes.KeySize = KEY_SIZE;
                aes.BlockSize = BLOCK_SIZE;
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                using (var ms = new System.IO.MemoryStream(ciphertext))
                using (var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (var result = new System.IO.MemoryStream())
                {
                    cs.CopyTo(result);
                    return result.ToArray();
                }
            }
        }

        public byte[] GenerateKey()
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = KEY_SIZE;
                aes.GenerateKey();
                return aes.Key;
            }
        }

        public byte[] GenerateIV()
        {
            using (var aes = Aes.Create())
            {
                aes.GenerateIV();
                return aes.IV;
            }
        }

        public void Dispose()
        {
            // Nothing to dispose in this implementation
        }
    }
}