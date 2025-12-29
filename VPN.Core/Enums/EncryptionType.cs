using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Enums
{
    /// <summary>
    /// Supported encryption algorithms
    /// </summary>
    public enum EncryptionType
    {
        None = 0,               // No encryption (for testing)
        AES128 = 1,             // AES 128-bit
        AES256 = 2,             // AES 256-bit (recommended)
        ChaCha20 = 3            // ChaCha20 stream cipher
    }
}