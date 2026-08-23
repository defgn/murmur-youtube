using System.Text;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Murmur.Abstractions;

namespace Murmur.Core;

/// <summary>
/// The bundled smart-clean pass: a small instruction model running in-process via
/// llama.cpp, shipped inside the zip. Woffle is then self-contained — no Ollama, no setup.
/// </summary>
/// <remarks>
/// <para>
/// The model (a Qwen2.5 1.5B instruct, Q4_K_M) is loaded lazily on the first dictation and
/// stays resident, like the speech model. The prompt is identical to the Ollama path: fix
/// punctuation and casing, resolve spoken self-corrections and arithmetic, and never invent
/// or drop information.
/// </para>
/// <para>
/// Every failure mode returns null so the caller falls back to the deterministic text:
/// missing model file, load failure, timeout, or a blank response. The first failure is
/// remembered so later dictations do not re-attempt a doomed load.
/// </para>
/// </remarks>
public sealed class BundledCleaner : ISmartCleaner, IDisposable
{
    /// <summary>The GGUF shipped with the app.</summary>
    public const string ModelFileName = "qwen2.5-1.5b-instruct-q4_k_m.gguf";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private LLamaWeights? _model;
    private LLamaContext? _context;
    private StatelessExecutor? _executor;
    private bool _loadFailed;

    /// <summary>Where the bundled model is looked for, in order — mirrors the speech model.</summary>
    public static IEnumerable<string> DefaultSearchPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Woffle", "models", ModelFileName);

        // AppContext.BaseDirectory, not Assembly.Location — the latter returns an empty
        // string in a single-file app, which silently resolves paths against the current
        // directory instead.
        yield return Path.Combine(AppContext.BaseDirectory, "models", ModelFileName);
    }

    /// <summary>Finds the bundled model file, or null when it is not installed.</summary>
    public static string? Locate() => DefaultSearchPaths().FirstOrDefault(File.Exists);

    /// <inheritdoc />
    public async Task<string?> CleanAsync(string text, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                if (!await EnsureLoadedAsync(timeout.Token).ConfigureAwait(false))
                {
                    return null;
                }

                var prompt = BuildPrompt(text);
                var inference = new InferenceParams
                {
                    MaxTokens = 256,
                    SamplingPipeline = new GreedySamplingPipeline(),
                    AntiPrompts = new List<string> { "<|im_end|>" },
                };

                var response = new StringBuilder();
                await foreach (var chunk in _executor!.InferAsync(prompt, inference, timeout.Token)
                                    .ConfigureAwait(false))
                {
                    response.Append(chunk);
                }

                var cleaned = response.ToString().Trim();
                return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or InvalidOperationException)
        {
            // Timeout, missing file, or a model that failed to produce output: fall back.
            return null;
        }
    }

    /// <summary>Loads the model once; remembers failure so later calls short-circuit.</summary>
    private async Task<bool> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_executor is not null) return true;
        if (_loadFailed) return false;

        try
        {
            var path = Locate();
            if (path is null)
            {
                _loadFailed = true;
                return false;
            }

            var parameters = new ModelParams(path)
            {
                ContextSize = 2048,
                GpuLayerCount = 0, // CPU backend; swap the NuGet for CUDA and raise this later
            };

            _model = await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken)
                .ConfigureAwait(false);
            _context = _model.CreateContext(parameters);
            _executor = new StatelessExecutor(_model, parameters);
            return true;
        }
        catch
        {
            _loadFailed = true;
            Dispose();
            return false;
        }
    }

    /// <summary>The Qwen2.5 chat template — identical instructions to the Ollama path.</summary>
    private static string BuildPrompt(string text) =>
        "<|im_start|>system\n" +
        "You clean up dictated transcripts for typing into a document. " +
        "Fix punctuation, capitalization and spacing. Resolve spoken self-corrections " +
        "and arithmetic (\"three potatoes no one potato no three minus one potatoes\" " +
        "becomes \"2 potatoes\"). Remove spoken fillers like um and er. " +
        "Never invent, add or drop information: no new numbers, names or facts. " +
        "Keep the meaning and language exactly. Reply with only the cleaned text." +
        "<|im_end|>\n" +
        "<|im_start|>user\n" + text + "<|im_end|>\n" +
        "<|im_start|>assistant\n";

    /// <inheritdoc />
    public void Dispose()
    {
        _executor = null;
        _context?.Dispose();
        _context = null;
        _model?.Dispose();
        _model = null;
        _gate.Dispose();
    }
}
