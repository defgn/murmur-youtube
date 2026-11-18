using MeetingTranscriber.Core;
using Xunit;

namespace MeetingTranscriberTests;

public sealed class SummarySchedulerTests
{
    private sealed class FakeClock
    {
        public DateTimeOffset Now = new(2026, 11, 18, 9, 30, 0, TimeSpan.Zero);

        public DateTimeOffset Read() => Now;
    }

    [Fact]
    public void Sixth_utterance_triggers_regen()
    {
        var clock = new FakeClock();
        var scheduler = new SummaryScheduler(clock.Read);
        var due = false;

        for (var i = 0; i < 6; i++)
        {
            due = scheduler.NoteUtterance();
        }

        Assert.True(due);
    }

    [Fact]
    public void Fewer_than_six_utterances_stay_quiet_before_interval()
    {
        var clock = new FakeClock();
        var scheduler = new SummaryScheduler(clock.Read);

        var due = false;
        for (var i = 0; i < 5; i++)
        {
            due = scheduler.NoteUtterance();
        }

        Assert.False(due);
        Assert.False(scheduler.CheckTime());
    }

    [Fact]
    public void Interval_elapsed_with_new_material_triggers_regen()
    {
        var clock = new FakeClock();
        var scheduler = new SummaryScheduler(clock.Read);
        scheduler.NoteUtterance();

        clock.Now = clock.Now.AddSeconds(46);

        Assert.True(scheduler.CheckTime());
    }

    [Fact]
    public void Interval_elapsed_with_no_new_material_stays_quiet()
    {
        var clock = new FakeClock();
        var scheduler = new SummaryScheduler(clock.Read);

        clock.Now = clock.Now.AddMinutes(10);

        Assert.False(scheduler.CheckTime());
    }

    [Fact]
    public void MarkRegenerated_resets_both_throttles()
    {
        var clock = new FakeClock();
        var scheduler = new SummaryScheduler(clock.Read);
        for (var i = 0; i < 6; i++)
        {
            scheduler.NoteUtterance();
        }

        scheduler.MarkRegenerated();

        Assert.Equal(0, scheduler.UtterancesSinceRegen);
        Assert.False(scheduler.CheckTime());
    }
}
