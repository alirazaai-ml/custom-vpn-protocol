using System;
using System.Security.Cryptography;
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
        private bool _isInitialized = false;

        public CryptoManager()
        {
            _aes = new AesEncryption();
            _keyExchange = new KeyExchange();
        }

        /// <summary>
        /// Initialize encryption with session key
        /// </summary>
        public void Initialize(byte[] sessionKey)
        {
            if (sessionKey == null || sessionKey.Length != 32)
                throw new EncryptionException("Session key must be 32 bytes for AES-256");

            _sessionKey = sessionKey;
            _isInitialized = true;
        }

        /// <summary>
        /// Encrypt VPN packet - IV is generated fresh for EACH packet
        /// </summary>
        public VpnPacket EncryptPacket(VpnPacket packet)
        {
            if (!_isInitialized)
                throw new EncryptionException("CryptoManager not initialized. Call Initialize() first.");

            try
            {
                // Generate NEW IV for this packet (CBC requires unique IV per packet)
                byte[] iv = GenerateIV();

                // Encrypt payload with this IV
                byte[] encryptedPayload = _aes.Encrypt(packet.Payload, _sessionKey, iv);

                // Create packet data for HMAC (includes IV to prevent tampering)
                byte[] packetDataForHmac = CreatePacketDataForHmac(packet, encryptedPayload, iv);

                // Compute HMAC for integrity
                byte[] hmac = HashHelper.ComputeHmacSha256(packetDataForHmac, _sessionKey);

                // Update packet - store IV with encrypted payload
                byte[] encryptedPayloadWithIv = CombineArrays(iv, encryptedPayload);
                packet.Payload = encryptedPayloadWithIv;
                packet.PayloadLength = (ushort)encryptedPayloadWithIv.Length;
                packet.Hmac = hmac;

                // DEBUG: Log encryption info
                // Console.WriteLine($"[ENCRYPT] IV: {BitConverter.ToString(iv).Replace("-", "")}");
                // Console.WriteLine($"[ENCRYPT] Plaintext: {packet.Payload.Length} → Encrypted: {encryptedPayloadWithIv.Length}");

                return packet;
            }
            catch (Exception ex)
            {
                throw new EncryptionException("Packet encryption failed", ex);
            }
        }

        /// <summary>
        /// Decrypt VPN packet - extracts IV from payload
        /// </summary>
        public VpnPacket DecryptPacket(VpnPacket packet)
        {
            if (!_isInitialized)
                throw new EncryptionException("CryptoManager not initialized. Call Initialize() first.");

            try
            {
                // Extract IV from beginning of payload (first 16 bytes)
                if (packet.Payload.Length < 16)
                    throw new EncryptionException("Packet too short to contain IV");

                byte[] iv = new byte[16];
                Buffer.BlockCopy(packet.Payload, 0, iv, 0, 16);

                // Get encrypted data (after IV)
                byte[] encryptedData = new byte[packet.Payload.Length - 16];
                Buffer.BlockCopy(packet.Payload, 16, encryptedData, 0, encryptedData.Length);

                // ✅ FIX: Create HMAC data with ORIGINAL encrypted payload length for verification
                var originalPacket = new VpnPacket
                {
                    Version = packet.Version,
                    Type = packet.Type,
                    SessionId = packet.SessionId,
                    SequenceNumber = packet.SequenceNumber,
                    PayloadLength = (ushort)encryptedData.Length // Use encrypted data length, not total payload length
                };

                byte[] packetDataForHmac = CreatePacketDataForHmac(originalPacket, encryptedData, iv);

                // Verify HMAC first
                if (!HashHelper.VerifyHmac(packetDataForHmac, packet.Hmac, _sessionKey))
                {
                    throw new EncryptionException("HMAC verification failed - packet may be tampered");
                }

                // Decrypt payload
                byte[] decryptedPayload = _aes.Decrypt(encryptedData, _sessionKey, iv);
                packet.Payload = decryptedPayload;
                packet.PayloadLength = (ushort)decryptedPayload.Length;

                // DEBUG: Enhanced logging for troubleshooting
                #if DEBUG
                Console.WriteLine($"[DECRYPT] IV: {BitConverter.ToString(iv).Replace("-", "")}");
                Console.WriteLine($"[DECRYPT] Encrypted: {encryptedData.Length} → Decrypted: {decryptedPayload.Length} bytes");
                Console.WriteLine($"[DECRYPT] HMAC verification: PASSED");
                #endif

                return packet;
            }
            catch (Exception ex)
            {
                // ✅ Enhanced error logging
                #if DEBUG
                Console.WriteLine($"[DECRYPT ERROR] {ex.Message}");
                if (packet != null)
                {
                    Console.WriteLine($"[DECRYPT ERROR] Packet length: {packet.Payload?.Length ?? 0}");
                    Console.WriteLine($"[DECRYPT ERROR] Packet type: {packet.Type}");
                }
                #endif
                throw new EncryptionException("Packet decryption failed", ex);
            }
        }

        /// <summary>
        /// Create consistent packet data for HMAC calculation
        /// </summary>
        private byte[] CreatePacketDataForHmac(VpnPacket packet, byte[] encryptedPayload, byte[] iv)
        {
            // Combine ALL important packet fields + IV
            // This ensures no tampering with packet metadata
            byte[] versionBytes = new byte[] { packet.Version };
            byte[] typeBytes = new byte[] { (byte)packet.Type };
            byte[] sessionIdBytes = BitConverter.GetBytes(packet.SessionId);
            byte[] sequenceBytes = BitConverter.GetBytes(packet.SequenceNumber);
            byte[] payloadLengthBytes = BitConverter.GetBytes(packet.PayloadLength);

            // Combine: version(1) + type(1) + sessionId(4) + sequence(4) + payloadLength(2) + IV(16) + encryptedPayload(n)
            return CombineArrays(
                versionBytes,
                typeBytes,
                sessionIdBytes,
                sequenceBytes,
                payloadLengthBytes,
                iv,
                encryptedPayload);
        }

        /// <summary>
        /// Generate unique IV for each packet
        /// </summary>
        private byte[] GenerateIV()
        {
            using var rng = RandomNumberGenerator.Create();
            byte[] iv = new byte[16]; // AES block size = 16 bytes
            rng.GetBytes(iv);
            return iv;
        }

        /// <summary>
        /// Perform key exchange and generate session key
        /// </summary>
        public byte[] PerformKeyExchange(byte[] otherPartyPublicKey)
        {
            try
            {
                // Derive shared secret and session key
                return _keyExchange.DeriveSessionKeyFromExchange(otherPartyPublicKey);
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
        public byte[] GenerateNewSessionKey()
        {
            return _aes.GenerateKey();
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
            _aes?.Dispose();
            _keyExchange?.Dispose();

            // Clear sensitive data from memory
            if (_sessionKey != null)
            {
                Array.Clear(_sessionKey, 0, _sessionKey.Length);
            }
        }
    }
}