using Murmur.Abstractions;
using Murmur.Core;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

public class QuestionDetectorTests
{
    [Theory]
    [InlineData("What is a bridge table?")]
    [InlineData("how would you handle slowly changing dimensions")]
    [InlineData("Can you describe your last project")]
    [InlineData("Tell me about a time you failed.")]
    [InlineData("Walk me through the design.")]
    [InlineData("Explain eventual consistency to me")]
    [InlineData("So you've used Fabric, right")]
    [InlineData("Why did you choose that approach?")]
    [InlineData("Give me an example of a hard trade-off")]
    public void Detects_interviewer_questions(string text) =>
        Assert.True(QuestionDetector.IsQuestion(text));

    [Theory]
    [InlineData("So a bridge table comes in when you've got a many-to-many.")]
    [InlineData("I handled that with a MERGE in the load pipeline.")]
    [InlineData("The main trade-off is complexity in the DAX and ETL.")]
    [InlineData("In my last role we moved to a lakehouse.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Does_not_flag_candidate_answers(string text) =>
        Assert.False(QuestionDetector.IsQuestion(text));
}

/// <summary>
/// A completer that records calls and returns scripted answers, so the assistant's flow
/// is testable without any model.
/// </summary>
public sealed class FakeCompleter : IChatCompleter
{
    private readonly Queue<string?> _answers;
    public List<(string System, string User)> Calls { get; } = [];

    public FakeCompleter(params string?[] answers) => _answers = new Queue<string?>(answers);

    public Task<string?> CompleteAsync(
        string systemPrompt, string userText, CancellationToken cancellationToken)
    {
        Calls.Add((systemPrompt, userText));
        return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
    }

    public void Dispose() { }
}

public class InterviewAssistantTests
{
    private static InterviewAssistant Build(IChatCompleter completer, bool enabled = true) =>
        new(completer, () => enabled);

    private static InterviewTurn QuestionTurn(string text) =>
        new(DateTimeOffset.Now, Speaker.Interviewer, text, IsQuestion: true);

    [Fact]
    public async Task A_question_triggers_a_draft_with_full_and_short_answers()
    {
        var completer = new FakeCompleter("FULL ANSWER", "SHORT ANSWER");
        using var assistant = Build(completer);

        InterviewAnswer? received = null;
        assistant.AnswerReady += (_, a) => received = a;

        assistant.Observe(QuestionTurn("What is a star schema?"));
        await Task.Delay(100);

        Assert.NotNull(received);
        Assert.Equal("FULL ANSWER", received!.FullAnswer);
        Assert.Equal("SHORT ANSWER", received.ShortAnswer);
        Assert.Equal(InterviewState.Answered, assistant.State);
        Assert.Equal(2, completer.Calls.Count);
    }

    [Fact]
    public void Candidate_turns_never_trigger_a_draft()
    {
        var completer = new FakeCompleter();
        using var assistant = Build(completer);

        assistant.Observe(new InterviewTurn(
            DateTimeOffset.Now, Speaker.Candidate, "What did you do next?", IsQuestion: false));

        Assert.Empty(completer.Calls);
        Assert.Equal(InterviewState.Listening, assistant.State);
    }

    [Fact]
    public void Non_question_interviewer_turns_are_ignored()
    {
        var completer = new FakeCompleter();
        using var assistant = Build(completer);

        assistant.Observe(new InterviewTurn(
            DateTimeOffset.Now, Speaker.Interviewer, "Let's move to data modelling.",
            IsQuestion: false));

        Assert.Empty(completer.Calls);
    }

    [Fact]
    public async Task A_regenerate_re_drafts_with_a_fresh_angle_prompt()
    {
        var completer = new FakeCompleter("A1", "A1s", "A2", "A2s");
        using var assistant = Build(completer);

        var answers = new List<InterviewAnswer>();
        assistant.AnswerReady += (_, a) => answers.Add(a);

        assistant.Observe(QuestionTurn("Why CDC?"));
        await Task.Delay(100);
        assistant.Regenerate();
        await Task.Delay(200);

        Assert.Equal(2, answers.Count);
        Assert.Equal(2, answers[1].Attempt);

        // The re-draft call carries the fresh-angle instruction.
        Assert.Contains("re-draft", completer.Calls[2].System);
    }

    [Fact]
    public async Task A_null_answer_surfaces_a_failure_not_an_exception()
    {
        var completer = new FakeCompleter((string?)null);   // backend unavailable
        using var assistant = Build(completer);

        string? failure = null;
        assistant.DraftFailed += (_, m) => failure = m;

        assistant.Observe(QuestionTurn("Why CDC?"));
        await Task.Delay(100);

        Assert.NotNull(failure);
        Assert.Single(completer.Calls);   // full answer attempted, short skipped
    }

    [Fact]
    public async Task Disabled_assistant_reports_the_switch_rather_than_drafting()
    {
        var completer = new FakeCompleter("X");
        using var assistant = Build(completer, enabled: false);

        string? failure = null;
        assistant.DraftFailed += (_, m) => failure = m;

        assistant.Observe(QuestionTurn("Why CDC?"));
        await Task.Delay(100);

        Assert.NotNull(failure);
        Assert.Empty(completer.Calls);
    }
}

public class UtteranceSegmenterTests
{
    private const int Rate = AudioChunk.SampleRate;

    [Fact]
    public void Too_short_audio_never_closes()
    {
        // 300 ms of speech, 2 s of trailing silence: below the minimum utterance.
        Assert.False(UtteranceSegmenter.IsBoundary(
            samplesSoFar: Rate * 300 / 1000,
            trailingSilentSamples: Rate * 2,
            peakRms: 0.1f));
    }

    [Fact]
    public void Speech_with_a_long_quiet_tail_closes()
    {
        Assert.True(UtteranceSegmenter.IsBoundary(
            samplesSoFar: Rate * 5,
            trailingSilentSamples: Rate * 1,
            peakRms: 0.1f));
    }

    [Fact]
    public void Continuous_speech_does_not_close()
    {
        Assert.False(UtteranceSegmenter.IsBoundary(
            samplesSoFar: Rate * 5,
            trailingSilentSamples: Rate / 10,   // 100 ms gap only
            peakRms: 0.1f));
    }

    [Fact]
    public void Near_silence_never_counts_as_speech()
    {
        // Long, but the peak never rose above the floor: a hiss, not a voice.
        Assert.False(UtteranceSegmenter.IsBoundary(
            samplesSoFar: Rate * 5,
            trailingSilentSamples: Rate * 2,
            peakRms: 0.004f));
    }

    [Fact]
    public void Overlong_utterances_are_force_flushed()
    {
        Assert.True(UtteranceSegmenter.MustFlush(
            samplesSoFar: UtteranceSegmenter.MaxUtteranceSeconds * Rate + 1));
        Assert.False(UtteranceSegmenter.MustFlush(samplesSoFar: Rate));
    }
}
