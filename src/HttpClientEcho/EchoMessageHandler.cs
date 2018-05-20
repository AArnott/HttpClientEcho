// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using Validation;

    /// <summary>
    /// A factory for an <see cref="HttpMessageHandler"/> that records and replays HTTP traffic.
    /// </summary>
    public static class EchoMessageHandler
    {
        /// <summary>
        /// Creates an <see cref="HttpMessageHandler"/> that records or replays HTTP messages.
        /// </summary>
        /// <param name="behaviors">Specialize behaviors with HTTP caching.</param>
        /// <param name="innerHandler">
        /// The <see cref="HttpMessageHandler"/> to use when performing real network calls.
        /// The default of <see cref="HttpClientHandler"/> will be used if not specified.
        /// </param>
        /// <returns>The <see cref="HttpMessageHandler"/>.</returns>
        public static HttpMessageHandler Create(EchoBehaviors behaviors = EchoBehaviors.Default, HttpMessageHandler innerHandler = null)
        {
            Requires.Argument(behaviors.HasFlag(EchoBehaviors.AllowReplay) || behaviors.HasFlag(EchoBehaviors.AllowNetworkCalls), nameof(behaviors), "Either {0} or {1} must be specified.", nameof(EchoBehaviors.AllowNetworkCalls), nameof(EchoBehaviors.AllowReplay));

            return new RecordingMessageHandler(innerHandler ?? new HttpClientHandler());
        }
    }
}
