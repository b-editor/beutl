using Beutl.Evidence;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Evidence;

/// <summary>
/// Pins what the fingerprint promises: that two runs sharing a comparability key really did share the
/// conditions that change what the renderer produces, and that a run which could not say so is rejected.
/// </summary>
[TestFixture]
public sealed class RenderEvidenceFingerprintTests
{
    [Test]
    public void ComparabilityKey_IsStableForEqualFingerprints()
    {
        Assert.That(
            Sample().ComparabilityKey,
            Is.EqualTo(Sample().ComparabilityKey));
    }

    [TestCase(nameof(RenderEvidenceFingerprint.VulkanDeviceUuid))]
    [TestCase(nameof(RenderEvidenceFingerprint.VulkanDriverVersionRaw))]
    [TestCase(nameof(RenderEvidenceFingerprint.MaxAttachmentDimension))]
    [TestCase(nameof(RenderEvidenceFingerprint.OsDescription))]
    [TestCase(nameof(RenderEvidenceFingerprint.BuildConfiguration))]
    [TestCase(nameof(RenderEvidenceFingerprint.SkiaBackend))]
    [TestCase(nameof(RenderEvidenceFingerprint.VulkanEnabledExtensions))]
    public void ComparabilityKey_ChangesWhenAConditionThatChangesOutputChanges(string field)
    {
        RenderEvidenceFingerprint changed = field switch
        {
            nameof(RenderEvidenceFingerprint.VulkanDeviceUuid) =>
                Sample() with { VulkanDeviceUuid = "ffffffffffffffffffffffffffffffff" },
            nameof(RenderEvidenceFingerprint.VulkanDriverVersionRaw) =>
                Sample() with { VulkanDriverVersionRaw = "2" },
            nameof(RenderEvidenceFingerprint.MaxAttachmentDimension) =>
                Sample() with { MaxAttachmentDimension = 8192 },
            nameof(RenderEvidenceFingerprint.OsDescription) =>
                Sample() with { OsDescription = "Another OS" },
            nameof(RenderEvidenceFingerprint.BuildConfiguration) =>
                Sample() with { BuildConfiguration = "Debug" },
            nameof(RenderEvidenceFingerprint.SkiaBackend) =>
                Sample() with { SkiaBackend = "Metal" },
            _ => Sample() with { VulkanEnabledExtensions = ["VK_KHR_surface", "VK_KHR_swapchain"] },
        };

        Assert.Multiple(() =>
        {
            Assert.That(changed.ComparabilityKey, Is.Not.EqualTo(Sample().ComparabilityKey));
            Assert.That(Sample().IsComparableTo(changed), Is.False);
        });
    }

    [Test]
    public void ComparabilityKey_IgnoresTheEngineBuildBeingCompared()
    {
        // A paired run compares two engine builds on one machine, so the engine's own version must not be the
        // thing that makes the two sides look incomparable.
        RenderEvidenceFingerprint other = Sample() with
        {
            BeutlEngineAssemblyVersion = "2.99.99+ffffffffffffffffffffffffffffffffffffffff",
            BeutlEngineSourceRevision = "ffffffffffffffffffffffffffffffffffffffff",
        };
        Assert.That(Sample().IsComparableTo(other), Is.True);
    }

    [Test]
    public void IsComparableTo_RejectsNull()
        => Assert.That(Sample().IsComparableTo(null), Is.False);

    [Test]
    public void Validate_RejectsABlankOrUnknownField()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(Sample() with { VulkanDeviceName = "  " }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("VulkanDeviceName"));
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(Sample() with { VulkanDriverName = "unknown" }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("VulkanDriverName"));
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(Sample() with { VulkanEnabledExtensions = [] }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("VulkanEnabledExtensions"));
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(Sample() with { MaxAttachmentDimension = 0 }),
                Throws.InstanceOf<InvalidOperationException>().With.Message.Contains("MaxAttachmentDimension"));
        });
    }

    [Test]
    public void Validate_ToleratesABlankDriverInfoOnASoftwareRasterizerOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(
                    Sample() with { VulkanDeviceType = "Cpu", VulkanDriverInfo = string.Empty }),
                Throws.Nothing,
                "a software rasterizer reporting no driver build is the device being honest");
            Assert.That(
                () => RenderEvidenceFingerprint.Validate(Sample() with { VulkanDriverInfo = string.Empty }),
                Throws.InstanceOf<InvalidOperationException>(),
                "a hardware driver may not hide behind a blank identity");
            Assert.That(EvidenceFingerprintRules.IsCpuDevice("cpu"), Is.True);
            Assert.That(
                EvidenceFingerprintRules.AllowsBlankValue("VulkanDeviceName", "Cpu"),
                Is.False,
                "only the driver-info field is excused");
        });
    }

    [Test]
    public void Validate_AcceptsACompleteFingerprint()
        => Assert.That(() => RenderEvidenceFingerprint.Validate(Sample()), Throws.Nothing);

    [Test]
    public void ExtractSourceRevision_FindsTheCommitInAnInformationalVersion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                RenderEvidenceFingerprint.ExtractSourceRevision(
                    "2.99.99+0123456789ABCDEF0123456789abcdef01234567"),
                Is.EqualTo("0123456789abcdef0123456789abcdef01234567"));
            Assert.That(
                RenderEvidenceFingerprint.ExtractSourceRevision("2.99.99"),
                Is.EqualTo("no-source-revision"),
                "a build that stamped no revision is a fact to record, not a capture failure");
        });
    }

    [Test]
    public void DecodeVulkanVersion_SplitsThePackedMajorMinorPatch()
        => Assert.That(
            RenderEvidenceFingerprint.DecodeVulkanVersion((1u << 22) | (2u << 12) | 323u),
            Is.EqualTo("1.2.323"));

    [Test]
    public void TryCapture_ReportsWhyItCouldNotCaptureRatherThanThrowing()
    {
        RenderEvidenceFingerprint? fingerprint = RenderEvidenceFingerprint.TryCapture(null, out string? reason);
        Assert.Multiple(() =>
        {
            Assert.That(fingerprint, Is.Null);
            Assert.That(reason, Is.Not.Null.And.Not.Empty);
        });
    }

    private static RenderEvidenceFingerprint Sample() => PairedBenchmarkAnalyzerTests.TestFingerprint();
}
