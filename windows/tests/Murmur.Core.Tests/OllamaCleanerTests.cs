using System.Net;
using System.Text;
using System.Text.Json;
using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// The Ollama smart-cleaner contract: polish on success, silent null on any failure —
/// a dead Ollama must never turn a dictation into an error.
/// </summary>
public sealed class OllamaCleanerTests
{
    [Fact]
    public async Task CleanAsync_returns_the_polished_text_and_asks_for_cleanup()
    {
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.PathAndQuery == "/api/chat")
            {
                return Json(HttpStatusCode.OK, new { message = new { role = "assistant", content = "Hello, world." } });
            }

            return Json(HttpStatusCode.NotFound, null);
        });

        using var cleaner = new OllamaCleaner(handler, model: "qwen3.6-27b");
        var result = await cleaner.CleanAsync("hello world", CancellationToken.None);

        result.ShouldBe("Hello, world.");

        var chat = handler.Requests.ShouldHaveSingleItem();
        chat.RequestUri!.AbsolutePath.ShouldBe("/api/chat");

        using var body = JsonDocument.Parse(await chat.Content!.ReadAsStringAsync());
        body.RootElement.GetProperty("model").GetString().ShouldBe("qwen3.6-27b");
        body.RootElement.GetProperty("options").GetProperty("temperature").GetDouble().ShouldBe(0);

        var system = body.RootElement.GetProperty("messages")[0];
        system.GetProperty("role").GetString().ShouldBe("system");
        system.GetProperty("content").GetString()!.ShouldContain("Never invent");
    }

    [Fact]
    public async Task Model_is_auto_picked_from_installed_tags()
    {
        var handler = new FakeHandler(request =>
            request.RequestUri!.PathAndQuery == "/api/tags"
                ? Json(HttpStatusCode.OK, new { models = new[] { new { name = "qwen3.6-27b:latest" } } })
                : Json(HttpStatusCode.OK, new { message = new { role = "assistant", content = "polished" } }));

        using var cleaner = new OllamaCleaner(handler);
        var result = await cleaner.CleanAsync("text", CancellationToken.None);

        result.ShouldBe("polished");
        handler.Requests.Count.ShouldBe(2);
        var chat = handler.Requests[1];
        using var body = JsonDocument.Parse(await chat.Content!.ReadAsStringAsync());
        body.RootElement.GetProperty("model").GetString().ShouldBe("qwen3.6-27b:latest");
    }

    [Fact]
    public async Task Ollama_down_returns_null()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        using var cleaner = new OllamaCleaner(handler);

        (await cleaner.CleanAsync("text", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task No_installed_model_returns_null()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { models = Array.Empty<object>() }));
        using var cleaner = new OllamaCleaner(handler);

        (await cleaner.CleanAsync("text", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Garbage_response_returns_null()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all", Encoding.UTF8),
        });
        using var cleaner = new OllamaCleaner(handler, model: "qwen3.6-27b");

        (await cleaner.CleanAsync("text", CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Blank_polish_returns_null()
    {
        var handler = new FakeHandler(_ => Json(HttpStatusCode.OK, new { message = new { role = "assistant", content = "   " } }));
        using var cleaner = new OllamaCleaner(handler, model: "qwen3.6-27b");

        (await cleaner.CleanAsync("text", CancellationToken.None)).ShouldBeNull();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object? payload) => new(status)
    {
        Content = new StringContent(
            payload is null ? string.Empty : JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json"),
    };

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }
}
