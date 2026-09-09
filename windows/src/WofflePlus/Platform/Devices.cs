using System.Reflection;
using Murmur.Abstractions;

namespace WofflePlus.Platform;

/// <summary>
/// Loads the Windows platform layer by reflection (same pattern as dictation-Woffle) and
/// exposes the device enumeration Woffle+ needs.
/// </summary>
internal static class Devices
{
    private const string AssemblyName = "Murmur.Platform.Windows";
    private const string Namespace = "Murmur.Platform.Windows";

    private static System.Reflection.Assembly? _assembly;
    private static bool _attempted;

    /// <summary>Whether the platform layer loaded (false on non-Windows).</summary>
    public static bool IsAvailable => Load() is not null;

    /// <summary>Teaches the load context to find the platform assembly beside the exe.</summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Platform assembly ships whole beside the executable.")]
    public static void InstallResolver()
    {
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var candidate = System.IO.Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
    }

    /// <summary>Capture (microphone) devices for the picker.</summary>
    public static List<AudioDeviceInfo> ListCaptureDevices() =>
        InvokeList("ListInputDevices");

    /// <summary>Render (output) devices for the interviewer's loopback feed.</summary>
    public static List<AudioDeviceInfo> ListRenderDevices() =>
        InvokeList("ListRenderDevices");

    /// <summary>Creates the mic capture, or a null stand-in off Windows.</summary>
    public static IAudioCapture CreateCapture(string? deviceId)
    {
        var real = CreateCaptureInternal(deviceId);
        return real ?? new InertCapture();
    }

    /// <summary>Creates the loopback capture, or a null stand-in off Windows.</summary>
    public static IAudioCapture CreateLoopback(string? deviceId)
    {
        var real = CreateLoopbackInternal(deviceId);
        return real ?? new InertCapture();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Platform assembly ships whole and is never trimmed.")]
    private static IAudioCapture? CreateCaptureInternal(string? deviceId)
    {
        var type = Load()?.GetType($"{Namespace}.WasapiAudioCapture");
        if (type is null) return null;
        try { return Activator.CreateInstance(type, [deviceId]) as IAudioCapture; }
        catch (Exception e) when (e is MissingMethodException or TargetInvocationException) { return null; }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Platform assembly ships whole and is never trimmed.")]
    private static IAudioCapture? CreateLoopbackInternal(string? deviceId)
    {
        var type = Load()?.GetType($"{Namespace}.WasapiLoopbackAudioCapture");
        if (type is null) return null;
        try { return Activator.CreateInstance(type, [deviceId]) as IAudioCapture; }
        catch (Exception e) when (e is MissingMethodException or TargetInvocationException) { return null; }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2075:DynamicallyAccessedMembers",
        Justification = "Platform assembly ships whole and is never trimmed.")]
    private static List<AudioDeviceInfo> InvokeList(string methodName)
    {
        var type = Load()?.GetType($"{Namespace}.WasapiDevices");
        var method = type?.GetMethod(methodName, Type.EmptyTypes);
        if (method is null) return [];
        try { return (method.Invoke(null, null) as IReadOnlyList<AudioDeviceInfo> ?? []).ToList(); }
        catch (Exception e) when (e is TargetInvocationException or MemberAccessException) { return []; }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Platform assembly ships whole and is never trimmed.")]
    private static System.Reflection.Assembly? Load()
    {
        if (_attempted) return _assembly;
        _attempted = true;
        try { _assembly = System.Reflection.Assembly.Load(AssemblyName); }
        catch (Exception e) when (e is FileNotFoundException or BadImageFormatException) { _assembly = null; }
        return _assembly;
    }
}

/// <summary>
/// Stand-in capture for non-Windows hosts: yields nothing, ends immediately, so the UI
/// still runs and the session reports no audio instead of crashing.
/// </summary>
internal sealed class InertCapture : IAudioCapture
{
    /// <inheritdoc />
    public bool IsCapturing => false;

    /// <inheritdoc />
    public string? DeviceId { get; set; }

    /// <inheritdoc />
    public float Gain { get; set; } = 1f;

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioChunk> CaptureAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
