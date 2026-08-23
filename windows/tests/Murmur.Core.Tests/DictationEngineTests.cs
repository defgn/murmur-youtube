using Murmur.Abstractions;
using Murmur.Core;
using Murmur.Dictionary;
using Murmur.Testing;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Murmur.CoreTests;

/// <summary>
/// Exercises the entire dictation path with fakes.
/// </summary>
/// <remarks>
/// This is the substitute for a Windows machine. Everything here runs on any platform, so a
/// regression in the state machine, the chunking, or the correction pass is caught on the
/// developer's own machine rather than discovered by a user on Windows.
/// </remarks>
public sealed class DictationEngineTests
{
    private static DictationEngine Build(
        IAudioCapture capture,
        FakeHotkeySource hotkey,
        FakeTranscriber transcriber,
        ITextInjector injector,
        params DictionaryEntry[] dictionary) =>
        Build(capture, hotkey, transcriber, injector, partialInterval: null, idleUnloadTimeout: default, dictionary);

    private static DictationEngine Build(
        IAudioCapture capture,
        FakeHotkeySource hotkey,
        FakeTranscriber transcriber,
        ITextInjector injector,
        TimeSpan? partialInterval,
        TimeSpan idleUnloadTimeout = default,
        params DictionaryEntry[] dictionary) =>
        new(capture, hotkey, transcriber, injector, () => dictionary, new FakeClock(),
            removeFillers: true, partialInterval: partialInterval, idleUnloadTimeout: idleUnloadTimeout);

    /// <summary>Presses, waits for capture to drain, then releases.</summary>
    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 500 && engine.State != DictationState.Recording; i++) await Task.Delay(10);
        for (var i = 0; i < 2000 && engine.Level == 0; i++) await Task.Delay(10);

        hotkey.Release();
        for (var i = 0; i < 500 && engine.State != DictationState.Idle; i++) await Task.Delay(10);
    }

    [Fact]
    public async Task Transcriber_is_loaded_before_first_utterance()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("hello");
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            FakeAudioCapture.Tone(0.2), hotkey, transcriber, injector);

        await DictateAsync(hotkey, engine);

        // The contract, enforced by the fake itself: a transcriber that is asked to work
        // before being loaded throws. This test exists so the *reason* is visible — the
        // engine must load the model on first use, not on the second dictation.
        transcriber.IsReady.ShouldBeTrue();
        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("hello");
    }

    [Fact]
    public async Task Speech_is_transcribed_corrected_and_injected()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("I use cloud code every day");
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            FakeAudioCapture.Tone(1.0), hotkey, transcriber, injector,
            DictionaryEntry.Correction("cloud code", "Claude Code"));

        DictationResult? completed = null;
        engine.Completed += (_, r) => completed = r;

        await DictateAsync(hotkey, engine);

        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("I use Claude Code every day");

        completed.ShouldNotBeNull();
        completed.Corrections.ShouldHaveSingleItem();
        completed.Corrections[0].To.ShouldBe("Claude Code");
    }

    [Fact]
    public async Task Silence_injects_nothing()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();

        // An engine that heard nothing returns empty — and empty must never be typed.
        await using var engine = Build(
            FakeAudioCapture.Silence(0.5), hotkey, new FakeTranscriber(""), injector);

        hotkey.Press();
        for (var i = 0; i < 500 && engine.State != DictationState.Recording; i++) await Task.Delay(10);
        hotkey.Release();
        for (var i = 0; i < 500 && engine.State != DictationState.Idle; i++) await Task.Delay(10);

        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Release_without_press_is_ignored()
    {
        var hotkey = new FakeHotkeySource();
        var injector = new RecordingTextInjector();
        await using var engine = Build(
            FakeAudioCapture.Tone(0.2), hotkey, new FakeTranscriber("hello"), injector);

        hotkey.Release();
        for (var i = 0; i < 100 && engine.State == DictationState.Idle; i++) await Task.Delay(10);

        engine.State.ShouldBe(DictationState.Idle);
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dictionary_terms_are_offered_to_the_engine_as_bias()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("anything");

        await using var engine = Build(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, new RecordingTextInjector(),
            DictionaryEntry.Term("Anthropic"),
            DictionaryEntry.Correction("cloud code", "Claude Code"));

        await DictateAsync(hotkey, engine);

        // Both the plain term and the *write* side of the correction get biased — the whole
        // point is to nudge the recogniser toward the correct spelling.
        transcriber.LastBias.ShouldContain("Anthropic");
        transcriber.LastBias.ShouldContain("Claude Code");
    }

    [Fact]
    public async Task State_returns_to_idle_after_a_dictation()
    {
        var hotkey = new FakeHotkeySource();
        await using var engine = Build(
            FakeAudioCapture.Tone(0.3), hotkey, new FakeTranscriber("done"), new RecordingTextInjector());

        engine.State.ShouldBe(DictationState.Idle);
        await DictateAsync(hotkey, engine);
        engine.State.ShouldBe(DictationState.Idle);
        engine.Level.ShouldBe(0);
    }

    [Fact]
    public async Task Failed_injection_keeps_the_transcript_and_notifies()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("um, actually the numbers look good");
        var injector = new FailingTextInjector();
        var notices = new List<string>();
        var completed = new List<DictationResult>();

        await using var engine = Build(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, injector);
        engine.Notice += (_, message) => notices.Add(message);
        engine.Completed += (_, result) => completed.Add(result);

        await DictateAsync(hotkey, engine);

        // The transcript is recorded *before* injection is attempted, so a failed injection
        // never costs the text — this is the contract behind "the text is in the
        // transcriptions list" when typing into an elevated or hostile window fails.
        completed.ShouldHaveSingleItem();
        completed[0].Text.ShouldBe("the numbers look good");
        injector.Injected.ShouldHaveSingleItem();
        notices.ShouldContain(m => m.Contains("could not type", StringComparison.OrdinalIgnoreCase));
        engine.State.ShouldBe(DictationState.Idle);
    }

    [Fact]
    public async Task Live_partials_stream_while_recording_and_never_inject()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("hello world");
        var injector = new RecordingTextInjector();
        var partials = new List<string>();

        // Pre-load so the first partial tick is allowed; the engine deliberately does not
        // trigger the model load from the preview loop.
        await transcriber.LoadAsync(CancellationToken.None);

        await using var engine = Build(
            FakeAudioCapture.Tone(3.0), hotkey, transcriber, injector,
            partialInterval: TimeSpan.FromMilliseconds(100));
        engine.PartialTranscript += (_, text) => partials.Add(text);

        hotkey.Press();
        // ~0.5s with a 100ms preview interval: several partial ticks should have run.
        for (var i = 0; i < 100 && partials.Count == 0; i++) await Task.Delay(50);
        hotkey.Release();
        for (var i = 0; i < 500 && engine.State != DictationState.Idle; i++) await Task.Delay(10);

        partials.ShouldNotBeEmpty("the preview should transcribe while the key is held");
        partials.ShouldAllBe(t => t == "hello world");

        // The final transcript still lands exactly once, and the partials never did.
        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("hello world");
    }

    [Fact]
    public async Task Live_partials_are_cleaned_like_the_final_transcript()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("um, actually hello there");
        var injector = new RecordingTextInjector();
        var partials = new List<string>();

        await transcriber.LoadAsync(CancellationToken.None);

        await using var engine = Build(
            FakeAudioCapture.Tone(3.0), hotkey, transcriber, injector,
            partialInterval: TimeSpan.FromMilliseconds(100));
        engine.PartialTranscript += (_, text) => partials.Add(text);

        hotkey.Press();
        for (var i = 0; i < 100 && partials.Count == 0; i++) await Task.Delay(50);
        hotkey.Release();
        for (var i = 0; i < 500 && engine.State != DictationState.Idle; i++) await Task.Delay(10);

        partials.ShouldNotBeEmpty();
        partials.ShouldAllBe(t => t == "hello there");
    }

    [Fact]
    public async Task Idle_model_unload_frees_the_recognizer_and_the_next_dictation_reloads_it()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("hello");
        var injector = new RecordingTextInjector();

        await using var engine = Build(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, injector,
            partialInterval: null, idleUnloadTimeout: TimeSpan.FromMilliseconds(150));

        // The model loads lazily on the first release.
        await DictateAsync(hotkey, engine);
        transcriber.IsReady.ShouldBeTrue();

        // The idle timeout fires and frees the recognizer — the memory win of the feature.
        // Generous budget: the 150ms timer races whatever else the test runner is doing, so
        // this must tolerate scheduling delays.
        for (var i = 0; i < 500 && transcriber.IsReady; i++) await Task.Delay(25);
        transcriber.IsReady.ShouldBeFalse("the model must be unloaded after the idle timeout");

        // And the next dictation reloads it transparently.
        await DictateAsync(hotkey, engine);
        transcriber.IsReady.ShouldBeTrue();
        var expected = new[] { "hello", "hello" };
        injector.Injected.ShouldBe(expected);
    }

    [Fact]
    public async Task Arithmetic_corrections_are_resolved_end_to_end()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber(
            "can I have three potatoes no one potato no three minus one potatoes");
        var injector = new RecordingTextInjector();
        var completed = new List<DictationResult>();

        await using var engine = Build(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, injector);
        engine.Completed += (_, result) => completed.Add(result);

        await DictateAsync(hotkey, engine);

        // The user's exact Wispr example: the transcript and the injection both say
        // "2 potatoes".
        completed.ShouldHaveSingleItem();
        completed[0].Text.ShouldBe("can I have 2 potatoes");
        injector.Injected.ShouldHaveSingleItem();
        injector.Injected[0].ShouldBe("can I have 2 potatoes");
    }

    [Fact]
    public async Task Smart_cleaner_polishes_the_final_transcript()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("hello world");
        var injector = new RecordingTextInjector();
        var cleaner = new FakeSmartCleaner { Result = "Hello, world." };
        var completed = new List<DictationResult>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, injector,
            () => [], new FakeClock(), removeFillers: true, smartCleaner: cleaner);
        engine.Completed += (_, result) => completed.Add(result);

        await DictateAsync(hotkey, engine);

        cleaner.Calls.ShouldBe(1);
        completed.ShouldHaveSingleItem();
        completed[0].Text.ShouldBe("Hello, world.");
        injector.Injected[0].ShouldBe("Hello, world.");
    }

    [Fact]
    public async Task Smart_cleaner_unavailable_falls_back_and_notifies_once()
    {
        var hotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("hello world");
        var injector = new RecordingTextInjector();
        var cleaner = new FakeSmartCleaner { Result = null };
        var notices = new List<string>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), hotkey, transcriber, injector,
            () => [], new FakeClock(), removeFillers: true, smartCleaner: cleaner);
        engine.Notice += (_, message) => notices.Add(message);

        await DictateAsync(hotkey, engine);
        await DictateAsync(hotkey, engine);

        // The deterministic text stands, and the failure is surfaced once, not every time.
        var expected = new[] { "hello world", "hello world" };
        injector.Injected.ShouldBe(expected);
        notices.Count(m => m.Contains("Smart cleanup unavailable", StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1);
    }

    /// <summary>
    /// The boundary is enforced by the compiler via CA1416, but this fails louder and names
    /// the reason: anything reachable from Core must run in CI on any platform.
    /// </summary>
    [Fact]
    public void Core_does_not_depend_on_any_platform_project()
    {
        var result = Types.InAssembly(typeof(DictationEngine).Assembly)
            .That().ResideInNamespace("Murmur.Core")
            .ShouldNot().HaveDependencyOn("Murmur.Platform")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Murmur.Core must stay platform-neutral: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}

/// <summary>Chunking behaviour around the encoder's hard limit.</summary>
public sealed class AudioSegmenterTests
{
    private static ReadOnlyMemory<float> Seconds(int n) =>
        new float[n * AudioChunk.SampleRate];

    [Fact]
    public void Short_audio_is_one_segment_and_is_not_copied()
    {
        var audio = Seconds(5);
        var pieces = AudioSegmenter.Split(audio);

        pieces.ShouldHaveSingleItem();
        pieces[0].Length.ShouldBe(audio.Length);
    }

    [Fact]
    public void Audio_at_the_limit_is_still_one_segment()
    {
        AudioSegmenter.Split(Seconds(AudioSegmenter.MaxSegmentSeconds)).ShouldHaveSingleItem();
    }

    [Fact]
    public void Long_audio_is_split_and_every_piece_is_under_the_limit()
    {
        var pieces = AudioSegmenter.Split(Seconds(200));

        pieces.Count.ShouldBeGreaterThan(1);
        foreach (var piece in pieces)
        {
            piece.Length.ShouldBeLessThanOrEqualTo(
                AudioSegmenter.MaxSegmentSeconds * AudioChunk.SampleRate);
            piece.Length.ShouldBeGreaterThan(0);
        }
    }

    [Fact]
    public void Splitting_loses_no_samples()
    {
        var pieces = AudioSegmenter.Split(Seconds(200));
        pieces.Sum(p => p.Length).ShouldBe(200 * AudioChunk.SampleRate);
    }

    /// <summary>
    /// 410 seconds is past the point where the encoder's position table overflows and
    /// inference throws rather than degrading. Nothing may reach it.
    /// </summary>
    [Fact]
    public void Nothing_ever_reaches_the_encoder_ceiling()
    {
        var pieces = AudioSegmenter.Split(Seconds(AudioSegmenter.EncoderCeilingSeconds + 100));

        foreach (var piece in pieces)
        {
            var seconds = (double)piece.Length / AudioChunk.SampleRate;
            seconds.ShouldBeLessThan(AudioSegmenter.EncoderCeilingSeconds);
        }
    }

    [Fact]
    public void A_cut_lands_in_the_quiet_part_rather_than_mid_word()
    {
        // Loud throughout, with one silent second placed inside the search window that
        // precedes the ideal cut point. The cut should be drawn to it.
        var total = AudioSegmenter.MaxSegmentSeconds * 2 * AudioChunk.SampleRate;
        var samples = new float[total];
        Array.Fill(samples, 0.5f);

        var quietStart = (AudioSegmenter.MaxSegmentSeconds - 3) * AudioChunk.SampleRate;
        Array.Clear(samples, quietStart, AudioChunk.SampleRate);

        var pieces = AudioSegmenter.Split(samples);
        var firstCut = pieces[0].Length;

        firstCut.ShouldBeGreaterThanOrEqualTo(quietStart);
        firstCut.ShouldBeLessThanOrEqualTo(quietStart + AudioChunk.SampleRate);
    }
}
