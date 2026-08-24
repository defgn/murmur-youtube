using Murmur.Abstractions;
using Murmur.App;
using Murmur.Core;
using Murmur.Testing;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

/// <summary>
/// Command Mode end to end: the command hotkey records an instruction, the coordinator
/// reads the selection, the cleaner rewrites it, and the result is pasted over the
/// selection. Every failure path ends in a notice and never types anything.
/// </summary>
public sealed class CommandModeCoordinatorTests
{
    [Fact]
    public async Task Instruction_rewrites_the_selection_and_pastes_it()
    {
        var commandHotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("make this more formal");
        var injector = new RecordingTextInjector();
        var selection = new FakeSelectionReader { Result = "hey dude what's up" };
        var cleaner = new FakeSmartCleaner { Result = "Dear Sir, I hope this finds you well." };
        var notices = new List<string>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), transcriber, injector,
            () => [], new FakeClock(), commandHotkey: commandHotkey);

        using var coordinator = new CommandModeCoordinator(
            engine, cleaner, injector, selection, notices.Add);

        await DictateAsync(commandHotkey, engine);
        await WaitForAsync(() => injector.Injected.Count > 0);

        cleaner.LastInstruction.ShouldBe("Make this more formal.");
        cleaner.LastText.ShouldBe("hey dude what's up");
        injector.Injected.ShouldHaveSingleItem().ShouldBe("Dear Sir, I hope this finds you well.");
        notices.ShouldContain(m => m.Contains("Command done", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task No_selection_notices_and_types_nothing()
    {
        var commandHotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("make this more formal");
        var injector = new RecordingTextInjector();
        var cleaner = new FakeSmartCleaner { Result = "rewritten" };
        var notices = new List<string>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), transcriber, injector,
            () => [], new FakeClock(), commandHotkey: commandHotkey);

        using var coordinator = new CommandModeCoordinator(
            engine, cleaner, injector, new FakeSelectionReader(), notices.Add);

        await DictateAsync(commandHotkey, engine);
        await WaitForAsync(() => notices.Count > 0);

        notices.ShouldContain(m => m.Contains("Select some text first", StringComparison.OrdinalIgnoreCase));
        injector.Injected.ShouldBeEmpty();
        cleaner.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Cleaner_failure_notices_and_types_nothing()
    {
        var commandHotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("make this more formal");
        var injector = new RecordingTextInjector();
        var selection = new FakeSelectionReader { Result = "selected text" };
        var cleaner = new FakeSmartCleaner { Result = null };
        var notices = new List<string>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), transcriber, injector,
            () => [], new FakeClock(), commandHotkey: commandHotkey);

        using var coordinator = new CommandModeCoordinator(
            engine, cleaner, injector, selection, notices.Add);

        await DictateAsync(commandHotkey, engine);
        await WaitForAsync(() => notices.Count > 0);

        notices.ShouldContain(m => m.Contains("Could not rewrite", StringComparison.OrdinalIgnoreCase));
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Missing_model_notices_and_types_nothing()
    {
        var commandHotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("make this more formal");
        var injector = new RecordingTextInjector();
        var selection = new FakeSelectionReader { Result = "selected text" };
        var notices = new List<string>();

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), transcriber, injector,
            () => [], new FakeClock(), commandHotkey: commandHotkey);

        using var coordinator = new CommandModeCoordinator(
            engine, cleaner: null, injector, selection, notices.Add);

        await DictateAsync(commandHotkey, engine);
        await WaitForAsync(() => notices.Count > 0);

        notices.ShouldContain(m => m.Contains("No cleanup model", StringComparison.OrdinalIgnoreCase));
        injector.Injected.ShouldBeEmpty();
    }

    [Fact]
    public async Task Engine_idle_unload_frees_the_command_cleaner_too()
    {
        var commandHotkey = new FakeHotkeySource();
        var transcriber = new FakeTranscriber("make this more formal");
        var injector = new RecordingTextInjector();
        var cleaner = new FakeSmartCleaner { CanUnload = true };

        await using var engine = new DictationEngine(
            FakeAudioCapture.Tone(0.4), new FakeHotkeySource(), transcriber, injector,
            () => [], new FakeClock(), commandHotkey: commandHotkey,
            idleUnloadTimeout: TimeSpan.FromMilliseconds(150));

        using var coordinator = new CommandModeCoordinator(
            engine, cleaner, injector, new FakeSelectionReader(), _ => { });

        await DictateAsync(commandHotkey, engine);
        for (var i = 0; i < 500 && !cleaner.Unloaded; i++) await Task.Delay(10);

        cleaner.Unloaded.ShouldBeTrue("the idle timeout must unload the command model too");
    }

    private static async Task DictateAsync(FakeHotkeySource hotkey, DictationEngine engine)
    {
        hotkey.Press();
        for (var i = 0; i < 500 && engine.State != DictationState.Recording; i++) await Task.Delay(10);
        for (var i = 0; i < 2000 && engine.Level == 0; i++) await Task.Delay(10);

        hotkey.Release();
        for (var i = 0; i < 500 && engine.State != DictationState.Idle; i++) await Task.Delay(10);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        condition().ShouldBeTrue("the asynchronous command flow did not finish");
    }
}
