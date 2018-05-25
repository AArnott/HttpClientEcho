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
    public class EchoMessageHandler : DelegatingHandler
    {
        /// <summary>
        /// The HTTP message cache.
        /// </summary>
        private HttpMessageCache cache;

        /// <summary>
        /// Backing field for the <see cref="PlaybackRuntimePath"/> property.
        /// </summary>
        private string playbackRuntimePath;

        /// <summary>
        /// Backing field for the <see cref="RecordingSourcePath"/> property.
        /// </summary>
        private string recordingSourcePath;

        /// <summary>
        /// Initializes a new instance of the <see cref="EchoMessageHandler"/> class
        /// that uses <see cref="HttpClientHandler"/> for outgoing network requests.
        /// </summary>
        public EchoMessageHandler()
            : this(new HttpClientHandler())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EchoMessageHandler"/> class.
        /// </summary>
        /// <param name="innerHandler">The message handler to use for outgoing network requests.</param>
        public EchoMessageHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            var settings = RuntimeSettings.Get();
            if (settings != null)
            {
                this.RecordingSourcePath = settings.RecordingSourcePath;
                this.PlaybackRuntimePath = settings.PlaybackRuntimePath;
            }
        }

        /// <summary>
        /// Gets or sets the specialized behaviors with HTTP caching.
        /// </summary>
        public EchoBehaviors Behaviors { get; set; }

        /// <summary>
        /// Gets or sets the path to write new recording files.
        /// </summary>
        public string RecordingSourcePath
        {
            get => this.recordingSourcePath;
            set
            {
                Requires.Argument(value == null || !string.IsNullOrWhiteSpace(value), nameof(value), "Must be null or non-empty.");
                this.recordingSourcePath = value;
            }
        }

        /// <summary>
        /// Gets or sets the relative path to previously recorded playback files.
        /// </summary>
        public string PlaybackRuntimePath
        {
            get => this.playbackRuntimePath;
            set
            {
                Requires.Argument(value == null || !string.IsNullOrWhiteSpace(value), nameof(value), "Must be null or non-empty.");
                if (this.playbackRuntimePath != value)
                {
                    this.playbackRuntimePath = value;
                    this.cache = null;
                }
            }
        }

        /// <summary>
        /// Gets the cache to use.
        /// </summary>
        internal HttpMessageCache Cache
        {
            get
            {
                HttpMessageCache cache = this.cache;
                if (cache == null)
                {
                    this.cache = cache = HttpMessageCache.Get(this.PlaybackRuntimePath);
                }

                return this.cache;
            }
        }

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var behaviors = this.Behaviors;
            HttpMessageCache cache = this.Cache;
            await cache.EnsureCachePopulatedAsync();

            if (!behaviors.HasFlag(EchoBehaviors.SkipCacheLookup) && cache.TryLookup(request, out HttpResponseMessage response))
            {
                Assumes.NotNull(response);
                return response;
            }

            if (behaviors.HasFlag(EchoBehaviors.DenyNetworkCalls))
            {
                throw new NoEchoCacheException();
            }

            response = await base.SendAsync(request, cancellationToken);

            if (!behaviors.HasFlag(EchoBehaviors.SkipRecordingResponses))
            {
                cache.AddOrUpdate(request, response);
                await cache.PersistCacheAsync(this.RecordingSourcePath);
            }

            return response;
        }
    }
}
