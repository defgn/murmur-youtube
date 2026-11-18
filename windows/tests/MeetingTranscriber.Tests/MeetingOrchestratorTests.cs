using MeetingTranscriber.Core;
using Murmur.Abstractions;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class MeetingOrchestratorTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now = new(2026, 11, 18, 9, 40, 0, TimeSpan.Zero);

        public DateTimeOffset Read() => Now;
    }

    private sealed class FakeCompleter : IChatCompleter
    {
        private readonly Queue<string?> _answers;

        public FakeCompleter(params string?[] answers) => _answers = new Queue<string?>(answers);

        public string? LastError { get; set; }

        public List<(string System, string User)> Calls { get; } = [];

        public Task<string?> CompleteAsync(string systemPrompt, string userText, CancellationToken cancellationToken)
        {
            Calls.Add((systemPrompt, userText));
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void Detection_returns_quote_and_counts()
    {
        var clock = new FakeClock();
        using var orchestrator = new MeetingOrchestrator(null, clock.Read);
        var at = clock.Now;
        orchestrator.AddUtterance(FeedKind.Them, "Backfill runs tonight.", at.AddSeconds(-10));

        var alert = orchestrator.AddUtterance(FeedKind.Them, "What do you think Giang?", at);

        Assert.NotNull(alert);
        Assert.Equal(1, alert.TotalCount);
        Assert.Equal(0, alert.EarlierCount);
        Assert.Equal(2, alert.Quote.Count);
        Assert.Equal(1, orchestrator.Mentions.TotalMentions);
    }

    [Fact]
    public void Non_name_utterance_returns_null()
    {
        var clock = new FakeClock();
        using var orchestrator = new MeetingOrchestrator(null, clock.Read);

        var alert = orchestrator.AddUtterance(FeedKind.You, "SCD2 on customer address is in.", clock.Now);

        Assert.Null(alert);
    }

    [Fact]
    public async Task Alert_without_backend_returns_verbatim_quote_and_placeholders()
    {
        var clock = new FakeClock();
        using var orchestrator = new MeetingOrchestrator(null, clock.Read);
        var alert = orchestrator.AddUtterance(FeedKind.Them, "What do you think Giang?", clock.Now);
        Assert.NotNull(alert);

        var content = await orchestrator.ResolveAlertAsync(alert.Quote, CancellationToken.None);

        Assert.Equal("What do you think Giang?", content.Quote);
        Assert.Contains("no answer backend configured", content.AskLine, StringComparison.Ordinal);
        Assert.Contains("no answer backend configured", content.ReplyLine, StringComparison.Ordinal);
        Assert.NotNull(content.Notice);
    }

    [Fact]
    public async Task Alert_with_backend_parses_two_one_liners()
    {
        var clock = new FakeClock();
        var completer = new FakeCompleter("ASK: Marco wants your call on the late fact window.\nREPLY: Keep 3 days for this run.");
        using var orchestrator = new MeetingOrchestrator(completer, clock.Read);
        var alert = orchestrator.AddUtterance(FeedKind.Them, "What do you think Giang?", clock.Now);
        Assert.NotNull(alert);

        var content = await orchestrator.ResolveAlertAsync(alert.Quote, CancellationToken.None);

        Assert.Equal("Marco wants your call on the late fact window.", content.AskLine);
        Assert.Equal("Keep 3 days for this run.", content.ReplyLine);
        Assert.Null(content.Notice);
        Assert.Single(completer.Calls);
    }

    [Fact]
    public async Task Summary_regen_parses_up_to_four_cards()
    {
        var clock = new FakeClock();
        var completer = new FakeCompleter(
            "BRIDGE: Mapped, one duplicate key item open.\nSCD2: Customer address live and tested.\n" +
            "MERGE: Match id plus line number, price only updates.\nBACKFILL: Tonight, 40 minutes, counts in the morning.\nEXTRA: Dropped.");
        using var orchestrator = new MeetingOrchestrator(completer, clock.Read);
        orchestrator.AddUtterance(FeedKind.Them, "Morning all.", clock.Now);

        var update = await orchestrator.RegenerateSummaryAsync([], CancellationToken.None);

        Assert.Equal(4, update.Cards.Count);
        Assert.Equal("BRIDGE", update.Cards[0].Label);
        Assert.Null(update.Notice);
        Assert.Equal(0, orchestrator.UtterancesSinceSummary);
    }

    [Fact]
    public async Task Summary_without_backend_keeps_previous_cards_with_notice()
    {
        var clock = new FakeClock();
        using var orchestrator = new MeetingOrchestrator(null, clock.Read);
        var previous = new List<SummaryCard> { new("BRIDGE", "Mapped.") };

        var update = await orchestrator.RegenerateSummaryAsync(previous, CancellationToken.None);

        Assert.Same(previous, update.Cards);
        Assert.Contains("no answer backend configured", update.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_due_after_six_utterances()
    {
        var clock = new FakeClock();
        using var orchestrator = new MeetingOrchestrator(null, clock.Read);

        for (var i = 0; i < 5; i++)
        {
            orchestrator.AddUtterance(FeedKind.Them, "Line " + i + ".", clock.Now);
        }

        Assert.False(orchestrator.IsSummaryDue());
        orchestrator.AddUtterance(FeedKind.Them, "Line 5.", clock.Now);
        Assert.True(orchestrator.IsSummaryDue());
    }
}
