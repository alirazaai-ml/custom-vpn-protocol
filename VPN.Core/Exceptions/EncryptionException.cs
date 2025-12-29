using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Exceptions
{
    /// <summary>
    /// Exception for encryption/decryption failures
    /// </summary>
    public class EncryptionException : VpnException
    {
        public EncryptionException() : base("Encryption operation failed")
        {
        }

        public EncryptionException(string message) : base(message)
        {
        }

        public EncryptionException(string message, System.Exception innerException)
            : base(message, innerException)
        {
        }

        public EncryptionException(string message, int errorCode)
            : base(message, errorCode)
        {
        }
    }
}