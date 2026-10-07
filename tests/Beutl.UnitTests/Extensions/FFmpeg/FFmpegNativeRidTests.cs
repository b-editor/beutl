using System.Runtime.InteropServices;

using Beutl.Extensions.FFmpeg;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegNativeRidTests
{
    [TestCase(Architecture.Arm64, "win-arm64")]
    [TestCase(Architecture.X86, "win-x86")]
    [TestCase(Architecture.X64, "win-x64")]
    public void GetWindowsRid_MapsArchitectureToRuntimeFolder(Architecture architecture, string expected)
    {
        Assert.That(FFmpegNativeRid.GetWindowsRid(architecture), Is.EqualTo(expected));
    }

    [TestCase(Architecture.Wasm)]
    [TestCase(Architecture.Arm)] // 32-bit ARM; win-arm is not a supported target
    public void GetWindowsRid_UnknownArchitecture_FallsBackToX64(Architecture architecture)
    {
        // Anything that is neither arm64 nor x86 must resolve to win-x64 so a 64-bit
        // x64 process never probes the win-x86 folder.
        Assert.That(FFmpegNativeRid.GetWindowsRid(architecture), Is.EqualTo("win-x64"));
    }

    [TestCase(Architecture.X64, "x86_64-linux-gnu")]
    [TestCase(Architecture.Arm64, "aarch64-linux-gnu")]
    [TestCase(Architecture.X86, "i386-linux-gnu")]
    [TestCase(Architecture.Arm, "arm-linux-gnueabihf")]
    public void GetLinuxMultiarchDirectory_MapsArchitectureToDebianTriplet(Architecture architecture, string expected)
    {
        Assert.That(FFmpegNativeRid.GetLinuxMultiarchDirectory(architecture), Is.EqualTo(expected));
    }

    [TestCase(Architecture.Wasm)]
    public void GetLinuxMultiarchDirectory_UnknownArchitecture_HasNone(Architecture architecture)
    {
        Assert.That(FFmpegNativeRid.GetLinuxMultiarchDirectory(architecture), Is.Null);
    }
}
