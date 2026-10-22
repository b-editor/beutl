using System.Diagnostics.CodeAnalysis;

namespace Beutl.Serialization;

/// <summary>
/// Gives a name that a type or a property was saved under before it was renamed or moved, so that the files,
/// settings and layouts that hold the former name still load.
/// </summary>
/// <remarks>
/// <para>
/// A former name is read only where the current name is not found. Beutl always writes the current name and does
/// not rewrite a file when it reads one. A build older than the rename does not know the new name: it reads a
/// renamed type as a fallback and does not load a renamed member. Writing a renamed type or member is therefore a
/// breaking change of the format, with or without this attribute.
/// </para>
/// <para>
/// On a type, <see cref="Name"/> is written the way <c>$type</c> held it: the assembly name in brackets, the
/// namespace, a colon and the type name, as in <c>[Old.Assembly]Old.Namespace:OldType</c>. A nested type follows
/// its declaring type after another colon, as in <c>[Old.Assembly]Old.Namespace:Outer:OldType</c>. A namespace
/// that starts with the assembly name may be written relative to it, with a leading period:
/// <c>[Beutl.Embedding.FFmpeg].Decoding:FFmpegDecoderInfo</c> is in the namespace
/// <c>Beutl.Embedding.FFmpeg.Decoding</c>. A generic type is named without type arguments, and its former name
/// has as many type parameters as the type. The former name is read wherever a type name holds it, including as a
/// type argument. The keymap and the extension settings, which are keyed by the namespace and name of an
/// extension's type, also read the entries saved under its former names.
/// </para>
/// <para>
/// On a property, <see cref="Name"/> is the member key the value was saved under. A value found only under a former
/// key loads into the property. This applies to the <c>IProperty</c> properties of every object, together with the
/// animations and expressions of engine objects. A type that reads a member itself in
/// <see cref="ICoreSerializable.Deserialize(ICoreSerializationContext)"/> handles that member's former key there.
/// </para>
/// <para>
/// The attribute may be given once for each former name. A derived type does not inherit the former names of its
/// base type.
/// </para>
/// </remarks>
/// <param name="name">The former name.</param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum
    | AttributeTargets.Property,
    AllowMultiple = true,
    Inherited = false)]
public sealed class FormerPersistedNameAttribute(string name) : Attribute
{
    /// <summary>Gets the former name.</summary>
    public string Name { get; } = name;
}

// Reads a member under a former key that FormerPersistedNameAttribute gives in a property's attributes.
internal static class FormerPersistedNames
{
    // The key a member is read from: its name, or else the first of its former names that context holds.
    internal static string ResolveMemberKey(ICoreSerializationContext context, string name, Attribute[]? attributes)
    {
        if (attributes is null || !HasFormerName(attributes) || context.Contains(name))
        {
            return name;
        }

        foreach (Attribute attribute in attributes)
        {
            if (attribute is FormerPersistedNameAttribute { Name: { Length: > 0 } formerName }
                && context.Contains(formerName))
            {
                return formerName;
            }
        }

        return name;
    }

    // Looks a member up by its name, or else by the first of its former names, in values keyed by member name.
    internal static bool TryGetMember<TValue>(
        IReadOnlyDictionary<string, TValue>? values,
        string name,
        Attribute[]? attributes,
        [MaybeNullWhen(false)] out TValue value)
    {
        value = default;
        if (values is null)
        {
            return false;
        }

        if (values.TryGetValue(name, out value))
        {
            return true;
        }

        if (attributes is not null)
        {
            foreach (Attribute attribute in attributes)
            {
                if (attribute is FormerPersistedNameAttribute { Name: { Length: > 0 } formerName }
                    && values.TryGetValue(formerName, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Whether key is one that TryGetMember looks the member up by: its name or one of its former names.
    internal static bool IsMemberKey(string key, string name, Attribute[]? attributes)
    {
        if (key == name)
        {
            return true;
        }

        if (attributes is not null)
        {
            foreach (Attribute attribute in attributes)
            {
                if (attribute is FormerPersistedNameAttribute { Name: { Length: > 0 } formerName }
                    && formerName == key)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasFormerName(Attribute[] attributes)
    {
        foreach (Attribute attribute in attributes)
        {
            if (attribute is FormerPersistedNameAttribute)
            {
                return true;
            }
        }

        return false;
    }
}
