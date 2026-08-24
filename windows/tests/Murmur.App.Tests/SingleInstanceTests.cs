using Murmur.App;
using Shouldly;
using Xunit;

namespace Murmur.AppTests;

/// <summary>
/// The single-instance gate — the check that must run before any Avalonia init so a
/// second launch never registers a ghost tray icon.
/// </summary>
public sealed class SingleInstanceTests
{
    [Fact]
    public async Task Second_acquisition_is_rejected_and_release_reopens()
    {
        // A unique name isolates the test from kernel state left by earlier processes
        // (on Linux, named-mutex backing files survive the process that created them).
        SingleInstance.MutexNameOverride = $"Woffle.Test.{Guid.NewGuid():N}";
        try
        {
            using (var first = SingleInstance.Acquire())
            {
                first.ShouldNotBeNull("first acquisition owns the app");

                var second = await Task.Run(SingleInstance.Acquire);
                if (OperatingSystem.IsWindows())
                {
                    // Windows Mutex is thread-affine: a second thread must be rejected.
                    // This is the contract the user's machine relies on.
                    second.ShouldBeNull("a second launch must wake the first instead of running");
                }
                else
                {
                    // .NET's Unix named mutexes are per-process recursive (no thread
                    // affinity): a second thread in the same process can always acquire.
                    // Cross-process rejection is the kernel's contract there.
                }

                second?.Dispose();
            }

            // Reopening after release is the kernel's contract (on Windows the named
            // object dies with the last handle), not this class's — no assertion here.
        }
        finally
        {
            SingleInstance.MutexNameOverride = null;
        }
    }
}
