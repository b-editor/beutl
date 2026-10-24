namespace Beutl.Serialization;

public sealed class CoreSerializationVisit
{
    internal CoreSerializationVisit(object value, CoreSerializationVisit? parent, string? memberName,
        int? index = null, string? dictionaryKey = null)
    {
        Value = value;
        Parent = parent;
        MemberName = memberName;
        Index = index;
        DictionaryKey = dictionaryKey;
    }

    public object Value { get; }

    public CoreSerializationVisit? Parent { get; }

    public string? MemberName { get; }

    public int? Index { get; }

    public string? DictionaryKey { get; }

    public string Path => Parent is null ? "$"
        : Index is { } index ? $"{Parent.Path}[{index}]"
        : $"{Parent.Path}/{MemberName ?? DictionaryKey}";
}
