using System;
using System.Collections.Generic;
using System.Text;

namespace VPN.Core.Exceptions
{
    /// <summary>
    /// Base exception for VPN-related errors
    /// </summary>
    public class VpnException : Exception
    {
        public int ErrorCode { get; set; }

        public VpnException() : base("VPN operation failed")
        {
        }

        public VpnException(string message) : base(message)
        {
        }

        public VpnException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        public VpnException(string message, int errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
        }

        public VpnException(string message, int errorCode, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }
}