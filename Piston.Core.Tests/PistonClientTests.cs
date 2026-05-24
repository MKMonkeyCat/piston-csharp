using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Piston.Core.Models;
using Xunit;

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
        [Fact]
        public async Task ExecuteAsync_InvalidFilename_ThrowsBeforeSending()
        {
            var requestCount = 0;
            var handler = new FakeHandler((req, ct) =>
            {
                requestCount++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
            });

            var client = new HttpClient(handler) { BaseAddress = new Uri("https://example.org/") };
            var piston = new PistonClient(client);

            var files = new List<PistonFile>
            {
                new() { Name = "t/../t.txt", Content = "hello" }
            };

            await Assert.ThrowsAsync<ArgumentException>(() => piston.ExecuteAsync("python", "3.10", files));
            Assert.Equal(0, requestCount);
        }
    }
}
