using System.Runtime.InteropServices;
using Murmur.Abstractions;
using NAudio.CoreAudioApi;

namespace Murmur.Platform.Windows;

/// <summary>
/// Enumerates capture devices for the Settings screen.
/// </summary>
/// <remarks>
/// Lives here rather than in <c>Murmur.App</c> because listing devices is Win32-adjacent
/// (COM, through NAudio's MMDeviceEnumerator) — the same reason the capture itself does.
/// Loaded by reflection from the UI project so it stays on plain <c>net10.0</c>.
/// </remarks>
public static class WasapiDevices
{
    /// <summary>
    /// Lists capture endpoints in a stable, user-friendly order: the OS default first,
    /// then everything else alphabetically.
    /// </summary>
    public static IReadOnlyList<AudioDeviceInfo> ListInputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultId = DefaultCaptureId(enumerator);

        // MMDevice implements IDisposable; drop the COM references we're done with.
        var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        try
        {
            return endpoints
                .Select(d => new AudioDeviceInfo(
                    d.ID,
                    FriendlyName(d),
                    string.Equals(d.ID, defaultId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(d => d.IsDefault)
                .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally
        {
            foreach (var endpoint in endpoints) endpoint.Dispose();
        }
    }

    /// <summary>The id of the OS default capture endpoint, or null.</summary>
    /// <remarks>
    /// Matches the capture path's choice of <see cref="Role.Communications"/>: the device
    /// the user expects as "my default mic" is the communications one, and the app follows
    /// it — so the picker should mark the same device as default.
    /// </remarks>
    private static string? DefaultCaptureId(MMDeviceEnumerator enumerator)
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return device.ID;
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            // No default endpoint at all (headless VM, all devices disabled).
            return null;
        }
    }

    /// <summary>
    /// A readable name. <c>FriendlyName</c> is usually enough; for Bluetooth and USB devices
    /// it can be a bare interface string, and the endpoint's own <c>PropertyStore</c> name
    /// is the better label.
    /// </summary>
    private static string FriendlyName(MMDevice device)
    {
        try
        {
            var friendly = device.FriendlyName;
            if (!string.IsNullOrWhiteSpace(friendly)) return friendly;

            var fromStore = device.Properties[PropertyKeys.PKEY_Device_FriendlyName];
            return fromStore.Value?.ToString() ?? "(Unnamed device)";
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return "(Unnamed device)";
        }
    }
}
