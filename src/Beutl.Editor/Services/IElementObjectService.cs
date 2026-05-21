using Beutl.Engine;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Mutations on the <c>EngineObject</c> list that an <see cref="Element"/>
/// owns — Add / Insert / Move / Remove / PasteOver / SetEnabled. These
/// operations were scattered across <c>ElementPropertyTabView</c>,
/// <c>EngineObjectPropertyView</c>, and <c>EngineObjectPropertyViewModel</c>
/// with no shared invariant guard. Centralizing them allows a single set
/// of unit tests to lock in the index-validation, idempotency, and
/// paste-failure contracts.
/// </summary>
public interface IElementObjectService
{
    /// <summary>Appends <paramref name="obj"/> to
    /// <paramref name="element"/>.<c>Objects</c>. Commits <c>AddObject</c>.</summary>
    void Add(Element element, EngineObject obj);

    /// <summary>Inserts <paramref name="obj"/> at <paramref name="index"/>.
    /// Clamps the index to the valid <c>[0, Count]</c> range. Commits
    /// <c>AddObject</c>.</summary>
    void InsertAt(Element element, int index, EngineObject obj);

    /// <summary>Removes <paramref name="obj"/> from
    /// <paramref name="element"/>.<c>Objects</c>. Returns false and commits
    /// nothing when the object is not in the list.</summary>
    bool Remove(Element element, EngineObject obj);

    /// <summary>Reorders the object at <paramref name="oldIndex"/> to
    /// <paramref name="newIndex"/>. No-ops and returns false when the
    /// indices are equal. Commits <c>MoveObject</c> on success.</summary>
    bool Move(Element element, int oldIndex, int newIndex);

    /// <summary>Replaces <paramref name="element"/>.<c>Objects[index]</c>
    /// with an object materialized from the clipboard JSON. Returns
    /// <see cref="ObjectPasteOutcome.Pasted"/> on success;
    /// <see cref="ObjectPasteOutcome.InvalidJson"/> when the payload does
    /// not parse; <see cref="ObjectPasteOutcome.MissingType"/> when the
    /// $type discriminator is absent or does not resolve to an
    /// <see cref="EngineObject"/>. Commits <c>PasteObject</c> only on
    /// success.</summary>
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
