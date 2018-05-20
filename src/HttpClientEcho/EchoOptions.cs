// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using Validation;

    /// <summary>
    /// Options that influence how the recording and playing of HTTP messages work.
    /// </summary>
    public class EchoOptions
    {
        /// <summary>
        /// Backing field for <see cref="InnerHandler"/>
        /// </summary>
        private HttpMessageHandler innerHandler;

        /// <summary>
        /// Gets or sets the inner <see cref="HttpMessageHandler"/> to use when networking calls go to actual HTTP servers.
        /// </summary>
        /// <value>An instance of <see cref="HttpMessageHandler"/>. This will never be null.</value>
        /// <remarks>
        /// The default of <see cref="HttpClientHandler"/> will be used if not specified.
        /// </remarks>
        public HttpMessageHandler InnerHandler
        {
            get => this.innerHandler;
            set => this.innerHandler = Requires.NotNull(value, nameof(value));
        }
    }
}
