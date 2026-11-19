using System.Net;
using MeetingTranscriber.Cloud;
using Shouldly;
using Xunit;

namespace MeetingTranscriber.Tests;

/// <summary>
/// Regression tests for the Codex (ChatGPT subscription) wire shape. The server
/// rejects input items without an explicit role ("Invalid value: ''"), so the
/// request body must carry typed user messages.
/// </summary>
public class CodexWireTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            const string sse = "data: {\"type\":\"response.output_text.done\",\"text\":\"OK\"}\n" +
                               "data: [DONE]\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse),
            });
        }
    }

    [Fact]
    public async Task Codex_request_sends_typed_user_message_and_parses_sse()
    {
        var handler = new CapturingHandler();
        using var completer = new MtCompleter(
            handler, CloudBackend.CodexSubscription, _ => Task.FromResult<string?>("tok"), "gpt-6.1-sol");

        var answer = await completer.CompleteAsync("system prompt", "hello", CancellationToken.None);

        answer.ShouldBe("OK");
        handler.LastRequest!.RequestUri!.AbsoluteUri.ShouldContain("codex/responses");
        handler.LastBody.ShouldNotBeNull();
        handler.LastBody.ShouldContain("\"model\":\"gpt-6.1-sol\"");
        handler.LastBody.ShouldContain("\"type\":\"message\"");
        handler.LastBody.ShouldContain("\"role\":\"user\"");
        handler.LastBody.ShouldContain("\"type\":\"input_text\"");
        handler.LastBody.ShouldNotContain("\"role\":\"\"");
    }

    [Fact]
    public async Task Codex_request_surfaces_http_error_as_last_error()
    {
        var handler = new FailingHandler();
        using var completer = new MtCompleter(
            handler, CloudBackend.CodexSubscription, _ => Task.FromResult<string?>("tok"), "gpt-6.1-sol");

        var answer = await completer.CompleteAsync("system prompt", "hello", CancellationToken.None);

        answer.ShouldBeNull();
        completer.LastError.ShouldNotBeNullOrWhiteSpace();
        completer.LastError.ShouldContain("400");
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"error\":{\"message\":\"model not supported\"}}"),
            });
        }
    }
}
