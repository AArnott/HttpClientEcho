// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Net.Http;

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that records traffic.
    /// </summary>
    internal class RecordingMessageHandler : DelegatingHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingMessageHandler"/> class.
        /// </summary>
        /// <param name="innerHandler">The inner message handler.</param>
        internal RecordingMessageHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }
    }
}
