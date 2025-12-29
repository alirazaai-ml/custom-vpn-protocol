using System;
using System.Collections.Generic;
using System.Text;

using VPN.Core.Models;

namespace VPN.Core.Interfaces
{
    /// <summary>
    /// Interface for encryption management
    /// </summary>
    public interface ICryptoManager
    {
        /// <summary>
        /// Initialize with session key and IV
        /// </summary>
        void Initialize(byte[] sessionKey, byte[] iv);

        /// <summary>
        /// Encrypt a VPN packet
        /// </summary>
        VpnPacket EncryptPacket(VpnPacket packet);

        /// <summary>
        /// Decrypt a VPN packet
        /// </summary>
        VpnPacket DecryptPacket(VpnPacket packet);

        /// <summary>
        /// Perform key exchange with other party
        /// </summary>
        (byte[] sessionKey, byte[] iv) PerformKeyExchange(byte[] otherPartyPublicKey);

        /// <summary>
        /// Get our public key for key exchange
        /// </summary>
        byte[] GetPublicKey();

        /// <summary>
        /// Generate new session key
        /// </summary>
        (byte[] newSessionKey, byte[] newIv) GenerateNewSessionKey();
    }
}