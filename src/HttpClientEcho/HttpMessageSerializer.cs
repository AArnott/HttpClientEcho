// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Serializes and deserializes <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>.
    /// </summary>
    internal static class HttpMessageSerializer
    {
        /// <summary>
        /// Serializes <see cref="HttpRequestMessage"/> objects.
        /// </summary>
        /// <param name="message">The request to serialize</param>
        /// <param name="outputStream">The stream to write the serialized form to.</param>
        /// <returns>A task that tracks completion.</returns>
        internal static async Task SerializeAsync(HttpRequestMessage message, Stream outputStream)
        {
            var writer = new StreamWriter(outputStream);
            await writer.WriteLineAsync($"{message.Method.Method} {message.RequestUri.PathAndQuery}");
            await writer.WriteLineAsync($"Host: {message.RequestUri.Host}");
            await WriteHeadersAsync(message.Headers, writer);
            await WriteHeadersAsync(message.Content?.Headers, writer);
            await writer.WriteLineAsync();
            await writer.FlushAsync();

            if (message.Content != null)
            {
                await message.Content.CopyToAsync(outputStream);
            }
        }

        private static async Task WriteHeadersAsync(HttpHeaders headers, TextWriter writer)
        {
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(",", header.Value)}");
                }
            }
        }
    }
}
