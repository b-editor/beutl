namespace Beutl.Serialization;

/// <summary>
/// Names member keys that a type drops when it reads them, instead of keeping them as unknown members.
/// </summary>
/// <remarks>
/// <para>
/// Reading an object keeps every member of its JSON that the object did not read, and writing the object
/// writes those members back, so data that a newer version of Beutl or of an extension wrote survives an
/// older one. A key named here is not kept: it holds data that the type has replaced or no longer needs,
/// such as a format it migrates from. A key the type reads is never kept either, so a migration that reads
/// the old key needs no entry here.
/// </para>
/// <para>
/// The attribute applies to derived types too. A name listed here should not be used again for a member
/// with another meaning, since builds that know the attribute drop it.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DroppedMembersAttribute(params string[] names) : Attribute
{
    /// <summary>Gets the member keys that the type drops.</summary>
    public IReadOnlyList<string> Names { get; } = names;
}
