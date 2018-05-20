// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Manages HTTP message cache lookups, additions and updates.
    /// </summary>
    internal class HttpMessageCache
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMessageCache"/> class.
        /// </summary>
        /// <param name="lookupLocation">The path to the directory to check for previously cached entries.</param>
        /// <param name="updateLocation">The path to the directory to write new or updated cache entries to.</param>
        internal HttpMessageCache(string lookupLocation, string updateLocation)
        {
            this.LookupLocation = lookupLocation;
            this.UpdateLocation = updateLocation;
        }

        /// <summary>
        /// Gets the path to the directory to check for previously cached entries.
        /// </summary>
        internal string LookupLocation { get; }

        /// <summary>
        /// Gets the path to the directory to write new or updated cache entries to.
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
            // Cache miss.
            response = null;
            return false;
        }

        /// <summary>
        /// Stores the response for a given request in the cache.
        /// </summary>
        /// <param name="request">The request that was made.</param>
        /// <param name="response">The response that was received.</param>
        /// <returns>A task tracking completion of the cache store operation.</returns>
        internal async Task StoreAsync(HttpRequestMessage request, HttpResponseMessage response)
        {
            // TODO: throw if the parent directory of UpdateLocation does not exist.
            Directory.CreateDirectory(this.UpdateLocation);

            string fileName = this.GetPathToRecordingRequest(request);
            using (var fileStream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await HttpMessageSerializer.SerializeAsync(request, fileStream);
            }
        }

        private string GetFileNameForRecordedRequest(HttpRequestMessage request) => $"{request.Method.Method} {request.RequestUri.Host}.txt";

        private string GetPathToRecordingRequest(HttpRequestMessage request) => Path.Combine(this.UpdateLocation, this.GetFileNameForRecordedRequest(request));

        private string GetPathToCachedRequest(HttpRequestMessage request) => Path.Combine(this.LookupLocation, this.GetFileNameForRecordedRequest(request));
    }
}
