using Beutl.Composition;

namespace Beutl.Graphics.Transformation;

/// <summary>
/// Helper for completing the canonical [T, R, S] layout.
///
/// <para>
/// <see cref="TransformGroup"/> composes its children via <c>Aggregate(I, (acc, x) => x.M * acc)</c>, so
/// under the row-vector right-multiplication convention (<c>v' = v · M</c>), the tail of the list ends up
/// on the left of the product (= first in application order, innermost).
/// Therefore <c>[T, R, S]</c> means <c>M = S · R · T</c> with application order S → R → T, making
/// Translate the outermost (scene-space) operation while Rotation/Scale act around the pivot.
/// </para>
///
/// <para>
/// If the existing list contains multiple R/S/T or non-canonical ordering, the list is not reorganized
/// (backward compatibility with old <c>[R, S, T]</c> projects). The "first occurrence" of each type is
/// adopted as the operative transform.
/// </para>
/// </summary>
internal static class CanonicalTransformLayout
{
    public static CanonicalTransformLayoutResult Ensure(Drawable drawable, CompositionContext context)
    {
        ArgumentNullException.ThrowIfNull(drawable);
        ArgumentNullException.ThrowIfNull(context);

        bool groupChanged = false;
        if (drawable.Transform.CurrentValue is not TransformGroup tg)
        {
            var newGroup = new TransformGroup();
            if (drawable.Transform.CurrentValue is Transform existing)
            {
                newGroup.Children.Add(existing);
            }
            drawable.Transform.CurrentValue = newGroup;
            tg = newGroup;
            groupChanged = true;
        }

        (int rIdx, int sIdx, int tIdx) = FindFirstEnabledIndices(tg.Children);

        bool added = false;

        if (tIdx < 0)
        {
            tg.Children.Insert(0, new TranslateTransform());
            tIdx = 0;
            if (rIdx >= 0) rIdx++;
            if (sIdx >= 0) sIdx++;
            added = true;
        }

        if (rIdx < 0)
        {
            int insertAt = tIdx + 1;
            tg.Children.Insert(insertAt, new RotationTransform());
            rIdx = insertAt;
            if (sIdx >= insertAt) sIdx++;
            added = true;
        }

        if (sIdx < 0)
        {
            tg.Children.Add(new ScaleTransform());
            sIdx = tg.Children.Count - 1;
            added = true;
        }

        var rotation = (RotationTransform)tg.Children[rIdx];
        var scale = (ScaleTransform)tg.Children[sIdx];
        var translate = (TranslateTransform)tg.Children[tIdx];

        // Matrix applied after T in application order — composition of children to the left of T in the
        // list. Identity for canonical [T, R, S] layouts; non-identity only for legacy [R, S, T] data.
        Matrix postMatrixOfT = Matrix.Identity;
        for (int i = 0; i < tIdx; i++)
        {
            postMatrixOfT = tg.Children[i].CreateMatrix(context) * postMatrixOfT;
        }

        bool structureChanged = groupChanged || added;
        return new CanonicalTransformLayoutResult(translate, scale, rotation, tg, structureChanged)
        {
            PostMatrixOfT = postMatrixOfT,
        };
    }

    /// <summary>
    /// Reads the first occurrence of R/S/T each (without mutating the group). Does not return null even if the order is invalid.
    /// </summary>
    public static (RotationTransform? Rotation, ScaleTransform? Scale, TranslateTransform? Translate)
        FindCanonicalTransforms(Transform? t)
    {
        if (t is TransformGroup tg)
        {
            (int rIdx, int sIdx, int tIdx) = FindFirstEnabledIndices(tg.Children);
            return (
                rIdx >= 0 ? (RotationTransform)tg.Children[rIdx] : null,
                sIdx >= 0 ? (ScaleTransform)tg.Children[sIdx] : null,
                tIdx >= 0 ? (TranslateTransform)tg.Children[tIdx] : null);
        }
        return t switch
        {
            RotationTransform r when r.IsEnabled => (r, null, null),
            ScaleTransform s when s.IsEnabled => (null, s, null),
            TranslateTransform tr when tr.IsEnabled => (null, null, tr),
            _ => (null, null, null)
        };
    }

    // Disabled children are ignored by TransformGroup.CreateMatrix, so they cannot serve as the
    // operative T/R/S — editing them would leave the preview unchanged.
    private static (int rIdx, int sIdx, int tIdx) FindFirstEnabledIndices(IReadOnlyList<Transform> children)
    {
        int rIdx = -1, sIdx = -1, tIdx = -1;
        for (int i = 0; i < children.Count; i++)
        {
            Transform c = children[i];
            if (!c.IsEnabled) continue;
            if (rIdx < 0 && c is RotationTransform) rIdx = i;
            if (sIdx < 0 && c is ScaleTransform) sIdx = i;
            if (tIdx < 0 && c is TranslateTransform) tIdx = i;
        }
        return (rIdx, sIdx, tIdx);
    }
}

internal sealed record CanonicalTransformLayoutResult(
    TranslateTransform Translate,
    ScaleTransform Scale,
    RotationTransform Rotation,
    TransformGroup Group,
    bool StructureChanged)
{
    /// <summary>Matrix applied after the operative T in application order (Identity under the new [T, R, S]).</summary>
    internal Matrix PostMatrixOfT { get; init; } = Matrix.Identity;
}
