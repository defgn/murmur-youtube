using MeetingTranscriber.Core;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class MentionTrackerTests
{
    [Fact]
    public void First_detection_has_zero_earlier()
    {
        var tracker = new MentionTracker();
        var now = DateTimeOffset.UtcNow;

        tracker.Record(now);

        Assert.Equal(1, tracker.TotalMentions);
        Assert.Equal(0, tracker.EarlierMentions);
        Assert.True(tracker.HasActiveAlert);
        Assert.Equal(now, tracker.LastMentionAt);
    }

    [Fact]
    public void Second_detection_reports_one_earlier()
    {
        var tracker = new MentionTracker();
        tracker.Record(DateTimeOffset.UtcNow);

        tracker.Record(DateTimeOffset.UtcNow.AddMinutes(4));

        Assert.Equal(2, tracker.TotalMentions);
        Assert.Equal(1, tracker.EarlierMentions);
    }

    [Fact]
    public void Dismiss_clears_strip_but_keeps_count_and_refires()
    {
        var tracker = new MentionTracker();
        tracker.Record(DateTimeOffset.UtcNow);
        tracker.Record(DateTimeOffset.UtcNow.AddMinutes(4));

        tracker.Dismiss();

        Assert.False(tracker.HasActiveAlert);
        Assert.Equal(2, tracker.TotalMentions);
        Assert.Equal(2, tracker.EarlierMentions);

        tracker.Record(DateTimeOffset.UtcNow.AddMinutes(8));

        Assert.True(tracker.HasActiveAlert);
        Assert.Equal(3, tracker.TotalMentions);
        Assert.Equal(2, tracker.EarlierMentions);
    }
}
