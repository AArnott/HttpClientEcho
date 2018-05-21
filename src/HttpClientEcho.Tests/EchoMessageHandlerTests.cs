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
        this.tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
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
