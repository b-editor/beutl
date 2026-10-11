using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

public sealed class ExtensionConfig : ConfigurationBase
{
    public ExtensionConfig()
    {
        EditorExtensions.CollectionChanged += (_, _) => OnChanged();
        DecoderPriority.CollectionChanged += (_, _) => OnChanged();
    }

    public struct TypeLazy(string formattedTypeName)
    {
        private Type? _type = null;

        public string FormattedTypeName { get; init; } = formattedTypeName;

        public Type? Type => _type ??= TypeFormat.ToType(FormattedTypeName);
    }

    // Keyには拡張子を含める
    public CoreDictionary<string, ICoreList<TypeLazy>> EditorExtensions { get; } = [];

    // Keyには拡張子を含める
    public CoreList<TypeLazy> DecoderPriority { get; } = [];

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);

        context.SetValue(nameof(EditorExtensions), EditorExtensions
            .ToDictionary(x => x.Key, y => y.Value.Select(z => z.FormattedTypeName).ToArray()));

        context.SetValue(nameof(DecoderPriority), DecoderPriority.Select(v => v.FormattedTypeName).ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (TryReadValue(context, nameof(EditorExtensions), out Dictionary<string, string[]>? ee))
        {
            EditorExtensions.Clear();
            if (ee != null)
            {
                foreach (KeyValuePair<string, string[]> item in ee)
                {
                    EditorExtensions.Add(item.Key, new CoreList<TypeLazy>(item.Value
                        .Select(str => new TypeLazy(str))
                        .Where(type => type.FormattedTypeName != null)));
                }
            }
        }

        if (TryReadValue(context, nameof(DecoderPriority), out string?[]? dp))
        {
            DecoderPriority.Clear();
            if (dp != null)
            {
                DecoderPriority.AddRange(dp
                    .Where(v => v != null)
                    .Select(v => new TypeLazy(v!)));
            }
        }
    }
}
