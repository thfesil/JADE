using System;

namespace Jade.Core
{
    public class JadeException : Exception
    {
        public int ErrorCode { get; }

        public JadeException(int errorCode, string message)
            : base($"{message} (Codigo: {errorCode})")
        {
            ErrorCode = errorCode;
        }

        public bool IsRidExhausted => ErrorCode == 8209;
    }
}
