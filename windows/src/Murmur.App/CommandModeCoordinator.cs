using Murmur.Abstractions;
using Murmur.Core;

namespace Murmur.App;

/// <summary>
/// Command Mode: select text anywhere, hold the command key, say what to do with it
/// ("make this more formal"), and Woffle rewrites the selection in place.
/// </summary>
/// <remarks>
/// The engine does the listening and transcribing — this wires the rest: read the
/// selection, hand it to the cleanup model as a transform instruction, paste the result
/// back over the selection. Every failure mode ends in a visible notice; nothing is ever
/// typed when a step could not be completed.
/// The cleaner is the <b>same instance the engine uses</b> and the engine owns its
/// disposal; <see cref="SetCleaner"/> only re-points this at the current one.
/// </remarks>
public sealed class CommandModeCoordinator : IDisposable
{
    private ISmartCleaner? _cleaner;
    private readonly ITextInjector? _injector;
    private readonly ISelectionReader? _selectionReader;
    private readonly Action<string> _notice;

    /// <summary>Wires Command Mode to <paramref name="engine"/>'s command hotkey.</summary>
    public CommandModeCoordinator(
        DictationEngine engine,
        ISmartCleaner? cleaner,
        ITextInjector? injector,
        ISelectionReader? selectionReader,
        Action<string> notice)
    {
        _cleaner = cleaner;
        _injector = injector;
        _selectionReader = selectionReader;
        _notice = notice;
        engine.CommandRequested += OnCommandRequested;
    }

    /// <summary>
    /// Points Command Mode at the current smart-clean backend. The engine owns the
    /// instance and its disposal; this just follows the Settings → AI switch.
    /// </summary>
    public void SetCleaner(ISmartCleaner? cleaner) => _cleaner = cleaner;

    private async void OnCommandRequested(object? sender, string instruction)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(instruction)) return;

            if (_cleaner is null)
            {
                _notice("AI cleanup is switched off — Command Mode needs it. Turn it on in "
                       + "Settings → AI.");
                return;
            }

            if (_selectionReader is null)
            {
                _notice("Command Mode is Windows-only (it needs to read the selection).");
                return;
            }

            var selected = await _selectionReader.ReadSelectedTextAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(selected))
            {
                _notice("Select some text first, then hold the command key and speak.");
                return;
            }

            var rewritten = await _cleaner.TransformAsync(instruction, selected, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(rewritten))
            {
                _notice("Could not rewrite the selection — is the cleanup model available?");
                return;
            }

            if (_injector is null)
            {
                _notice("Command Mode is Windows-only (it needs to paste the result).");
                return;
            }

            var pasted = await _injector.InjectAsync(rewritten, CancellationToken.None);
            _notice(pasted
                ? $"Command done: {instruction}"
                : "Rewrote the selection but could not paste it — the focused window may be "
                  + "elevated. The text is on the clipboard.");
        }
        catch (Exception e)
        {
            _notice($"Command failed: {e.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The cleaner belongs to the engine; nothing to release here.
    }
}
