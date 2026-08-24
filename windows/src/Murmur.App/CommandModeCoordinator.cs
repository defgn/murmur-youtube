using Murmur.Abstractions;
using Murmur.Core;

namespace Murmur.App;

/// <summary>
/// Command Mode: select text anywhere, hold the command key, say what to do with it
/// ("make this more formal"), and Woffle rewrites the selection in place.
/// </summary>
/// <remarks>
/// <para>
/// The coordinator owns its own cleaner, deliberately independent of the Settings → AI
/// switch: Command Mode <i>is</i> an LLM feature, and the switch controls the transcript
/// cleanup pass, not the whole idea. Turning the switch off frees the cleanup model but a
/// command still works — the model loads on demand, and idle-unloads with the speech model.
/// </para>
/// </remarks>
public sealed class CommandModeCoordinator : IDisposable
{
    private readonly DictationEngine _engine;
    private readonly ITextInjector? _injector;
    private readonly ISelectionReader? _selectionReader;
    private readonly Action<string> _notice;
    private ISmartCleaner? _cleaner;

    /// <summary>Wires Command Mode to the engine's command hotkey.</summary>
    /// <param name="engine">The running engine; its <see cref="DictationEngine.CommandRequested"/> drives this.</param>
    /// <param name="cleaner">The model backend (bundled or Ollama), owned and disposed here.</param>
    /// <param name="injector">Types the rewritten text; null off Windows.</param>
    /// <param name="selectionReader">Reads the selected text; null off Windows.</param>
    /// <param name="notice">A UI-thread-safe way to surface a transient message.</param>
    public CommandModeCoordinator(
        DictationEngine engine,
        ISmartCleaner? cleaner,
        ITextInjector? injector,
        ISelectionReader? selectionReader,
        Action<string> notice)
    {
        _engine = engine;
        _cleaner = cleaner;
        _injector = injector;
        _selectionReader = selectionReader;
        _notice = notice;

        _engine.CommandRequested += OnCommandRequested;
        _engine.IdleUnloaded += OnIdleUnloaded;
    }

    private void OnIdleUnloaded(object? sender, EventArgs e) => _cleaner?.Unload();

    private async void OnCommandRequested(object? sender, string instruction)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(instruction)) return;

            if (_selectionReader is null)
            {
                _notice("Command Mode needs Windows (reading the selection).");
                return;
            }

            var selected = await _selectionReader.ReadSelectedTextAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(selected))
            {
                _notice("Select some text first, then hold the command key and speak.");
                return;
            }

            if (_cleaner is null)
            {
                _notice("No cleanup model is available — check the AI settings.");
                return;
            }

            var rewritten = await _cleaner.TransformAsync(instruction, selected, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(rewritten))
            {
                _notice("Could not rewrite the selection (is the model available?).");
                return;
            }

            if (_injector is null)
            {
                _notice("Command Mode needs Windows (pasting the result).");
                return;
            }

            var ok = await _injector.InjectAsync(rewritten, CancellationToken.None);
            _notice(ok ? $"Command done: {instruction}" : "Could not paste the rewritten text.");
        }
        catch (Exception e)
        {
            _notice($"Command failed: {e.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _engine.CommandRequested -= OnCommandRequested;
        _engine.IdleUnloaded -= OnIdleUnloaded;
        _cleaner?.Dispose();
        _cleaner = null;
    }
}
