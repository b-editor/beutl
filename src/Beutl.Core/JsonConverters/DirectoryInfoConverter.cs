namespace Beutl.JsonConverters;

internal class DirectoryInfoConverter : StringJsonConverter<DirectoryInfo>
{
    protected override string TypeName => "DirectoryInfo";
    protected override DirectoryInfo Parse(string s) => new DirectoryInfo(s);
    protected override string Format(DirectoryInfo value) => value.FullName;
}
