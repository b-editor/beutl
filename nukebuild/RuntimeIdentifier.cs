
using System.ComponentModel;

[TypeConverter(typeof(TypeConverter<RuntimeIdentifier>))]
public class RuntimeIdentifier : Enumeration
{
    public static RuntimeIdentifier win_x64 = new()
    {
        Value = "win-x64"
    };

    public static RuntimeIdentifier win_arm64 = new()
    {
        Value = "win-arm64"
    };

    public static RuntimeIdentifier linux_x64 = new()
    {
        Value = "linux-x64"
    };

    public static RuntimeIdentifier linux_arm = new()
    {
        Value = "linux-arm"
    };

    public static RuntimeIdentifier linux_arm64 = new()
    {
        Value = "linux-arm64"
    };

    public static RuntimeIdentifier osx_x64 = new()
    {
        Value = "osx-x64"
    };

    public static RuntimeIdentifier osx_arm64 = new()
    {
        Value = "osx-arm64"
    };

    /// <summary>
    /// True for Windows RIDs (win-x64, win-arm64, ...), which must publish against the
    /// net*-windows TFM rather than the cross-platform one.
    /// </summary>
    public bool IsWindows => Value.StartsWith("win", StringComparison.Ordinal);

    /// <summary>The architecture segment of the RID (e.g. "x64", "arm64").</summary>
    public string Architecture => Value[(Value.IndexOf('-') + 1)..];
}
