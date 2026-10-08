using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SkiaSharp;

namespace Beutl.Media;

// DirectWrite rasterizes grayscale glyph masks with about 16 coverage levels, so text that scales flickers
// on Windows (b-editor/beutl#2693). Beutl's Windows libSkiaSharp also builds FreeType in, behind the export
// from native/SkiaSharp/freetype-fontmgr.patch, and FreeType draws the same font data as smoothly as on Linux.
// Only drawing moves: FontManager still names and matches fonts through DirectWrite, because FreeType reads
// a few families differently and saved projects refer to fonts by name.
internal static class FreeTypeFonts
{
    public static SKFontManager? Manager { get; } = CreateManager();

    internal static SKFontManager Wrap(nint handle) => GetObject(null, handle);

    private static SKFontManager? CreateManager()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        nint handle;
        try
        {
            handle = CreateFreeTypeFontManager();
        }
        catch (EntryPointNotFoundException)
        {
            // A stock libSkiaSharp, such as the one BenchmarkDotNet's generated project ships.
            return null;
        }

        return handle == 0 ? null : Wrap(handle);
    }

    // SkiaSharp has no public way to wrap a native font manager.
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "GetObject")]
    private static extern SKFontManager GetObject(SKFontManager? type, nint handle);

    [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl,
        EntryPoint = "sk_beutl_fontmgr_create_freetype")]
    private static extern nint CreateFreeTypeFontManager();
}
