// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;

    /// <summary>
    /// An exception thrown when the cache file cannot be deserialized.
    /// </summary>
#if NETSTANDARD2_0 || NET45
    [Serializable]
#endif
    public class BadCacheFileException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BadCacheFileException"/> class.
        /// </summary>
        public BadCacheFileException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BadCacheFileException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public BadCacheFileException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BadCacheFileException"/> class.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="inner">The inner exception.</param>
        public BadCacheFileException(string message, Exception inner)
            : base(message, inner)
        {
        }

#if NETSTANDARD2_0 || NET45
        /// <summary>
        /// Initializes a new instance of the <see cref="BadCacheFileException"/> class
        /// by deserializing it.
        /// </summary>
        /// <param name="info">Serialization info.</param>
        /// <param name="context">Serialization context.</param>
        protected BadCacheFileException(
          System.Runtime.Serialization.SerializationInfo info,
          System.Runtime.Serialization.StreamingContext context)
            : base(info, context)
        {
        }
#endif
    }
}
