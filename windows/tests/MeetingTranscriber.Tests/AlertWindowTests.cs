using MeetingTranscriber.Core;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class AlertWindowTests
{
    private static readonly DateTimeOffset Base =
        new(2026, 11, 18, 9, 43, 12, TimeSpan.Zero);

    private static TranscriptUtterance At(double secondsBefore, string text, FeedKind feed = FeedKind.Them) =>
        new(Guid.NewGuid(), Base.AddSeconds(-secondsBefore), feed, text);

    [Fact]
    public void Short_trigger_includes_preceding_utterances_within_20s()
    {
        var older = At(60, "Morning all. Bridge tables are mapped.");
        var prev = At(15, "Backfill runs tonight, about 40 minutes.");
        var trigger = At(0, "Giang?");
        var history = new List<TranscriptUtterance> { older, prev, trigger };

        var window = AlertWindow.SelectWindow(history, trigger);

        Assert.Equal(2, window.Count);
        Assert.Equal(prev.Id, window[0].Id);
        Assert.Equal(trigger.Id, window[1].Id);
        Assert.Equal("Backfill runs tonight, about 40 minutes. Giang?", AlertWindow.FormatQuote(window));
    }

    [Fact]
    public void Long_trigger_quotes_alone()
    {
        var prev = At(10, "Backfill runs tonight, about 40 minutes.");
        var trigger = At(0, "What do you think Giang? Should we keep the late arriving fact window at 3 days or cut it to 1?");
        var history = new List<TranscriptUtterance> { prev, trigger };

        var window = AlertWindow.SelectWindow(history, trigger);

        Assert.Single(window);
        Assert.Equal(trigger.Text, AlertWindow.FormatQuote(window));
    }

    [Fact]
    public void Window_is_capped_at_20s()
    {
        var outside = At(25, "SCD2 on customer address is in.");
        var inside = At(19, "MERGE strategy is agreed.");
        var trigger = At(0, "Giang, quick one.");
        var history = new List<TranscriptUtterance> { outside, inside, trigger };

        var window = AlertWindow.SelectWindow(history, trigger);

        Assert.Equal(2, window.Count);
        Assert.DoesNotContain(window, u => u.Id == outside.Id);
    }

    [Fact]
    public void Trigger_missing_from_history_is_still_quoted()
    {
        var trigger = At(0, "Giang?");

        var window = AlertWindow.SelectWindow([], trigger);

        Assert.Single(window);
    }

    [Fact]
    public void Short_trigger_with_no_predecessor_quotes_trigger_only()
    {
        var trigger = At(0, "Giang?");

        var window = AlertWindow.SelectWindow([trigger], trigger);

        Assert.Single(window);
    }
}
