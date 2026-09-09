using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Murmur.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Murmur.Platform.Windows;

/// <summary>
/// Continuous loopback capture of a render endpoint, delivering 16 kHz mono float.
/// </summary>
/// <remarks>
/// <para>
/// The interviewer side of a call arrives at the PC through the output the user is
/// listening on; WASAPI loopback taps that render mix without touching exclusive mode or
/// disturbing playback. Shaped as <see cref="IAudioCapture"/> so the same utterance
/// machinery that serves the microphone feed serves this one, and so the device can be
/// swapped live exactly like the mic (the id is read when capture starts).
/// </para>
/// <para>
/// Kept logic-free per the platform rule: this file moves audio, nothing else. Utterance
/// segmentation, transcription and cleanup live in <c>Murmur.Core</c>.
/// </para>
/// </remarks>
public sealed class WasapiLoopbackAudioCapture : IAudioCapture
{
    private const int BufferMilliseconds = 50;

    private WasapiLoopbackCapture? _capture;
    private Channel<float[]>? _channel;
    private BufferedWaveProvider? _rawSink;
    private WdlResamplingSampleProvider? _pipeline;
    private float[] _pullBuffer = [];

    /// <summary>Captures from a specific render endpoint, or the default when null.</summary>
    public WasapiLoopbackAudioCapture(string? deviceId = null) => DeviceId = deviceId;

    /// <inheritdoc />
    public string? DeviceId { get; set; }

    /// <inheritdoc />
    public float Gain { get; set; } = 1f;

    /// <inheritdoc />
    public bool IsCapturing { get; private set; }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StartCapture();

        try
        {
            await foreach (var buffer in _channel!.Reader
                .ReadAllAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                yield return new AudioChunk(buffer);
            }
        }
        finally
        {
            StopCapture();
        }
    }

    private void StartCapture()
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = DeviceId is null
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console)
            : enumerator.GetDevice(DeviceId);

        _channel = Channel.CreateBounded<float[]>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });

        // Loopback always delivers the engine mix format (IEEE float, the render device's
        // channel count) — there is no point negotiating a custom format first, so straight
        // to the managed converter: downmix, then resample to the model's rate.
        var capture = new WasapiLoopbackCapture(device)
        {
            ShareMode = AudioClientShareMode.Shared,
        };
        capture.DataAvailable += OnDataAvailable;
        capture.RecordingStopped += OnRecordingStopped;

        BuildConverter(capture.WaveFormat);

        _capture = capture;
        capture.StartRecording();
        IsCapturing = true;
    }

    private void BuildConverter(WaveFormat source)
    {
        _rawSink = new BufferedWaveProvider(source)
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true,
        };

        ISampleProvider provider = _rawSink.ToSampleProvider();
        if (source.Channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider) { LeftVolume = 0.5f, RightVolume = 0.5f };
        }
        else if (source.Channels > 2)
        {
            provider = new DownmixSampleProvider(provider);
        }

        _pipeline = new WdlResamplingSampleProvider(provider, AudioChunk.SampleRate);
        _pullBuffer = new float[AudioChunk.SampleRate / 10];
    }

    /// <summary>
    /// Runs on NAudio's dedicated capture thread — never the UI thread. The buffer is
    /// reused per callback; copy before handing downstream.
    /// </summary>
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;

        _rawSink!.AddSamples(e.Buffer, 0, e.BytesRecorded);   // AddSamples copies internally

        while (true)
        {
            var read = _pipeline!.Read(_pullBuffer, 0, _pullBuffer.Length);
            if (read == 0) return;

            var owned = new float[read];
            if (Gain != 1f)
            {
                for (var i = 0; i < read; i++)
                {
                    var scaled = _pullBuffer[i] * Gain;
                    owned[i] = scaled > 1f ? 1f : scaled < -1f ? -1f : scaled;
                }
            }
            else
            {
                Array.Copy(_pullBuffer, owned, read);
            }

            _channel?.Writer.TryWrite(owned);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // A non-null exception here is usually AUDCLNT_E_DEVICE_INVALIDATED — the output
        // device changed or vanished (unplugged headset, switched default). Completing the
        // channel ends this capture; the host reports the fault and restarts on the new id.
        _channel?.Writer.TryComplete(e.Exception);
        IsCapturing = false;
    }

    private void StopCapture()
    {
        if (_capture is null) return;

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        try { _capture.StopRecording(); } catch (COMException) { /* already gone */ }
        _capture.Dispose();
        _capture = null;

        _channel?.Writer.TryComplete();
        IsCapturing = false;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        StopCapture();
        return ValueTask.CompletedTask;
    }
}
