using System;
using System.Linq;
using System.Text;
using VPN.Core.Security;
using VPN.Core.Models;
using VPN.Core.Enums;
using VPN.Core.Exceptions;

namespace VPN.Tests
{
    /// <summary>
    /// Comprehensive unit tests for CryptoManager encryption/decryption
    /// </summary>
    public class CryptoManagerTests
    {
        public void TestEncryptDecryptRoundTrip()
        {
            // Arrange
            var cryptoManager = new CryptoManager();
            byte[] sessionKey = GenerateRandomBytes(32);
            cryptoManager.Initialize(sessionKey);

            var originalData = Encoding.UTF8.GetBytes("Hello VPN World!");
            var packet = new VpnPacket
            {
                Version = 1,
                Type = PacketType.Data,
                SessionId = 12345,
                SequenceNumber = 1,
                Payload = originalData,
                PayloadLength = (ushort)originalData.Length
            };

            // Act - Encrypt
            var encryptedPacket = cryptoManager.EncryptPacket(packet);
            
            // Verify encryption changed the data
            if (originalData.SequenceEqual(encryptedPacket.Payload))
                throw new Exception("? Encryption failed - data not changed");
            
            if (encryptedPacket.Payload.Length <= originalData.Length)
                throw new Exception("? Encrypted payload should be larger (IV + encrypted data)");
            
            if (encryptedPacket.Hmac == null || encryptedPacket.Hmac.Length != 32)
                throw new Exception("? HMAC not generated correctly");

            // Act - Decrypt
            var decryptedPacket = cryptoManager.DecryptPacket(encryptedPacket);

            // Assert
            if (!originalData.SequenceEqual(decryptedPacket.Payload))
                throw new Exception("? Decryption failed - data doesn't match");
            
            if (decryptedPacket.PayloadLength != originalData.Length)
                throw new Exception("? Payload length mismatch after decryption");

            Console.WriteLine("? Encrypt/Decrypt round-trip test PASSED");
        }

        public void TestMultiplePacketsDifferentIVs()
        {
            // Arrange
            var cryptoManager = new CryptoManager();
            byte[] sessionKey = GenerateRandomBytes(32);
            cryptoManager.Initialize(sessionKey);

            var data = Encoding.UTF8.GetBytes("Same data for all packets");
            var packet1 = CreateTestPacket(1, data);
            var packet2 = CreateTestPacket(2, data);
            var packet3 = CreateTestPacket(3, data);

            // Act - Encrypt all packets
            var encrypted1 = cryptoManager.EncryptPacket(packet1);
            var encrypted2 = cryptoManager.EncryptPacket(packet2);
            var encrypted3 = cryptoManager.EncryptPacket(packet3);

            // Assert - All encrypted payloads should be different (due to different IVs)
            if (encrypted1.Payload.SequenceEqual(encrypted2.Payload))
                throw new Exception("? Encrypted packets should have different IVs");
            
            if (encrypted2.Payload.SequenceEqual(encrypted3.Payload))
                throw new Exception("? Encrypted packets should have different IVs");

            // But decrypted payloads should be the same
            var decrypted1 = cryptoManager.DecryptPacket(encrypted1);
            var decrypted2 = cryptoManager.DecryptPacket(encrypted2);
            var decrypted3 = cryptoManager.DecryptPacket(encrypted3);

            if (!decrypted1.Payload.SequenceEqual(decrypted2.Payload) ||
                !decrypted2.Payload.SequenceEqual(decrypted3.Payload))
                throw new Exception("? Decrypted payloads should be identical");

            Console.WriteLine("? Multiple packets with different IVs test PASSED");
        }

        public void TestHMACTamperDetection()
        {
            // Arrange
            var cryptoManager = new CryptoManager();
            byte[] sessionKey = GenerateRandomBytes(32);
            cryptoManager.Initialize(sessionKey);

            var packet = CreateTestPacket(1, Encoding.UTF8.GetBytes("Secret data"));
            var encryptedPacket = cryptoManager.EncryptPacket(packet);

            // Tamper with HMAC
            encryptedPacket.Hmac[0] ^= 0xFF;

            // Act & Assert
            try
            {
                cryptoManager.DecryptPacket(encryptedPacket);
                throw new Exception("? Should have detected HMAC tampering");
            }
            catch (EncryptionException ex)
            {
                if (!ex.Message.Contains("HMAC verification failed"))
                    throw new Exception("? Wrong exception message for HMAC failure");
                
                Console.WriteLine("? HMAC tamper detection test PASSED");
            }
        }

        public void TestPayloadTamperDetection()
        {
            // Arrange
            var cryptoManager = new CryptoManager();
            byte[] sessionKey = GenerateRandomBytes(32);
            cryptoManager.Initialize(sessionKey);

            var packet = CreateTestPacket(1, Encoding.UTF8.GetBytes("Secret data"));
            var encryptedPacket = cryptoManager.EncryptPacket(packet);

            // Tamper with encrypted payload (after IV)
            if (encryptedPacket.Payload.Length > 20)
            {
                encryptedPacket.Payload[20] ^= 0xFF;
            }

            // Act & Assert
            try
            {
                cryptoManager.DecryptPacket(encryptedPacket);
                throw new Exception("? Should have detected payload tampering");
            }
            catch (EncryptionException ex)
            {
                if (!ex.Message.Contains("HMAC verification failed"))
                    throw new Exception("? Wrong exception message for payload tampering");
                
                Console.WriteLine("? Payload tamper detection test PASSED");
            }
        }

        public void TestEmptyPayload()
        {
            // Arrange
            var cryptoManager = new CryptoManager();
            byte[] sessionKey = GenerateRandomBytes(32);
            cryptoManager.Initialize(sessionKey);

            var packet = new VpnPacket
            {
                Version = 1,
                Type = PacketType.KeepAlive,
                SessionId = 99999,
                SequenceNumber = 100,
                Payload = Array.Empty<byte>(),
                PayloadLength = 0
            };

            // Act & Assert
            var encryptedPacket = cryptoManager.EncryptPacket(packet);
            var decryptedPacket = cryptoManager.DecryptPacket(encryptedPacket);

            if (decryptedPacket.Payload.Length != 0)
                throw new Exception("? Empty payload should remain empty");

            Console.WriteLine("? Empty payload test PASSED");
        }

        public void TestCrossManagerCompatibility()
        {
            // Arrange - Two different CryptoManager instances with same session key
            var sessionKey = GenerateRandomBytes(32);
            
            var encryptionManager = new CryptoManager();
            encryptionManager.Initialize(sessionKey);
            
            var decryptionManager = new CryptoManager();
            decryptionManager.Initialize(sessionKey);

            var originalData = Encoding.UTF8.GetBytes("Cross-manager test data");
            var packet = CreateTestPacket(42, originalData);

            // Act - Encrypt with first manager, decrypt with second
            var encryptedPacket = encryptionManager.EncryptPacket(packet);
            var decryptedPacket = decryptionManager.DecryptPacket(encryptedPacket);

            // Assert
            if (!originalData.SequenceEqual(decryptedPacket.Payload))
                throw new Exception("? Cross-manager decryption failed");

            Console.WriteLine("? Cross-manager compatibility test PASSED");
        }

        public void RunAllTests()
        {
            Console.WriteLine("?? Running CryptoManager Unit Tests...");
            Console.WriteLine("=" * 50);

            try
            {
                TestEncryptDecryptRoundTrip();
                TestMultiplePacketsDifferentIVs();
                TestHMACTamperDetection();
                TestPayloadTamperDetection();
                TestEmptyPayload();
                TestCrossManagerCompatibility();

                Console.WriteLine("=" * 50);
                Console.WriteLine("?? ALL TESTS PASSED! CryptoManager is working correctly.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("=" * 50);
                Console.WriteLine($"? TEST FAILED: {ex.Message}");
                throw;
            }
        }

        // Helper methods
        private static byte[] GenerateRandomBytes(int length)
        {
            var bytes = new byte[length];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }

        private static VpnPacket CreateTestPacket(int sequenceNumber, byte[] payload)
        {
            return new VpnPacket
            {
                Version = 1,
                Type = PacketType.Data,
                SessionId = 12345,
                SequenceNumber = sequenceNumber,
                Payload = payload,
                PayloadLength = (ushort)payload.Length
            };
        }
    }
}