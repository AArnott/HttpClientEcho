// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;

    /// <summary>
    /// Controls which behaviors are included in HTTP handling.
    /// </summary>
    [Flags]
    public enum EchoBehaviors
    {
        /// <summary>
        /// Allows cache lookup, actual network calls, and recording of responses.
        /// </summary>
        Default = 0x0,

        /// <summary>
        /// Treat every HTTP request like a cache miss.
        /// </summary>
        SkipCacheLookup = 0x1,

        /// <summary>
        /// When an uncached request is observed, throw rather than make a network call.
        /// </summary>
        DenyNetworkCalls = 0x2,

        /// <summary>
        /// Skips recording responses, even when <see cref="EchoMessageHandler.RecordingSourcePath"/> is set.
        /// </summary>
        SkipRecordingResponses = 0x4,
    }
}
