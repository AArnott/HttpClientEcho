// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Validation;

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
        /// The HTTP message cache.
        /// </summary>
        private readonly HttpMessageCache cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="RecordingMessageHandler"/> class.
        /// </summary>
        /// <param name="behaviors">Specialized behaviors with HTTP caching.</param>
        /// <param name="innerHandler">The inner message handler.</param>
        internal RecordingMessageHandler(EchoBehaviors behaviors, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            this.behaviors = behaviors;
            this.Settings = RuntimeSettings.Get();
            Verify.Operation(this.Settings != null || (!behaviors.HasFlag(EchoBehaviors.AllowReplay) && !behaviors.HasFlag(EchoBehaviors.RecordResponses)), "Cannot record or replay without a settings file.");
            this.cache = new HttpMessageCache(this.Settings.PlaybackRuntimePath, this.Settings.RecordingSourcePath);
        }

        /// <summary>
        /// Gets the runtime settings that are configured when the test project is built.
        /// </summary>
        internal RuntimeSettings Settings { get; }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (this.behaviors.HasFlag(EchoBehaviors.AllowReplay) && this.cache.TryLookup(request, out HttpResponseMessage response))
            {
                return response;
            }

            if (!this.behaviors.HasFlag(EchoBehaviors.AllowNetworkCalls))
            {
                throw new NoEchoCacheException();
            }

            response = await base.SendAsync(request, cancellationToken);

            if (this.behaviors.HasFlag(EchoBehaviors.RecordResponses))
            {
                await this.cache.StoreAsync(request, response);
            }

            return response;
        }
    }
}
