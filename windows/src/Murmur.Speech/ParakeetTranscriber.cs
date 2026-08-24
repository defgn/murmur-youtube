using Murmur.Abstractions;
using SherpaOnnx;

namespace Murmur.Speech;

/// <summary>
/// The bundled speech model, via sherpa-onnx's offline recognizer. The model is an NVIDIA
/// Parakeet export, quantized to int8 so it runs ~40× faster than real time on four
/// threads.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which model:</b> two Parakeet exports are shipped and auto-detected from the model
/// directory's layout. The accurate one is the TDT 0.6B (encoder/decoder/joiner + 128
/// feature dims, ~660 MB). The compact one is the TDT-CTC 110M (a single
/// <c>model.int8.onnx</c>, 80 feature dims, ~126 MB, ~4× faster) — a genuine memory/speed
/// option for machines that do not want half a gigabyte resident.
/// </para>
/// <para>
/// <b>CPU only, deliberately.</b> sherpa-onnx ships no GPU package at all — setting a CUDA
/// provider silently falls back to CPU. DirectML is several releases behind, forbids parallel
/// inference, and wants fixed tensor shapes, which a variable-length audio model cannot
/// provide. With int8 weights on four threads this runs roughly 40× faster than real time,
/// so none of that is worth the dependency.
/// </para>
/// <para>
/// <b>Biasing is not supported by this engine.</b> sherpa-onnx's offline recogniser has no
/// contextual-strings equivalent to Apple's <c>AnalysisContext</c>, so the dictionary's
/// correction pass carries the whole job on Windows. That pass was always the guarantee and
/// biasing only ever the nudge, so behaviour matches — the Windows build simply has one fewer
/// chance to get a name right before correction.
/// </para>
/// </remarks>
public sealed class ParakeetTranscriber : ITranscriber
{
    /// <summary>Threads for inference. Four measured fastest; eight measured slower.</summary>
    private const int Threads = 4;

    /// <summary>The TDT 0.6B model folder name.</summary>
    public const string AccurateFolder = "parakeet-v2";

    /// <summary>The CTC 110M model folder name.</summary>
    public const string CompactFolder = "parakeet-compact";

    private readonly string _modelDirectory;
    private OfflineRecognizer? _recognizer;

    /// <summary>Points the engine at a folder of model files.</summary>
    /// <param name="modelDirectory">
    /// Must contain either the TDT 0.6B files (<c>encoder.int8.onnx</c>,
    /// <c>decoder.int8.onnx</c>, <c>joiner.int8.onnx</c>, <c>tokens.txt</c>) or the CTC 110M
    /// files (<c>model.int8.onnx</c>, <c>tokens.txt</c>). The layout selects the config.
    /// </param>
    public ParakeetTranscriber(string modelDirectory) => _modelDirectory = modelDirectory;

    /// <summary>Where the models are looked for, in order.</summary>
    /// <remarks>
    /// <c>%LOCALAPPDATA%</c> first: it needs no administrator rights, so the app can download
    /// and update the model itself even when installed under Program Files.
    /// </remarks>
    public static IEnumerable<string> DefaultSearchPaths(string folderName)
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Woffle", "models", folderName);

        // AppContext.BaseDirectory, not Assembly.Location — the latter returns an empty
        // string in a single-file app, which silently resolves paths against the current
        // directory instead.
        yield return Path.Combine(AppContext.BaseDirectory, "models", folderName);
    }

    /// <summary>
    /// Finds the first complete model directory, preferring the accurate layout, or null.
    /// </summary>
    public static string? Locate() =>
        DefaultSearchPaths(AccurateFolder).FirstOrDefault(IsComplete)
        ?? DefaultSearchPaths(CompactFolder).FirstOrDefault(IsComplete);

    /// <summary>
    /// Finds a complete directory for <paramref name="folderName"/> only, or null.
    /// </summary>
    public static string? Locate(string folderName) =>
        DefaultSearchPaths(folderName).FirstOrDefault(IsComplete);

    /// <summary>Whether <paramref name="directory"/> holds every required file.</summary>
    /// <remarks>
    /// Worth checking before loading: a truncated download fails with an opaque protobuf
    /// parse error that reads like a corrupt build rather than a missing byte range.
    /// </remarks>
    public static bool IsComplete(string directory) =>
        (RequiredTdtFiles.All(f => File.Exists(Path.Combine(directory, f)))
         || RequiredCtcFiles.All(f => File.Exists(Path.Combine(directory, f))))
        && File.Exists(Path.Combine(directory, "tokens.txt"));

    /// <summary>The files the accurate (TDT) layout needs.</summary>
    public static IReadOnlyList<string> RequiredTdtFiles { get; } =
    [
        "encoder.int8.onnx",
        "decoder.int8.onnx",
        "joiner.int8.onnx",
        "tokens.txt",
    ];

    /// <summary>The files the compact (CTC) layout needs.</summary>
    public static IReadOnlyList<string> RequiredCtcFiles { get; } =
    [
        "model.int8.onnx",
        "tokens.txt",
    ];

    /// <inheritdoc />
    public bool IsReady => _recognizer is not null;

    /// <inheritdoc />
    public ValueTask<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (_recognizer is not null) return ValueTask.FromResult(true);
        if (!IsComplete(_modelDirectory)) return ValueTask.FromResult(false);

        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = AudioChunk.SampleRate;
        config.ModelConfig.NumThreads = Threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        // The layout IS the model identity: the TDT transducer needs three ONNX files and
        // 128 feature dims; the CTC export is a single file and 80 dims. Mixing them is the
        // classic "got invalid dimensions for input" failure.
        if (File.Exists(Path.Combine(_modelDirectory, "encoder.int8.onnx")))
        {
            config.FeatConfig.FeatureDim = 128;
            config.ModelConfig.Transducer.Encoder = Path.Combine(_modelDirectory, "encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = Path.Combine(_modelDirectory, "decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = Path.Combine(_modelDirectory, "joiner.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(_modelDirectory, "tokens.txt");
            config.ModelConfig.ModelType = "nemo_transducer";
        }
        else
        {
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.NeMoCtc.Model = Path.Combine(_modelDirectory, "model.int8.onnx");
            config.ModelConfig.Tokens = Path.Combine(_modelDirectory, "tokens.txt");
            config.ModelConfig.ModelType = "nemo_ctc";
        }

        _recognizer = new OfflineRecognizer(config);
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="biasPhrases"/> is accepted and ignored — see the class remarks. Audio
    /// longer than the encoder can handle is the caller's problem; <c>AudioSegmenter</c>
    /// splits it before this is reached.
    /// </remarks>
    public ValueTask<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        IReadOnlyList<string> biasPhrases,
        CancellationToken cancellationToken)
    {
        if (_recognizer is null || samples.Length == 0) return ValueTask.FromResult(string.Empty);

        cancellationToken.ThrowIfCancellationRequested();

        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(AudioChunk.SampleRate, samples.ToArray());
        _recognizer.Decode(stream);

        return ValueTask.FromResult(stream.Result.Text?.Trim() ?? string.Empty);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _recognizer?.Dispose();
        _recognizer = null;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UnloadAsync()
    {
        // Same as DisposeAsync minus the interface's own expectations: dispose the native
        // recognizer (which frees the model memory) and allow a reload later.
        _recognizer?.Dispose();
        _recognizer = null;
        return ValueTask.CompletedTask;
    }
}
