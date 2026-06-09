using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Mutations on the <c>EngineObject</c> list an <see cref="Element"/> owns —
/// Add / Insert / Move / Remove / PasteOver / SetEnabled. Centralizes what was
/// scattered across the property ViewModels so the index-validation, idempotency,
/// and paste-failure contracts can be unit-tested in one place.
/// </summary>
public interface IElementObjectService
{
    /// <summary>Appends <paramref name="obj"/> to
    /// <paramref name="element"/>.<c>Objects</c>. Commits <c>AddObject</c>.</summary>
    void Add(Element element, EngineObject obj);

    /// <summary>Inserts <paramref name="obj"/> at <paramref name="index"/>, clamped
    /// to <c>[0, Count]</c>. Commits <c>AddObject</c>.</summary>
    void InsertAt(Element element, int index, EngineObject obj);

    /// <summary>Removes <paramref name="obj"/>. Returns false and commits nothing
    /// when it is not in the list.</summary>
    bool Remove(Element element, EngineObject obj);

    /// <summary>Reorders <paramref name="oldIndex"/> to <paramref name="newIndex"/>.
    /// Returns false (no-op) when they are equal; commits <c>MoveObject</c> otherwise.</summary>
    bool Move(Element element, int oldIndex, int newIndex);

    /// <summary>Replaces <c>Objects[index]</c> with an object materialized from the
    /// clipboard JSON. Returns <see cref="ObjectPasteOutcome.InvalidJson"/> when the
    /// payload does not parse, <see cref="ObjectPasteOutcome.MissingType"/> when the
    /// $type discriminator is absent or does not resolve to an
    /// <see cref="EngineObject"/>. Commits <c>PasteObject</c> only on success.</summary>
    ObjectPasteOutcome PasteOver(Element element, int index, string json);

    /// <summary>Idempotent boolean write: skips the commit when
    /// <paramref name="obj"/>.<c>IsEnabled</c> already matches
    /// <paramref name="isEnabled"/>.</summary>
    bool SetEnabled(EngineObject obj, bool isEnabled);
}

public enum ObjectPasteOutcome
{
    InvalidJson,
    MissingType,
    UnexpectedError,
    Pasted,
}
