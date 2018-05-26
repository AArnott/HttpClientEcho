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

    /// <summary>
    /// Manages HTTP message cache lookups, additions and updates.
    /// </summary>
    public class HttpMessageCache
    {
        private const string DefaultCacheFileName = "HttpMessageCache.vcr";

        private const string GitAttributesContent = "###############################################################################\r\n# Do not normalize line endings for .vcr files\r\n###############################################################################\r\n*.vcr -text\r\n";

        private static readonly byte[] SpaceBetweenResponseAndRequest = Encoding.UTF8.GetBytes("\n\n");

        /// <summary>
        /// The file header
        /// </summary>
        /// <remarks>
        /// This header deliberately mixes line endings so that we can detect and error out if
        /// line ending normalization has taken place, since that corrupts the entity and screws up its size
        /// so that it no longer agrees with Content-Length headers.
        /// </remarks>
        private static readonly byte[] FileHeader = Encoding.UTF8.GetBytes("HttpClientEcho cache file. DO NOT NORMALIZE LINE ENDINGS.\n\r\n");

        /// <summary>
        /// A means to share instances of the cache based on the path to the file that is loaded to populate the cache.
        /// </summary>
        private static ImmutableDictionary<string, HttpMessageCache> cacheByLookupPath = ImmutableDictionary.Create<string, HttpMessageCache>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The path to the file to check for previously cached entries. May be null.
        /// </summary>
        private readonly string cacheFilePath;

        /// <summary>
        /// A semaphore that must be entered to save the cache.
        /// </summary>
        private readonly SemaphoreSlim savingCacheSemaphore = new SemaphoreSlim(1);

        /// <summary>
        /// A cache of HTTP requests to their responses. Initialized by <see cref="EnsureCachePopulatedAsync"/>.
        /// </summary>
        private Task<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>> cacheDictionary;

        /// <summary>
        /// Tracks whether there is already an unstarted save operation waiting for the semaphore. 0 if not, 1 if so.
        /// </summary>
        private int saveQueued;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpMessageCache"/> class.
        /// </summary>
        /// <param name="cacheFilePath">The path to the file to read for previously cached entries. May be null.</param>
        private HttpMessageCache(string cacheFilePath)
        {
            this.cacheFilePath = cacheFilePath;
        }

        /// <summary>
        /// Gets an instance of <see cref="HttpMessageCache"/> to represent the lookup path specified.
        /// </summary>
        /// <param name="lookupLocation">The path to the directory to store cache files.</param>
        /// <returns>An instance of <see cref="HttpMessageCache"/>.</returns>
        public static HttpMessageCache Get(string lookupLocation)
        {
            if (lookupLocation == null)
            {
                // No sharing. Just create an isolated, in-memory cache.
                return new HttpMessageCache(null);
            }

            string cacheFileName = Path.Combine(lookupLocation, DefaultCacheFileName);
            return cacheByLookupPath.TryGetValue(cacheFileName, out HttpMessageCache result)
                ? result
                : ImmutableInterlocked.GetOrAdd(ref cacheByLookupPath, cacheFileName, new HttpMessageCache(cacheFileName));
        }

        /// <summary>
        /// Clears the memory cache. Any cache file on disk will be reread on next use.
        /// </summary>
        public void Reset()
        {
            this.cacheDictionary = null;
        }

        /// <summary>
        /// Looks up the response for a given request, if a cached one exists.
        /// </summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="response">Receives the response, if a cache hit is found.</param>
        /// <returns><c>true</c> if a cache hit is found; <c>false</c> otherwise.</returns>
        internal bool TryLookup(HttpRequestMessage request, out HttpResponseMessage response)
        {
            Requires.NotNull(request, nameof(request));

            var cacheDictionary = this.GetPreloadedCacheOrThrow();
            return cacheDictionary.Result.TryGetValue(request, out response);
        }

        /// <summary>
        /// Stores the response for a given request in the cache.
        /// </summary>
        /// <param name="request">The request that was made.</param>
        /// <param name="response">The response that was received.</param>
        internal void AddOrUpdate(HttpRequestMessage request, HttpResponseMessage response)
        {
            Requires.NotNull(request, nameof(request));
            Requires.NotNull(response, nameof(response));

            bool lostRace;
            do
            {
                var cacheDictionary = this.GetPreloadedCacheOrThrow();
                lostRace = Interlocked.CompareExchange(ref this.cacheDictionary, Task.FromResult(cacheDictionary.Result.SetItem(request, response)), cacheDictionary) != cacheDictionary;
            }
            while (lostRace);
        }

        /// <summary>
        /// Serializes the cache out to a file in the specified directory.
        /// </summary>
        /// <param name="updateLocation">The path to the directory to write new or updated cache entries to. May be null.</param>
        /// <returns>A task that tracks completion of the operation.</returns>
        internal async Task PersistCacheAsync(string updateLocation)
        {
            Requires.NotNullOrEmpty(updateLocation, nameof(updateLocation));
            Verify.Operation(this.cacheFilePath != null, "This instance cannot be persisted because it was not created with a path.");

            // We don't want to write files to the source directory of the test project if the test project isn't even there.
            Verify.Operation(Directory.GetParent(updateLocation).Exists, "Caching an HTTP response to \"{0}\" requires that its parent directory already exist. Is the source code for the test not on this machine?", updateLocation);

            // Get in line to write to the cache so we avoid file conflicts.
            // We don't need to queue up a long line of redundant file saves,
            // so only queue one if no one is already waiting to start saving.
            if (Interlocked.Exchange(ref this.saveQueued, 1) == 0)
            {
                await this.savingCacheSemaphore.WaitAsync();
                try
                {
                    Directory.CreateDirectory(updateLocation);
                    string filePathToUpdate = Path.Combine(updateLocation, Path.GetFileName(this.cacheFilePath));

                    using (var fileStream = new FileStream(filePathToUpdate, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                    {
                        await fileStream.WriteAsync(FileHeader, 0, FileHeader.Length);

                        // Snap the memory cache to save. But first, clear the saveQueued field so if anyone wants to persist changes made after this, they will queue themselves.
                        Volatile.Write(ref this.saveQueued, 0);
#if !NETSTANDARD1_3
                        Thread.MemoryBarrier();
#endif
                        var cacheDictionary = await Volatile.Read(ref this.cacheDictionary);

                        foreach (var entry in cacheDictionary)
                        {
                            await HttpMessageSerializer.SerializeAsync(entry.Key, fileStream);
                            await HttpMessageSerializer.SerializeAsync(entry.Value, fileStream);

                            await fileStream.WriteAsync(SpaceBetweenResponseAndRequest, 0, SpaceBetweenResponseAndRequest.Length);
                        }
                    }

                    // Protect against git normalizing line endings for this file.
                    await this.WriteGitAttributesFileAsync(updateLocation);
                }
                finally
                {
                    this.savingCacheSemaphore.Release();
                }
            }
        }

        /// <summary>
        /// Reads the cache file into memory, if it has not been already.
        /// </summary>
        /// <returns>A task tracking the operation.</returns>
        internal async Task<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>> EnsureCachePopulatedAsync()
        {
            if (this.cacheDictionary == null)
            {
                var loadingCacheSource = new TaskCompletionSource<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>>();
                if (Interlocked.CompareExchange(ref this.cacheDictionary, loadingCacheSource.Task, null) == null)
                {
                    try
                    {
                        if (this.cacheFilePath != null && File.Exists(this.cacheFilePath))
                        {
                            loadingCacheSource.TrySetResult(await ReadCacheAsync(this.cacheFilePath));
                        }
                        else
                        {
                            loadingCacheSource.TrySetResult(ImmutableDictionary.Create<HttpRequestMessage, HttpResponseMessage>(HttpRequestEqualityComparer.Default));
                        }
                    }
                    catch (Exception ex)
                    {
                        loadingCacheSource.TrySetException(ex);
                    }
                }
            }

            return await this.cacheDictionary;
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

        private static async Task<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>> ReadCacheAsync(string fileName)
        {
            var result = ImmutableDictionary.CreateBuilder<HttpRequestMessage, HttpResponseMessage>(HttpRequestEqualityComparer.Default);
            DateTime lastWriteTimeUtc;
            using (var cacheStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(fileName);
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

                    // Allow for extra line endings after the response for readability.
                    SkipBlankLines(cacheStream);
                }

                if (result.Count == 0)
                {
                    throw new BadCacheFileException("No cached responses found.");
                }
            }

            return result.ToImmutable();
        }

        private static void SkipBlankLines(FileStream cacheStream)
        {
            Requires.NotNull(cacheStream, nameof(cacheStream));

            char ch;
            do
            {
                int b = cacheStream.ReadByte();
                if (b == -1)
                {
                    // end of file
                    return;
                }

                ch = (char)b;
            }
            while (ch == '\n' || ch == '\r');

            // Go back one byte since we found a non-whitespace character;
            cacheStream.Position -= 1;
        }

        private async Task WriteGitAttributesFileAsync(string updateLocation)
        {
            Requires.NotNullOrEmpty(updateLocation, nameof(updateLocation));

            Directory.CreateDirectory(updateLocation);
            string gitAttributesPath = Path.Combine(updateLocation, ".gitattributes");
            if (!File.Exists(gitAttributesPath))
            {
                using (var writer = new StreamWriter(new FileStream(gitAttributesPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true)))
                {
                    await writer.WriteAsync(GitAttributesContent);
                    await writer.FlushAsync();
                }
            }
        }

#pragma warning disable UseAsyncSuffix // Use Async suffix
        private Task<ImmutableDictionary<HttpRequestMessage, HttpResponseMessage>> GetPreloadedCacheOrThrow()
#pragma warning restore UseAsyncSuffix // Use Async suffix
        {
            var cacheDictionary = Volatile.Read(ref this.cacheDictionary);
            Verify.Operation(cacheDictionary?.Status == TaskStatus.RanToCompletion, "Await {0} first.", nameof(this.EnsureCachePopulatedAsync));
            return cacheDictionary;
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
