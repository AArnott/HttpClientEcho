// Copyright (c) Andrew Arnott. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HttpClientEcho;
using Xunit;

public class EchoMessageHandlerTests
{
    private readonly MockInnerHandler mockHandler = new MockInnerHandler(new HttpClientHandler());

    [Fact]
    public async Task SkipCacheLookupForwardsToInnerHandler()
    {
        var httpClient = new HttpClient(EchoMessageHandler.Create(EchoBehaviors.SkipCacheLookup, innerHandler: this.mockHandler));
        var response = await httpClient.GetAsync("https://www.bing.com/");
        response.EnsureSuccessStatusCode();
        Assert.Equal(1, this.mockHandler.TrafficCounter);
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task CacheLookupDoesNotForwardToInnerHandler()
    {
        var httpClient = new HttpClient(EchoMessageHandler.Create(EchoBehaviors.AllowReplay, innerHandler: this.mockHandler));
        var response = await httpClient.GetAsync("https://www.bing.com/");
        response.EnsureSuccessStatusCode();
        Assert.Equal(0, this.mockHandler.TrafficCounter);
    }

    [Fact]
    public void NoReplayNoNetworkThrows()
    {
        Assert.Throws<ArgumentException>(() => EchoMessageHandler.Create(EchoBehaviors.RecordResponses));
        Assert.Throws<ArgumentException>(() => EchoMessageHandler.Create(0));
    }

    [Fact(Skip = "Not yet implemented")]
    public async Task CacheMissFailsWhenNetworkDenied()
    {
        var httpClient = new HttpClient(EchoMessageHandler.Create(EchoBehaviors.AllowReplay, innerHandler: this.mockHandler));
        await Assert.ThrowsAsync<NoEchoCacheException>(() => httpClient.GetAsync("https://www.bing.com/"));
    }

    private class MockInnerHandler : DelegatingHandler
    {
        public MockInnerHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        public int TrafficCounter { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.TrafficCounter++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
