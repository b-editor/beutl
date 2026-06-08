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

    [Test]
    public void GetWindowsRid_UnknownArchitecture_FallsBackToX64()
    {
        // Anything that is neither arm64 nor x86 must resolve to win-x64 so a 64-bit
        // x64 process never probes the win-x86 folder.
        Assert.That(FFmpegNativeRid.GetWindowsRid(Architecture.Wasm), Is.EqualTo("win-x64"));
    }
}
