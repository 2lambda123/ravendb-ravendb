using System;
using System.Runtime.Serialization;

namespace Raven.Abstractions.Exceptions
{
    /// <summary>
    /// This exception is raised when the server is asked to perform an operation on an unsupported storage type.
    /// </summary>
    [Serializable]
    public class StorageNotSupportedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StorageNotSupportedException"/> class.
        /// </summary>
        public StorageNotSupportedException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageNotSupportedException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        public StorageNotSupportedException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageNotSupportedException"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="inner">The inner.</param>
        public StorageNotSupportedException(string message, Exception inner)
            : base(message, inner)
        {
        }

#if !DNXCORE50
        protected StorageNotSupportedException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
