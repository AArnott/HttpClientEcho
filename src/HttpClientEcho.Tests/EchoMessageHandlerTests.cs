// Copyright (c) Andrew Arnott. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpClientEcho;
using Xunit;

public class EchoMessageHandlerTests : IDisposable
{
    private const string PublicTestSite = "https://www.bing.com/";

    private const string MockContentString = "Mock data";

    private readonly MockInnerHandler mockHandler;

    private HttpClient httpClient;

    private EchoMessageHandler echoMessageHandler;

    private string tempDir;

    public EchoMessageHandlerTests()
    {
        // Pick a directory to isolate this test's input/output.
        // Precreate it, so that we exercise the library's willingness to create just one directory deeper.
        this.tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(this.tempDir);

        this.mockHandler = new MockInnerHandler();
        this.echoMessageHandler = new EchoMessageHandler(this.mockHandler)
        {
            RecordingSourcePath = Path.Combine(this.tempDir, "recording"),
            PlaybackRuntimePath = Path.Combine(this.tempDir, "playback"),
        };
        this.httpClient = new HttpClient(this.echoMessageHandler);
    }

    public void Dispose()
    {
        Directory.Delete(this.tempDir, recursive: true);
    }

    [Fact]
    public void PlaybackRuntimePath()
    {
        var echoMessageHandler = new EchoMessageHandler();
        Assert.Equal("HttpClientEcho\\", echoMessageHandler.PlaybackRuntimePath);
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

    [Fact]
    public async Task StoreCacheThenReuseTwice()
    {
        var response = await this.httpClient.GetAsync(PublicTestSite);
        Assert.Equal(1, this.mockHandler.TrafficCounter);
        response.EnsureSuccessStatusCode();
        string actual = await response.Content.ReadAsStringAsync();
        Assert.Equal(MockContentString, actual);

        // From here on out, the cache should be hit.
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

    [Fact(Skip = "We need to prepopulate the cache.")]
    public async Task CacheLookupDoesNotForwardToInnerHandler()
    {
        this.echoMessageHandler.Behaviors = EchoBehaviors.DenyNetworkCalls;
        this.mockHandler.ThrowIfCalled = true;
        var response = await this.httpClient.GetAsync(PublicTestSite);
        response.EnsureSuccessStatusCode();
        Assert.NotNull(response.Content);
        string actual = await response.Content.ReadAsStringAsync();
        Assert.Equal(MockContentString, actual);
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

    private static string GetOutputDirectory()
    {
        string thisAssemblyPathUri = typeof(EchoMessageHandlerTests).GetTypeInfo().Assembly.CodeBase;
        string thisAssemblyLocalPath = new Uri(thisAssemblyPathUri).LocalPath;
        return Path.GetDirectoryName(thisAssemblyLocalPath);
    }

    private class MockInnerHandler : DelegatingHandler
    {
        public int TrafficCounter { get; set; }

        public bool ThrowIfCalled { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.False(this.ThrowIfCalled, "Unexpected call to inner handler.");
            this.TrafficCounter++;

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
