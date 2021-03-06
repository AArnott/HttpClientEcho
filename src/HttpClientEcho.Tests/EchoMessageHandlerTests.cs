﻿// Copyright (c) Andrew Arnott. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpClientEcho;
using Validation;
using Xunit;
using Xunit.Abstractions;

public class EchoMessageHandlerTests : IDisposable
{
    private const string PublicTestSite = "https://www.bing.com/";

    private const string MockContentString = "Mock data";

    /// <summary>
    /// The name used for the cache. Keep in sync with HttpMessageCache.CacheFileName
    /// </summary>
    private const string CacheFileName = "HttpMessageCache.vcr";

    private readonly MockInnerHandler mockHandler;

    private readonly string tempDir;

    private readonly ITestOutputHelper logger;

    private HttpClient httpClient;

    private EchoMessageHandler echoMessageHandler;

    public EchoMessageHandlerTests(ITestOutputHelper logger)
    {
        // Pick a directory to isolate this test's input/output.
        // Precreate it, so that we exercise the library's willingness to create just one directory deeper.
        this.tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this.tempDir);

        this.mockHandler = new MockInnerHandler();
        this.StartNewSession();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Dispose()
    {
        Directory.Delete(this.tempDir, recursive: true);
    }

    [Fact]
    public void PlaybackRuntimePath()
    {
        var echoMessageHandler = new EchoMessageHandler();
        Assert.Equal("HttpClientEcho" + Path.DirectorySeparatorChar, echoMessageHandler.PlaybackRuntimePath);
    }

    [Fact]
    public void RecordingSourcePath()
    {
        var echoMessageHandler = new EchoMessageHandler();
        string expected = Path.GetFullPath(Path.Combine(GetOutputDirectory(), "..", "..", "..", "..", "src", "HttpClientEcho.Tests", "HttpClientEcho" + Path.DirectorySeparatorChar));
        Assert.Equal(expected, echoMessageHandler.RecordingSourcePath);
    }

    [Fact]
    public async Task SkipCacheLookupForwardsToInnerHandler()
    {
        this.echoMessageHandler.Behaviors = EchoBehaviors.SkipCacheLookup;
        var response = await this.httpClient.GetAsync(PublicTestSite);
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, this.mockHandler.TrafficCounter);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StoreCacheThenReuseTwice(bool restartSession)
    {
        var response = await this.httpClient.GetAsync(PublicTestSite);
        Assert.Equal(1, this.mockHandler.TrafficCounter);
        response.EnsureSuccessStatusCode();
        string actual = await response.Content.ReadAsStringAsync();
        Assert.Equal(MockContentString, actual);

        // From here on out, the cache should be hit.
        if (restartSession)
        {
            this.StartNewSession();
        }

        this.mockHandler.ThrowIfCalled = true;

        response = await this.httpClient.GetAsync(PublicTestSite);
        response.EnsureSuccessStatusCode();
        actual = await response.Content.ReadAsStringAsync();
        Assert.Equal(MockContentString, actual);

        response = await this.httpClient.GetAsync(PublicTestSite);
        response.EnsureSuccessStatusCode();
        actual = await response.Content.ReadAsStringAsync();
        Assert.Equal(MockContentString, actual);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CacheHitsConsiderHeaders(bool restartSession)
    {
        // Start with some unique calls
        for (int i = 1; i <= 2; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, PublicTestSite);
            request.Headers.Add("Custom", i.ToString(CultureInfo.InvariantCulture));
            var response = await this.httpClient.SendAsync(request);
            Assert.Equal(i, this.mockHandler.TrafficCounter);
        }

        // Now repeat those calls, which should not hit the network any more.
        if (restartSession)
        {
            this.StartNewSession();
        }

        this.mockHandler.ThrowIfCalled = true;
        for (int i = 1; i <= 2; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, PublicTestSite);
            request.Headers.Add("Custom", i.ToString(CultureInfo.InvariantCulture));
            var response = await this.httpClient.SendAsync(request);
        }
    }

    [Fact]
    public void DefaultCtorUsesRealNetwork()
    {
        var handler = new EchoMessageHandler();
        Assert.IsType<HttpClientHandler>(handler.InnerHandler);
    }

    [Fact]
    public void InnerHandlerIsSet()
    {
        var handler = new EchoMessageHandler(this.mockHandler);
        Assert.Same(this.mockHandler, handler.InnerHandler);
    }

    [Fact]
    public async Task NoReplayNoNetworkThrows()
    {
        this.echoMessageHandler.Behaviors = EchoBehaviors.DenyNetworkCalls | EchoBehaviors.SkipCacheLookup;
        await Assert.ThrowsAsync<NoEchoCacheException>(() => this.httpClient.GetAsync(PublicTestSite));
    }

    [Fact]
    public async Task CacheMissFailsWhenNetworkDenied()
    {
        this.echoMessageHandler.PlaybackRuntimePath = null; // ensure a cache miss.
        this.echoMessageHandler.Behaviors = EchoBehaviors.DenyNetworkCalls;
        await Assert.ThrowsAsync<NoEchoCacheException>(() => this.httpClient.GetAsync(PublicTestSite));
    }

    [Fact]
    public async Task StoreCacheThrowsIfSrcDirectoryParentDoesNotExist()
    {
        // It's permissible to create the directory itself, but only if its parent already exists.
        // This makes for a reasonable auto-update story when running on a dev box, but makes it unlikely
        // that we would try creating that same source directory on a test-only machine for which updating sources is pointless.
        this.echoMessageHandler.RecordingSourcePath = Path.Combine(this.echoMessageHandler.RecordingSourcePath, "sub-path");
        await Assert.ThrowsAsync<InvalidOperationException>(() => this.httpClient.GetAsync(PublicTestSite));
    }

    [Fact]
    public async Task StoreCacheAllowsTrailingSlashOnRecordingPath()
    {
        this.echoMessageHandler.RecordingSourcePath = this.echoMessageHandler.RecordingSourcePath + Path.DirectorySeparatorChar;
        await this.httpClient.GetAsync(PublicTestSite);
    }

    [Fact]
    public async Task TruncatedFile()
    {
        string playbackFile = await this.UseTestReplayFileAsync();

        // Start by validating that the full length file is valid.
        await this.httpClient.GetAsync(PublicTestSite);

        int originalFileLength = (int)new FileInfo(playbackFile).Length;
        for (int truncatedLength = originalFileLength - 1; truncatedLength >= 0; truncatedLength--)
        {
            using (var playbackFileStream = File.Open(playbackFile, FileMode.Open))
            {
                playbackFileStream.SetLength(truncatedLength);
            }

            try
            {
                this.ClearMemoryCache(); // flush out any prior attempt
                await Assert.ThrowsAsync<BadCacheFileException>(() => this.httpClient.GetAsync(PublicTestSite));
            }
            catch
            {
                this.logger.WriteLine("Failure when testing file with length {0}:{1}{2}", truncatedLength, Environment.NewLine, File.ReadAllText(playbackFile));
                throw;
            }
        }
    }

    [Fact]
    public async Task CacheIsSharedAcrossInstances()
    {
        var handler2 = new EchoMessageHandler(this.mockHandler)
        {
            PlaybackRuntimePath = this.echoMessageHandler.PlaybackRuntimePath,
            RecordingSourcePath = this.echoMessageHandler.RecordingSourcePath,
        };
        var httpClient2 = new HttpClient(handler2);

        // The first time should hit the network.
        await this.httpClient.GetAsync(PublicTestSite);
        Assert.Equal(1, this.mockHandler.TrafficCounter);

        // The subsequent time should not, even if using a different instance of the message handler.
        this.mockHandler.ThrowIfCalled = true;
        await httpClient2.GetAsync(PublicTestSite);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRequests_SharedHandler(bool restartSession)
    {
        const int concurrencyLevel = 10;

        await RunParallelRequestsAsync();
        Assert.Equal(concurrencyLevel, this.mockHandler.TrafficCounter);

        // Now verify that they all were cached.
        this.mockHandler.ThrowIfCalled = true;
        if (restartSession)
        {
            this.StartNewSession();
        }

        await RunParallelRequestsAsync();

        Task RunParallelRequestsAsync()
        {
            return Task.WhenAll(Enumerable.Range(1, concurrencyLevel).Select(i => Task.Run(async delegate
            {
                var request = new HttpRequestMessage(HttpMethod.Get, PublicTestSite);
                request.Headers.Add("Custom", i.ToString(CultureInfo.InvariantCulture));
                var response = await this.httpClient.SendAsync(request);
            })));
        }
    }

    [Fact]
    public async Task ConcurrentRequests_UniqueHandlers()
    {
        const int concurrencyLevel = 10;

        await RunParallelRequestsAsync();
        Assert.Equal(concurrencyLevel, this.mockHandler.TrafficCounter);

        // Now verify that they all were cached.
        this.mockHandler.ThrowIfCalled = true;
        this.StartNewSession(); // we need to migrate recordings to playback files so our new handlers can find them.

        await RunParallelRequestsAsync();

        Task RunParallelRequestsAsync()
        {
            return Task.WhenAll(Enumerable.Range(1, concurrencyLevel).Select(i => Task.Run(async delegate
            {
                var request = new HttpRequestMessage(HttpMethod.Get, PublicTestSite);
                request.Headers.Add("Custom", i.ToString(CultureInfo.InvariantCulture));
                var httpClient = new HttpClient(new EchoMessageHandler(this.mockHandler)
                {
                    PlaybackRuntimePath = this.echoMessageHandler.PlaybackRuntimePath,
                    RecordingSourcePath = this.echoMessageHandler.RecordingSourcePath,
                });
                var response = await httpClient.SendAsync(request);
            })));
        }
    }

    private static string GetOutputDirectory()
    {
        string thisAssemblyPathUri = typeof(EchoMessageHandlerTests).GetTypeInfo().Assembly.CodeBase;
        string thisAssemblyLocalPath = new Uri(thisAssemblyPathUri).LocalPath;
        return Path.GetDirectoryName(thisAssemblyLocalPath);
    }

    private void StartNewSession()
    {
        this.echoMessageHandler = new EchoMessageHandler(this.mockHandler)
        {
            RecordingSourcePath = Path.Combine(this.tempDir, "recording"),
            PlaybackRuntimePath = Path.Combine(this.tempDir, "playback"),
        };
        this.ClearMemoryCache();

        // If prior recordings existed, migrate them to playback.
        // This emulates the anticipated build step that will occur in test projects to deploy recorded files.
        var recordingDir = new DirectoryInfo(this.echoMessageHandler.RecordingSourcePath);
        if (recordingDir.Exists)
        {
            foreach (var file in recordingDir.EnumerateFiles())
            {
                Directory.CreateDirectory(this.echoMessageHandler.PlaybackRuntimePath);
                file.CopyTo(Path.Combine(this.echoMessageHandler.PlaybackRuntimePath, file.Name), overwrite: true);
            }
        }

        this.httpClient = new HttpClient(this.echoMessageHandler);
    }

    private void ClearMemoryCache()
    {
        HttpMessageCache.Get(this.echoMessageHandler.PlaybackRuntimePath).Reset();
    }

    /// <summary>
    /// Copies a test asset with the given name to the test's isolated playback directory.
    /// </summary>
    /// <param name="name">The name of the file under the TestAssets folder, excluding the .vcr extension.</param>
    /// <returns>The full path to the file created in the test's isolated playback directory.</returns>
    private async Task<string> UseTestReplayFileAsync([CallerMemberName] string name = null)
    {
        using (var assetStream = typeof(EchoMessageHandlerTests).GetTypeInfo().Assembly.GetManifestResourceStream($"TestAssets.{name}.vcr"))
        {
            Requires.Argument(assetStream != null, nameof(name), "No test asset by that name found.");
            string placedFilePath = Path.Combine(this.echoMessageHandler.PlaybackRuntimePath, "HttpMessageCache.vcr");
            Directory.CreateDirectory(this.echoMessageHandler.PlaybackRuntimePath);
            using (var placedFileStream = File.OpenWrite(placedFilePath))
            {
                await assetStream.CopyToAsync(placedFileStream);
            }

            return placedFilePath;
        }
    }

    private class MockInnerHandler : DelegatingHandler
    {
        private int trafficCounter;

        public int TrafficCounter => this.trafficCounter;

        public bool ThrowIfCalled { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(this.ThrowIfCalled, "Unexpected call to inner handler.");
            Interlocked.Increment(ref this.trafficCounter);

            Encoding encoding = Encoding.UTF8;
            var mockContent = new StringContent(MockContentString, encoding);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = mockContent,
            };
            return Task.FromResult(response);
        }
    }
}
