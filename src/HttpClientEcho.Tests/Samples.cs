// Copyright (c) Andrew Arnott. All rights reserved.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using HttpClientEcho;
using Xunit;

public class Samples
{
    [Fact]
    public async Task CacheBing()
    {
        var httpClient = new HttpClient(new EchoMessageHandler());
        var response = await httpClient.GetAsync("https://www.bing.com/");
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CacheGoogle()
    {
        var httpClient = new HttpClient(new EchoMessageHandler());
        var response = await httpClient.GetAsync("https://www.google.com/");
        response.EnsureSuccessStatusCode();
    }
}
