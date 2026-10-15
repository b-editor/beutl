namespace Beutl.Converters;

internal sealed class ParsableJsonConverter<T> : StringJsonConverter<T>
    where T : IParsable<T>
{
    protected override string TypeName => typeof(T).Name;

    protected override T Parse(string s) => T.Parse(s, null);
}
