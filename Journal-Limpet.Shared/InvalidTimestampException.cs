using System;
using System.Runtime.Serialization;

namespace Journal_Limpet.Shared
{
    [Serializable]
    public class InvalidTimestampException : Exception
    {
        public InvalidTimestampException()
        {
        }

        public InvalidTimestampException(string message) : base(message)
        {
        }

        public InvalidTimestampException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected InvalidTimestampException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}