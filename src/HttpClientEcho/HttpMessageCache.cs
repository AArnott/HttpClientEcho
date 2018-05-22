// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Validation;

    // TODO: serialization thread-safety

    /// <summary>
    /// Manages HTTP message cache lookups, additions and updates.
    /// </summary>
    internal class HttpMessageCache
    {
        private const string CacheFileName = "HttpMessageCache.vcr";

        private const string GitAttributesContent = "###############################################################################\r\n# Do not normalize line endings for .vcr files\r\n###############################################################################\r\n*.vcr -text\r\n";

        /// <summary>
        /// The file header
        /// </summary>
        /// <remarks>
        /// This header deliberately mixes line endings so that we can detect and error out if
        /// line ending normalization has taken place, since that corrupts the entity and screws up its size
        /// so that it no longer agrees with Content-Length headers.
        /// </remarks>
        private static readonly byte[] FileHeader = Encoding.UTF8.GetBytes("HttpClientEcho cache file. DO NOT NORMALIZE LINE ENDINGS.\n\r\n");

        private ImmutableDictionary<HttpRequestMessage, HttpResponseMessage> cacheDictionary;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMessageCache"/> class.
        /// </summary>
        /// <param name="lookupLocation">The path to the directory to check for previously cached entries. May be null.</param>
        /// <param name="updateLocation">The path to the directory to write new or updated cache entries to. May be null.</param>
        internal HttpMessageCache(string lookupLocation, string updateLocation)
        {
            this.LookupLocation = lookupLocation;
            this.UpdateLocation = updateLocation;
        }

        /// <summary>
        /// Gets the path to the directory to check for previously cached entries. May be null.
        /// </summary>
        internal string LookupLocation { get; }

        /// <summary>
        /// Gets the path to the directory to write new or updated cache entries to. May be null.
        /// </summary>
        internal string UpdateLocation { get; }

        /// <summary>
        /// Looks up the response for a given request, if a cached one exists.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="response">Receives the response, if a cache hit is found.</param>
        /// <returns><c>true</c> if a cache hit is found; <c>false</c> otherwise.</returns>
        internal bool TryLookup(HttpRequestMessage request, out HttpResponseMessage response)
        {
            Requires.NotNull(request, nameof(request));
            Verify.Operation(this.cacheDictionary != null, "Await {0} first.", nameof(this.EnsureCachePopulatedAsync));

            return this.cacheDictionary.TryGetValue(request, out response);
        }

        /// <summary>
        /// Stores the response for a given request in the cache.
        /// </summary>
        /// <param name="request">The request that was made.</param>
        /// <param name="response">The response that was received.</param>
        /// <returns>A task tracking completion of the cache store operation.</returns>
        internal async Task StoreAsync(HttpRequestMessage request, HttpResponseMessage response)
        {
            Verify.Operation(this.UpdateLocation != null, "Cannot store a response when {0} is not set.", nameof(this.UpdateLocation));

            this.StoreInMemory(request, response);
            await this.PersistCacheAsync();
        }

        /// <summary>
        /// Reads the cache file into memory, if it has not been already.
        /// </summary>
        /// <returns>A task tracking the operation.</returns>
        internal async Task EnsureCachePopulatedAsync()
        {
            if (this.cacheDictionary == null)
            {
                var cacheDictionary = await this.ReadCacheAsync();
                Interlocked.CompareExchange(ref this.cacheDictionary, cacheDictionary, null);
            }
        }

        private static async Task VerifyFileHeaderAsync(FileStream cacheStream)
        {
            var actualFileHeader = new byte[FileHeader.Length];
            int bytesRead = 0;
            while (bytesRead < FileHeader.Length)
            {
                int bytesJustRead = await cacheStream.ReadAsync(actualFileHeader, bytesRead, FileHeader.Length - bytesRead);
                if (bytesJustRead == 0)
                {
                    throw new BadCacheFileException("Bad or missing file header.");
                }

                bytesRead += bytesJustRead;
            }

            for (int i = 0; i < FileHeader.Length; i++)
            {
                if (actualFileHeader[i] != FileHeader[i])
                {
                    throw new BadCacheFileException("Bad or missing file header.");
                }
            }
        }

        private async Task<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>> ReadCacheAsync()
        {
            var result = ImmutableDictionary.CreateBuilder<HttpRequestMessage, HttpResponseMessage>(HttpRequestEqualityComparer.Default);
            if (this.LookupLocation != null)
            {
                string fileName = Path.Combine(this.LookupLocation, CacheFileName);
                if (File.Exists(fileName))
                {
                    using (var cacheStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                    {
                        await VerifyFileHeaderAsync(cacheStream);
                        while (cacheStream.Position < cacheStream.Length)
                        {
                            var request = await HttpMessageSerializer.DeserializeRequestAsync(cacheStream);
                            if (request == null)
                            {
                                break;
                            }

                            var response = await HttpMessageSerializer.DeserializeResponseAsync(cacheStream);
                            result[request] = response;
                        }

                        if (result.Count == 0)
                        {
                            throw new BadCacheFileException("No cached responses found.");
                        }
                    }
                }
            }

            return result.ToImmutable();
        }

        private async Task PersistCacheAsync()
        {
            // We don't want to write files to the source directory of the test project if the test project isn't even there.
            Verify.Operation(Directory.GetParent(this.UpdateLocation).Exists, "Caching an HTTP response to \"{0}\" requires that its parent directory already exist. Is the source code for the test not on this machine?", this.UpdateLocation);

            Directory.CreateDirectory(this.UpdateLocation);
            string fileName = Path.Combine(this.UpdateLocation, CacheFileName);
            using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fileStream.WriteAsync(FileHeader, 0, FileHeader.Length);
                foreach (var entry in this.cacheDictionary)
                {
                    await HttpMessageSerializer.SerializeAsync(entry.Key, fileStream);
                    await HttpMessageSerializer.SerializeAsync(entry.Value, fileStream);
                }
            }

            // Protect against git normalizing line endings for this file.
            await this.WriteGitAttributesFileAsync();
        }

        private async Task WriteGitAttributesFileAsync()
        {
            Directory.CreateDirectory(this.UpdateLocation);
            string gitAttributesPath = Path.Combine(this.UpdateLocation, ".gitattributes");
            if (!File.Exists(gitAttributesPath))
            {
                using (var writer = new StreamWriter(new FileStream(gitAttributesPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true)))
                {
                    await writer.WriteAsync(GitAttributesContent);
                    await writer.FlushAsync();
                }
            }
        }

        private void StoreInMemory(HttpRequestMessage request, HttpResponseMessage response)
        {
            ImmutableInterlocked.AddOrUpdate(ref this.cacheDictionary, request, response, (k, v) => response);
        }

        private class HttpRequestEqualityComparer : IEqualityComparer<HttpRequestMessage>
        {
            private HttpRequestEqualityComparer()
            {
            }

            internal static HttpRequestEqualityComparer Default { get; } = new HttpRequestEqualityComparer();

            public bool Equals(HttpRequestMessage x, HttpRequestMessage y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null ^ y == null)
                {
                    return false;
                }

                bool match = x.RequestUri.Equals(y.RequestUri);
                match &= x.Method == y.Method;
                match &= HttpHeadersEqualityComparer.Default.Equals(x.Headers, y.Headers);
                match &= HttpHeadersEqualityComparer.Default.Equals(x.Content?.Headers, y.Content?.Headers);

                return match;
            }

            public int GetHashCode(HttpRequestMessage value) => value.RequestUri?.GetHashCode() ?? 0;
        }

        private class HttpHeadersEqualityComparer : IEqualityComparer<HttpHeaders>
        {
            private HttpHeadersEqualityComparer()
            {
            }

            internal static HttpHeadersEqualityComparer Default { get; } = new HttpHeadersEqualityComparer();

            public bool Equals(HttpHeaders x, HttpHeaders y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null ^ y == null)
                {
                    return false;
                }

                var xHeaders = x.ToDictionary(kv => kv.Key, kv => kv.Value);
                var yHeaders = y.ToDictionary(kv => kv.Key, kv => kv.Value);

                bool match = xHeaders.Count == yHeaders.Count;
                foreach (var xHeader in x)
                {
                    if (!y.TryGetValues(xHeader.Key, out IEnumerable<string> yValue))
                    {
                        return false;
                    }

                    if (!Enumerable.SequenceEqual(xHeader.Value, yValue))
                    {
                        return false;
                    }
                }

                return match;
            }

            public int GetHashCode(HttpHeaders obj) => obj.Count();
        }
    }
}
