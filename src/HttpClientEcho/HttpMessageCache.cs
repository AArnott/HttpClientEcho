// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
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
        private const string CacheFileName = "HttpMessageCache.txt";

        private Dictionary<HttpRequestMessage, HttpResponseMessage> cacheDictionary;

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

            lock (this.cacheDictionary)
            {
                return this.cacheDictionary.TryGetValue(request, out response);
            }
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

            // Cache for later in this session.
            lock (this.cacheDictionary)
            {
                this.cacheDictionary[request] = response;
            }

            // Throw if the parent directory of UpdateLocation does not exist.
            Verify.Operation(Directory.GetParent(this.UpdateLocation).Exists, "Caching an HTTP response to \"{0}\" requires that its parent directory already exist. Is the source code for the test not on this machine?", this.UpdateLocation);

            ////Directory.CreateDirectory(this.UpdateLocation);
            ////string fileName = Path.Combine(this.UpdateLocation, CacheFileName);
            ////using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            ////{
            ////    // Update the serialized cache too.
            ////    await HttpMessageSerializer.SerializeAsync(request, fileStream);
            ////    await HttpMessageSerializer.SerializeAsync(response, fileStream);
            ////}
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

        private async Task<Dictionary<HttpRequestMessage, HttpResponseMessage>> ReadCacheAsync()
        {
            var result = new Dictionary<HttpRequestMessage, HttpResponseMessage>(new HttpRequestEqualityComparer());
            if (this.LookupLocation != null)
            {
                string fileName = Path.Combine(this.LookupLocation, CacheFileName);
                if (File.Exists(fileName))
                {
                    using (var cacheStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                    {
                        var request = await HttpMessageSerializer.DeserializeRequestAsync(cacheStream);
                        if (request != null)
                        {
                            var response = await HttpMessageSerializer.DeserializeResponseAsync(cacheStream);
                            result[request] = response;
                        }
                    }
                }
            }

            return result;
        }

        private class HttpRequestEqualityComparer : IEqualityComparer<HttpRequestMessage>
        {
            public bool Equals(HttpRequestMessage x, HttpRequestMessage y)
            {
                // TODO: make this a more precise match.
                return x.RequestUri.Equals(y.RequestUri);
            }

            public int GetHashCode(HttpRequestMessage value) => value.RequestUri?.GetHashCode() ?? 0;
        }
    }
}
