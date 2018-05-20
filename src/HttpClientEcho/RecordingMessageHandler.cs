// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An <see cref="HttpMessageHandler"/> that records traffic.
    /// </summary>
    internal class RecordingMessageHandler : DelegatingHandler
    {
        /// <summary>
        /// Specialized behaviors with HTTP caching.
        /// </summary>
        private readonly EchoBehaviors behaviors;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingMessageHandler"/> class.
        /// </summary>
        /// <param name="behaviors">Specialized behaviors with HTTP caching.</param>
        /// <param name="innerHandler">The inner message handler.</param>
        internal RecordingMessageHandler(EchoBehaviors behaviors, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            this.behaviors = behaviors;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // We don't yet even have a cache yet.
            if (!this.behaviors.HasFlag(EchoBehaviors.AllowNetworkCalls))
            {
                throw new NoEchoCacheException();
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
