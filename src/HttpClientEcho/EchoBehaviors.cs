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
        /// Records HTTP responses in the cache.
        /// </summary>
        RecordResponses = 0x1,

        /// <summary>
        /// Allows HTTP requests to be responded to from the cache.
        /// </summary>
        AllowReplay = 0x2,

        /// <summary>
        /// Allow cache misses to be filled in with actual network calls.
        /// </summary>
        AllowNetworkCalls = 0x4,

        /// <summary>
        /// Skips the cache and makes actual network calls.
        /// When <see cref="RecordResponses"/> is also set, responses from the network will refresh any existing cached entries.
        /// </summary>
        SkipCacheLookup = AllowNetworkCalls | 0x8,

        /// <summary>
        /// Prefer cache. Fall back to network calls on cache misses and record responses.
        /// </summary>
        Default = RecordResponses | AllowReplay | AllowNetworkCalls,
    }
}
