// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;

    /// <summary>
    /// An exception thrown when there is a cache miss and the <see cref="EchoBehaviors.DenyNetworkCalls"/> flag is set.
    /// </summary>
#if NETSTANDARD2_0 || NET45
    [Serializable]
#endif
    public class NoEchoCacheException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NoEchoCacheException"/> class.
        /// </summary>
        public NoEchoCacheException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NoEchoCacheException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public NoEchoCacheException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="NoEchoCacheException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="inner">The inner exception.</param>
        public NoEchoCacheException(string message, Exception inner)
            : base(message, inner)
        {
        }

#if NETSTANDARD2_0 || NET45
        /// <summary>
        /// Initializes a new instance of the <see cref="NoEchoCacheException"/> class
        /// by deserializing it.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
        protected NoEchoCacheException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
