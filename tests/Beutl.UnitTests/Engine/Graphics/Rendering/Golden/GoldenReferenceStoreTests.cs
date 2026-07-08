using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Covers <see cref="GoldenReferenceStore.FreezeOrAssert"/>'s freeze-on-missing policy (feature 004 review M5):
/// missing references self-heal (freeze) only locally; on CI a missing reference must fail instead of silently
/// passing, so a deleted or renamed reference can never disable its parity gate forever.
/// </summary>
[NonParallelizable]
[TestFixture]
public class GoldenReferenceStoreTests
{
    [Test]
    public void FreezeOrAssert_MissingReference_LocallyFreezes_ButFailsOnCi()
    {
        string? original = Environment.GetEnvironmentVariable("CI");
        const string category = "004-store-test";
        string localName = "local-" + Guid.NewGuid().ToString("N");
        string ciName = "ci-" + Guid.NewGuid().ToString("N");
        string localPath = GoldenReferenceStore.ResolvePath(category, localName);
        string ciPath = GoldenReferenceStore.ResolvePath(category, ciName);
        try
        {
            using Bitmap bmp = new(4, 4, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

            Environment.SetEnvironmentVariable("CI", null);
            bool frozen = GoldenReferenceStore.FreezeOrAssert(category, localName, bmp);
            Assert.Multiple(() =>
            {
                Assert.That(frozen, Is.True, "locally a missing reference is frozen");
                Assert.That(File.Exists(localPath), Is.True, "the frozen blob is written locally");
            });

            Environment.SetEnvironmentVariable("CI", "true");
            Assert.That(
                () => GoldenReferenceStore.FreezeOrAssert(category, ciName, bmp),
                Throws.InstanceOf<Exception>(),
                "on CI a missing reference fails instead of freezing green");
            Assert.That(File.Exists(ciPath), Is.False, "CI must not write a self-healed reference");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", original);
            string dir = Path.GetDirectoryName(localPath)!;
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
