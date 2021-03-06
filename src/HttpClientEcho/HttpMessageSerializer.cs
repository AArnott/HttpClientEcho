﻿// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading.Tasks;
    using Validation;

    /// <summary>
    /// Serializes and deserializes <see cref="HttpRequestMessage"/> and <see cref="HttpResponseMessage"/>.
    /// </summary>
    internal static class HttpMessageSerializer
    {
        private const int BufferSize = 1;
        private static readonly char[] SpaceSeparator = new[] { ' ' };
        private static readonly char[] ColonSeparator = new[] { ':' };
        private static readonly Encoding HTTPHeaderEncoding = Encoding.ASCII; // RFC 2616

        /// <summary>
        /// Serializes <see cref="HttpRequestMessage"/> objects.
        /// </summary>
        /// <param name="message">The request to serialize</param>
        /// <param name="outputStream">The stream to write the serialized form to.</param>
        /// <returns>A task that tracks completion.</returns>
        internal static async Task SerializeAsync(HttpRequestMessage message, Stream outputStream)
        {
            using (var writer = GetWriter(outputStream))
            {
                await writer.WriteLineAsync($"{message.Method.Method} {message.RequestUri.AbsoluteUri}");
                await WriteHeadersAsync(message.Headers, writer);
                await WriteHeadersAsync(message.Content?.Headers, writer);
                await writer.WriteLineAsync();
                await writer.FlushAsync();

                message.Content = await CopyContentAsync(message.Content, outputStream);
            }
        }

        /// <summary>
        /// Deserializes an <see cref="HttpRequestMessage"/> from a stream.
        /// </summary>
        /// <param name="inputStream">The stream to deserialize from.</param>
        /// <returns>The deserialized value, or <c>null</c> if we reached the end of the stream.</returns>
        internal static async Task<HttpRequestMessage> DeserializeRequestAsync(Stream inputStream)
        {
            Requires.NotNull(inputStream, nameof(inputStream));

            string line = await ReadLineAsync(inputStream);
            if (line == null)
            {
                // End of stream.
                return null;
            }

            HttpRequestMessage request;
            string[] verbAndUrl = line.Split(SpaceSeparator);
            ThrowBadCacheFileIf(verbAndUrl.Length != 2, "Expected HTTP verb and URL");
            try
            {
                request = new HttpRequestMessage(new HttpMethod(verbAndUrl[0]), verbAndUrl[1]);
            }
            catch (UriFormatException ex)
            {
                throw new BadCacheFileException("Failed to parse URL in request.", ex);
            }

            var contentHeaders = await ReadHeadersAsync(inputStream, request.Headers);
            request.Content = await ReadContentAsync(inputStream, contentHeaders);

            return request;
        }

        /// <summary>
        /// Serializes an <see cref="HttpResponseMessage"/> to a stream.
        /// </summary>
        /// <param name="message">The request to serialize</param>
        /// <param name="outputStream">The stream to write the serialized form to.</param>
        /// <returns>A task that tracks completion.</returns>
        internal static async Task SerializeAsync(HttpResponseMessage message, Stream outputStream)
        {
            using (var writer = GetWriter(outputStream))
            {
                await writer.WriteLineAsync($"{(int)message.StatusCode} {message.StatusCode}");
                await WriteHeadersAsync(message.Headers, writer);
                message.Content = await EnsureContentHasLengthAsync(message.Content);
                await WriteHeadersAsync(message.Content?.Headers, writer);
                await writer.WriteLineAsync();
                await writer.FlushAsync();
                message.Content = await CopyContentAsync(message.Content, outputStream);
            }
        }

        /// <summary>
        /// Deserializes an <see cref="HttpResponseMessage"/> from a stream.
        /// </summary>
        /// <param name="inputStream">The stream to deserialize from.</param>
        /// <returns>The deserialized value, or <c>null</c> if we reached the end of the stream.</returns>
        internal static async Task<HttpResponseMessage> DeserializeResponseAsync(Stream inputStream)
        {
            var response = new HttpResponseMessage();

            string line = await ReadLineAsync(inputStream);
            ThrowUnexpectedEndOfStream(line == null);
            int indexOfSpace = line.IndexOf(' ');
            ThrowBadCacheFileIf(indexOfSpace < 0, $"Missing space after \"{0}\".", line);
            string statusCodeAsString = line.Substring(0, indexOfSpace);
            try
            {
                response.StatusCode = (HttpStatusCode)int.Parse(statusCodeAsString, CultureInfo.InvariantCulture);
            }
            catch (FormatException ex)
            {
                throw new BadCacheFileException($"Failed to parse \"{statusCodeAsString}\".", ex);
            }

            var contentHeaders = await ReadHeadersAsync(inputStream, response.Headers);
            response.Content = await ReadContentAsync(inputStream, contentHeaders);

            return response;
        }

        private static async Task<HttpContent> EnsureContentHasLengthAsync(HttpContent content)
        {
            if (content != null && !content.Headers.ContentLength.HasValue)
            {
                // Copy the content, which sets the content length.
                content = await CopyContentAsync(content, Stream.Null);
                Assumes.True(content.Headers.ContentLength.HasValue);
            }

            return content;
        }

        private static async Task<HttpContent> ReadContentAsync(Stream inputStream, IReadOnlyDictionary<string, IEnumerable<string>> contentHeaders)
        {
            Requires.NotNull(inputStream, nameof(inputStream));
            Requires.NotNull(contentHeaders, nameof(contentHeaders));

            if (TryGetContentLength(contentHeaders, out int length))
            {
                var contentStream = new MemoryStream(length);
                await CopyToAsync(inputStream, contentStream, length);
                contentStream.Position = 0;
                var content = new StreamContent(contentStream);
                CopyHeaders(contentHeaders, content.Headers);
                return content;
            }

            return null;
        }

        private static bool TryGetContentLength(IReadOnlyDictionary<string, IEnumerable<string>> contentHeaders, out int contentLength)
        {
            if (contentHeaders.TryGetValue("Content-Length", out IEnumerable<string> lengthString))
            {
                try
                {
                    contentLength = int.Parse(lengthString.Single(), CultureInfo.InvariantCulture);
                    return true;
                }
                catch (FormatException ex)
                {
                    throw new BadCacheFileException($"Failed to parse \"{lengthString.Single()}\".", ex);
                }
            }

            contentLength = 0;
            return false;
        }

        private static StreamWriter GetWriter(Stream outputStream)
        {
            return new StreamWriter(outputStream, HTTPHeaderEncoding, BufferSize, leaveOpen: true)
            {
                NewLine = "\r\n", // RFC 2616
            };
        }

        /// <summary>
        /// Reads a line from an HTTP header.
        /// </summary>
        /// <param name="inputStream">The stream to read from.</param>
        /// <returns>The line read, excluding line endings; or <c>null</c> if the end of stream is reached.</returns>
        private static async Task<string> ReadLineAsync(Stream inputStream)
        {
            var sb = new StringBuilder(40);
            var decoder = HTTPHeaderEncoding.GetDecoder();
            var buffer = new byte[1];
            var chars = new char[1];
            while (true)
            {
                int bytesRead = await inputStream.ReadAsync(buffer, 0, 1);
                if (bytesRead == 0)
                {
                    return sb.Length == 0 ? null : sb.ToString();
                }

                decoder.Convert(buffer, 0, 1, chars, 0, 1, false, out int _, out int charsUsed, out bool completed);
                Assumes.True(charsUsed > 0 == completed);
                sb.Append(chars, 0, 1);

                // Support for \r\n line endings for Windows and because that's what we serialized in the first place.
                if (sb.Length >= 2 && sb[sb.Length - 2] == '\r' && sb[sb.Length - 1] == '\n')
                {
                    sb.Length -= 2;
                    break;
                }

                // Support for \n line endings because a git controlled repo may be normalizing line endings on *nix platforms.
                if (sb[sb.Length - 1] == '\n')
                {
                    sb.Length -= 1;
                    break;
                }
            }

            return sb.ToString();
        }

        private static async Task WriteHeadersAsync(HttpHeaders headers, TextWriter writer)
        {
            if (headers != null)
            {
                bool contentLengthWritten = false;
                foreach (var header in headers)
                {
                    await writer.WriteLineAsync($"{header.Key}: {string.Join(",", header.Value)}");
                    contentLengthWritten |= string.Equals(header.Key, "Content-Length", StringComparison.Ordinal);
                }

                // Defend against apparent bug in mono when run on OSX and Linux, where even though we set the ContentLength
                // header, it doesn't always enumerate with the rest of the headers.
                if (!contentLengthWritten && headers is HttpContentHeaders contentHeaders && contentHeaders.ContentLength.HasValue)
                {
                    await writer.WriteLineAsync($"Content-Length: {contentHeaders.ContentLength.Value.ToString(CultureInfo.InvariantCulture)}");
                }
            }
        }

        private static async Task<IReadOnlyDictionary<string, IEnumerable<string>>> ReadHeadersAsync(Stream inputStream, HttpHeaders headers)
        {
            Requires.NotNull(inputStream, nameof(inputStream));
            Requires.NotNull(headers, nameof(headers));

            var result = new Dictionary<string, IEnumerable<string>>();
            string line;
            while ((line = await ReadLineAsync(inputStream))?.Length > 0)
            {
                string[] headerSplit = line.Split(ColonSeparator, 2);
                if (headerSplit.Length != 2)
                {
                    throw new BadCacheFileException("Missing colon separator in header.");
                }

                string headerValue = headerSplit[1].Trim(); // Per RFC, whitespace around header value is ignored.

                if (!headers.TryAddWithoutValidation(headerSplit[0], headerValue))
                {
                    result.Add(headerSplit[0], new[] { headerValue });
                }
            }

            ThrowUnexpectedEndOfStream(line == null);

            return result;
        }

        private static void CopyHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> input, HttpHeaders output)
        {
            foreach (var header in input)
            {
                output.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        private static async Task<HttpContent> CopyContentAsync(HttpContent content, Stream copyTo)
        {
            if (content != null)
            {
                var buffer = await content.ReadAsByteArrayAsync();
                await copyTo.WriteAsync(buffer, 0, buffer.Length);
                var result = new ByteArrayContent(buffer);
                CopyHeaders(content.Headers, result.Headers);
                result.Headers.ContentLength = buffer.Length;
                return result;
            }

            return null;
        }

        private static async Task CopyToAsync(Stream input, Stream output, int bytes)
        {
            Requires.NotNull(input, nameof(input));
            Requires.NotNull(output, nameof(output));
            Requires.Range(bytes >= 0, nameof(bytes));

            byte[] buffer = new byte[bytes];
            while (bytes > 0)
            {
                int bytesRead = await input.ReadAsync(buffer, 0, bytes);
                ThrowUnexpectedEndOfStream(bytesRead == 0);

                await output.WriteAsync(buffer, 0, bytesRead);
                bytes -= bytesRead;
            }
        }

        private static void ThrowBadCacheFileIf(bool condition, string unformattedMessage, params object[] args)
        {
            if (condition)
            {
                throw new BadCacheFileException(string.Format(CultureInfo.CurrentCulture, unformattedMessage, args));
            }
        }

        private static void ThrowUnexpectedEndOfStream(bool condition = true)
        {
            if (condition)
            {
                throw new BadCacheFileException("Unexpected end of stream.");
            }
        }
    }
}
