using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Beutl.UnitTests.Core;

[TestFixture]
public class FilePathComparisonMacTests
{
    [Test]
    public void Existing_paths_resolve_without_listing_siblings_on_macOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Uses Darwin's entry-name metadata query.");
        }

        string root = Path.Combine(Path.GetTempPath(), $"beutl-native-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Target", "Child"));
        string file = Path.Combine(root, "Target", "Café.bin");
        File.WriteAllText(file, "content");
        Directory.CreateSymbolicLink(Path.Combine(root, "alias"), Path.Combine(root, "Target", "Child"));
        try
        {
            var context = new FilePathComparison.ResolutionContext(
                _ => throw new AssertionException("An existing native entry must not enumerate its siblings."),
                useNativeEntryNames: true);
            string expected = Path.Combine(context.ResolveCanonicalPath(root), "Target", "Café.bin");
            Assert.Multiple(() =>
            {
                Assert.That(context.ResolveCanonicalPath(file), Is.EqualTo(expected));
                Assert.That(context.ResolveCanonicalPath(Path.Combine(root, "alias", "..", "Café.bin")), Is.EqualTo(expected));
                Assert.That(context.ResolveCanonicalPath(Path.Combine(root, "missing", "Untouched")),
                    Is.EqualTo(Path.Combine(context.ResolveCanonicalPath(root), "missing", "Untouched")));
            });

            string alternate = Path.Combine(root, "Target", "CAFÉ.BIN".Normalize(NormalizationForm.FormD));
            if (File.Exists(alternate))
            {
                Assert.That(context.ResolveCanonicalPath(alternate), Is.EqualTo(expected));
            }

            string hardLink = Path.Combine(root, "Target", "another.bin");
            using var process = Process.Start(new ProcessStartInfo("/bin/ln")
            {
                UseShellExecute = false,
                ArgumentList = { file, hardLink },
            })!;
            process.WaitForExit();
            Assert.That(process.ExitCode, Is.Zero);
            Assert.That(context.ResolveCanonicalPath(hardLink),
                Is.EqualTo(Path.Combine(context.ResolveCanonicalPath(root), "Target", "another.bin")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Native_name_buffer_decodes_utf8_relative_to_the_attribute_reference()
    {
        Assert.That(FilePathComparison.DecodeMacEntryName(CreateBuffer("Café.bin")), Is.EqualTo("Café.bin"));
    }

    [TestCase("negative-offset")]
    [TestCase("inside-header")]
    [TestCase("oversized-length")]
    [TestCase("oversized-buffer")]
    [TestCase("missing-terminator")]
    [TestCase("invalid-utf8")]
    [TestCase("empty")]
    [TestCase("embedded-null")]
    [TestCase("separator")]
    [TestCase("parent")]
    public void Invalid_native_name_buffers_are_rejected(string kind)
    {
        byte[] buffer = CreateBuffer(kind switch
        {
            "empty" => "",
            "embedded-null" => "a\0b",
            "separator" => "a/b",
            "parent" => "..",
            _ => "filename",
        });
        switch (kind)
        {
            case "negative-offset": BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), -1); break;
            case "inside-header": BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 0); break;
            case "oversized-length": BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), uint.MaxValue); break;
            case "oversized-buffer": BinaryPrimitives.WriteUInt32LittleEndian(buffer, uint.MaxValue); break;
            case "missing-terminator": buffer[^1] = 1; break;
            case "invalid-utf8": buffer[12] = 0xff; break;
        }

        Assert.That(FilePathComparison.DecodeMacEntryName(buffer), Is.Null);
    }

    private static byte[] CreateBuffer(string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        byte[] buffer = new byte[13 + bytes.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)buffer.Length);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), (uint)bytes.Length + 1);
        bytes.CopyTo(buffer, 12);
        return buffer;
    }
}
