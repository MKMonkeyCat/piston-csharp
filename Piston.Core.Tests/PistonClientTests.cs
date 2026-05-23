using System;
// using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
// using System.Collections.Generic;
// using Newtonsoft.Json;
// using Xunit;
// using Piston.Core.Models;

namespace Piston.Core.Tests
{
  public class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
  {
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder = responder ?? throw new ArgumentNullException(nameof(responder));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
      return _responder(request, cancellationToken);
    }
  }

  public class PistonClientTests
  {
    // [Fact]
    // public async Task GetRuntimesAsync_ReturnsRuntimes()
    // {
    //   var runtimes = new List<PistonRuntime> { new() { Language = "python", Version = "3.10.4" } };
    //   var json = JsonConvert.SerializeObject(runtimes);
    //   var handler = new FakeHandler((req, ct) =>
    //   {
    //     var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    //     return Task.FromResult(resp);
    //   });
    //   var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
    //   var piston = new PistonClient(client);
    //   var result = await piston.GetRuntimesAsync();
    //   Assert.Single(result);
    //   Assert.Equal("python", result[0].Language);
    // }

    // [Fact]
    // public async Task GetLanguagesAsync_ReturnsDistinctCachedLanguages()
    // {
    //   var runtimes = new List<PistonRuntime>
    //   {
    //     new() { Language = "python", Version = "3.10.4" },
    //     new() { Language = "python", Version = "3.11.0" },
    //     new() { Language = "csharp", Version = "8.0" }
    //   };
    //   var json = JsonConvert.SerializeObject(runtimes);
    //   var requestCount = 0;
    //   var handler = new FakeHandler((req, ct) =>
    //   {
    //     requestCount++;
    //     var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    //     return Task.FromResult(resp);
    //   });
    //   var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
    //   var piston = new PistonClient(client);

    //   var languages = await piston.GetLanguagesAsync();
    //   var cachedLanguages = await piston.GetLanguagesAsync();

    //   Assert.Equal(1, requestCount);
    //   Assert.Equal(new[] { "python", "csharp" }, languages);
    //   Assert.Equal(languages, cachedLanguages);
    // }

    // [Fact]
    // public async Task ExecuteAsync_ReturnsResult()
    // {
    //   var resultObj = new PistonResult
    //   {
    //     Language = "python",
    //     Version = "3.10",
    //     Run = new PistonStageResult { Stdout = "ok", Code = 0 }
    //   };
    //   var json = JsonConvert.SerializeObject(resultObj);
    //   var handler = new FakeHandler((req, ct) =>
    //   {
    //     var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    //     return Task.FromResult(resp);
    //   });
    //   var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
    //   var piston = new PistonClient(client);
    //   var res = await piston.ExecuteAsync("python", "3.10", "print('hi')");
    //   Assert.Equal("python", res.Language);
    //   Assert.Equal(0, res.Run.Code);
    // }

    // [Fact]
    // public async Task ExecuteAsync_NonSuccess_ThrowsPistonException()
    // {
    //   var handler = new FakeHandler((req, ct) =>
    //   {
    //     var resp = new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error") };
    //     return Task.FromResult(resp);
    //   });
    //   var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
    //   var piston = new PistonClient(client);
    //   await Assert.ThrowsAsync<PistonException>(() => piston.ExecuteAsync("py", "3", "x"));
    // }

    // [Fact]
    // public async Task ExecuteAsync_Cancellation_ThrowsPistonException()
    // {
    //   var handler = new FakeHandler((req, ct) =>
    //   {
    //     throw new OperationCanceledException();
    //   });
    //   var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
    //   var piston = new PistonClient(client);
    //   await Assert.ThrowsAsync<PistonException>(() => piston.ExecuteAsync("py", "3", "x", CancellationToken.None));
    // }
  }
}
