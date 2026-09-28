using System.Runtime.InteropServices;

namespace Beutl.Services;

internal static class FlatpakBundleMetadata
{
    private const string GLib = "libglib-2.0.so.0";

    // Flatpak bundles are OSTree static-delta superblocks. Use the same GVariant
    // type and untrusted-data reader as flatpak_bundle_load(), without importing
    // the bundle into a repository or changing any installed application.
    // https://github.com/flatpak/flatpak/blob/main/common/flatpak-repo-utils.c
    private const string BundleType = "(a{sv}tayay(a{sv}aya(say)sstayay)aya(uayttay)a(yaytt))";

    internal static string? ReadRef(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();

        IntPtr mapped = IntPtr.Zero, bytes = IntPtr.Zero, delta = IntPtr.Zero;
        IntPtr checksum = IntPtr.Zero, metadata = IntPtr.Zero, reference = IntPtr.Zero;
        try
        {
            mapped = g_mapped_file_new(path, 0, IntPtr.Zero);
            if (mapped == IntPtr.Zero) return null;
            bytes = g_mapped_file_get_bytes(mapped);
            delta = g_variant_ref_sink(g_variant_new_from_bytes(BundleType, bytes, 0));
            checksum = g_variant_get_child_value(delta, 3);
            if (g_variant_n_children(checksum) != 32) return null;
            metadata = g_variant_get_child_value(delta, 0);
            reference = g_variant_lookup_value(metadata, "ref", "s");
            if (reference == IntPtr.Zero) return null;
            IntPtr value = g_variant_get_string(reference, out nuint length);
            return length is > 0 and <= 512 ? Marshal.PtrToStringUTF8(value, (int)length) : null;
        }
        finally
        {
            if (reference != IntPtr.Zero) g_variant_unref(reference);
            if (metadata != IntPtr.Zero) g_variant_unref(metadata);
            if (checksum != IntPtr.Zero) g_variant_unref(checksum);
            if (delta != IntPtr.Zero) g_variant_unref(delta);
            if (bytes != IntPtr.Zero) g_bytes_unref(bytes);
            if (mapped != IntPtr.Zero) g_mapped_file_unref(mapped);
        }
    }

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_mapped_file_new([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, int writable, IntPtr error);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_mapped_file_get_bytes(IntPtr file);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_mapped_file_unref(IntPtr file);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_bytes_unref(IntPtr bytes);

    // G_VARIANT_TYPE() represents a GVariantType as its NUL-terminated type string.
    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_variant_new_from_bytes([MarshalAs(UnmanagedType.LPUTF8Str)] string type, IntPtr bytes, int trusted);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_variant_ref_sink(IntPtr value);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_variant_get_child_value(IntPtr value, nuint index);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint g_variant_n_children(IntPtr value);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_variant_lookup_value(IntPtr dictionary,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string key, [MarshalAs(UnmanagedType.LPUTF8Str)] string expectedType);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr g_variant_get_string(IntPtr value, out nuint length);

    [DllImport(GLib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_variant_unref(IntPtr value);
}
