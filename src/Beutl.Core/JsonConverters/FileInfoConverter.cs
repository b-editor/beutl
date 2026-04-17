namespace Beutl.JsonConverters;

internal class FileInfoConverter : StringJsonConverter<FileInfo>
{
    protected override string TypeName => "FileInfo";
    protected override FileInfo Parse(string s) => new FileInfo(s);
    protected override string Format(FileInfo value) => value.FullName;
}
