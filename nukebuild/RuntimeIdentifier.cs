
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
}