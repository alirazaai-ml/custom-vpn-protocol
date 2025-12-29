using System;
using System.Text;
using VPN.Core.Exceptions;
using VPN.Core.Models;

namespace VPN.Core.Security
{
    /// <summary>
    /// Main encryption controller for VPN
    /// </summary>
    public class CryptoManager : IDisposable
    {
        private readonly AesEncryption _aes;
        private readonly KeyExchange _keyExchange;
        private byte[] _sessionKey = Array.Empty<byte>();
        private byte[] _iv = Array.Empty<byte>();
        private bool _isInitialized = false;

        public CryptoManager()
        {
            _aes = new AesEncryption();
            _keyExchange = new KeyExchange();
        }

        /// <summary>
        /// Initialize encryption with session key
        /// </summary>
        public void Initialize(byte[] sessionKey, byte[] iv)
        {
            if (sessionKey == null || sessionKey.Length != 32)
                throw new EncryptionException("Session key must be 32 bytes for AES-256");

            if (iv == null || iv.Length != 16)
                throw new EncryptionException("IV must be 16 bytes for AES");

            _sessionKey = sessionKey;
            _iv = iv;
            _isInitialized = true;
        }

        /// <summary>
        /// Encrypt VPN packet
        /// </summary>
        public VpnPacket EncryptPacket(VpnPacket packet)
        {
            if (!_isInitialized)
                throw new EncryptionException("CryptoManager not initialized. Call Initialize() first.");

            try
            {
                // Encrypt payload
                byte[] encryptedPayload = _aes.Encrypt(packet.Payload, _sessionKey, _iv);

                // Create HMAC for integrity
                byte[] dataToHash = CombineArrays(
                    BitConverter.GetBytes(packet.SequenceNumber),
                    encryptedPayload);

                byte[] hmac = HashHelper.ComputeHmacSha256(dataToHash, _sessionKey);

                // Update packet
                packet.Payload = encryptedPayload;
                packet.PayloadLength = (ushort)encryptedPayload.Length;
                packet.Hmac = hmac;

                return packet;
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Packet encryption failed", ex);
            }
        }

        /// <summary>
        /// Decrypt VPN packet
        /// </summary>
        public VpnPacket DecryptPacket(VpnPacket packet)
        {
            if (!_isInitialized)
                throw new EncryptionException("CryptoManager not initialized. Call Initialize() first.");

            try
            {
                // Verify HMAC first
                byte[] dataToVerify = CombineArrays(
                    BitConverter.GetBytes(packet.SequenceNumber),
                    packet.Payload);

                if (!HashHelper.VerifyHmac(dataToVerify, packet.Hmac, _sessionKey))
                {
                    throw new EncryptionException("HMAC verification failed - packet may be tampered");
                }

                // Decrypt payload
                byte[] decryptedPayload = _aes.Decrypt(packet.Payload, _sessionKey, _iv);
                packet.Payload = decryptedPayload;

                return packet;
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Packet decryption failed", ex);
            }
        }

        /// <summary>
        /// Perform key exchange and generate session key
        /// </summary>
        public (byte[] sessionKey, byte[] iv) PerformKeyExchange(byte[] otherPartyPublicKey)
        {
            try
            {
                // Use KeyExchange class for the actual exchange
                return _keyExchange.PerformKeyExchange(otherPartyPublicKey);
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Key exchange failed", ex);
            }
        }

        /// <summary>
        /// Get our public key for key exchange
        /// </summary>
        public byte[] GetPublicKey() => _keyExchange.GetPublicKey();

        /// <summary>
        /// Generate new session key (for rekeying)
        /// </summary>
        public (byte[] newSessionKey, byte[] newIv) GenerateNewSessionKey()
        {
            byte[] newSessionKey = _aes.GenerateKey();
            byte[] newIv = _aes.GenerateIV();
            return (newSessionKey, newIv);
        }

        private byte[] CombineArrays(params byte[][] arrays)
        {
            int totalLength = 0;
            foreach (var array in arrays)
                totalLength += array.Length;

            byte[] result = new byte[totalLength];
            int offset = 0;

            foreach (var array in arrays)
            {
                Buffer.BlockCopy(array, 0, result, offset, array.Length);
                offset += array.Length;
            }

            return result;
        }

        public void Dispose()
        {
            _aes?.Dispose();
            _keyExchange?.Dispose();

            // Clear sensitive data from memory
            if (_sessionKey != null)
                Array.Clear(_sessionKey, 0, _sessionKey.Length);

            if (_iv != null)
                Array.Clear(_iv, 0, _iv.Length);
        }
    }
}