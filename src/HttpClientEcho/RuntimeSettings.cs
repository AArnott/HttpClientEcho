// Copyright (c) Andrew Arnott. All rights reserved.

namespace HttpClientEcho
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    /// <summary>
    /// Exposes runtime settings that are configured at the time the test project is built.
    /// </summary>
    internal class RuntimeSettings
    {
        private const string SettingsFileName = "HttpClientEchoSettings.json";

        /// <summary>
        /// Gets or sets the path to write new recording files.
        /// </summary>
        public string RecordingSourcePath { get; set; }

        /// <summary>
        /// Gets or sets the relative path to previously recorded playback files.
        /// </summary>
        public string PlaybackRuntimePath { get; set; }

        /// <summary>
        /// Gets the runtime settings for this library, if they can be found.
        /// </summary>
        /// <returns>An instance of <see cref="RuntimeSettings"/> if the settings file could be found; otherwise <c>null</c>.</returns>
        internal static RuntimeSettings Get()
        {
            if (File.Exists(SettingsFileName))
            {
                using (var fileReader = new StreamReader(File.Open(SettingsFileName, FileMode.Open, FileAccess.Read, FileShare.Read)))
                {
                    JsonSerializer serializer = JsonSerializer.Create();
                    serializer.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    var settings = (RuntimeSettings)serializer.Deserialize(new JsonTextReader(fileReader), typeof(RuntimeSettings));
                    return settings;
                }
            }
            else
            {
                return null;
            }
        }
    }
}
