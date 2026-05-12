using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.UnitTests.Engine.Graphics.Transformation;

public class CanonicalTransformLayoutTests
{
    private static CompositionContext Context => CompositionContext.Default;

    private static Drawable CreateDrawable()
    {
        return new FallbackDrawable();
    }

    [Test]
    public void Ensure_EmptyTransform_AppendsTRSInCanonicalOrder()
    {
        var drawable = CreateDrawable();
        drawable.Transform.CurrentValue = null;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.StructureChanged, Is.True);
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            // New canonical: [T, R, S]
            Assert.That(result.Group.Children[0], Is.TypeOf<TranslateTransform>());
            Assert.That(result.Group.Children[1], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[2], Is.TypeOf<ScaleTransform>());
        });
    }

    [Test]
    public void Ensure_OnlyTranslate_AppendsRotationAfterAndScaleAtEnd()
    {
        var drawable = CreateDrawable();
        var existing = new TranslateTransform(50f, 0f);
        var group = new TransformGroup();
        group.Children.Add(existing);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [T_existing, R_new, S_new]
            Assert.That(result.Group, Is.SameAs(group));
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.SameAs(existing));
            Assert.That(result.Group.Children[1], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[2], Is.TypeOf<ScaleTransform>());
            Assert.That(result.Translate, Is.SameAs(existing));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    [Test]
    public void Ensure_OnlyRotation_InsertsTranslateAtFrontAndScaleAtEnd()
    {
        var drawable = CreateDrawable();
        var existing = new RotationTransform(45f);
        var group = new TransformGroup();
        group.Children.Add(existing);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [T_new, R_existing, S_new]
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.TypeOf<TranslateTransform>());
            Assert.That(result.Group.Children[1], Is.SameAs(existing));
            Assert.That(result.Group.Children[2], Is.TypeOf<ScaleTransform>());
            Assert.That(result.Rotation, Is.SameAs(existing));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    [Test]
    public void Ensure_OnlyScale_InsertsTranslateAndRotationAtFront()
    {
        var drawable = CreateDrawable();
        var existing = new ScaleTransform(200f, 200f);
        var group = new TransformGroup();
        group.Children.Add(existing);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [T_new, R_new, S_existing]
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.TypeOf<TranslateTransform>());
            Assert.That(result.Group.Children[1], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[2], Is.SameAs(existing));
            Assert.That(result.Scale, Is.SameAs(existing));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    [Test]
    public void Ensure_SkewAndTranslate_PreservesSkewAndAppendsAfterT()
    {
        var drawable = CreateDrawable();
        var skew = new SkewTransform(15f, 0f);
        var translate = new TranslateTransform(10f, 20f);
        var group = new TransformGroup();
        group.Children.Add(skew);
        group.Children.Add(translate);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [Skew, T_existing, R_new, S_new]
            // T is at tIdx=1, R right after T (idx=2), S at the tail (idx=3). Skew is at idx=0, before T = on the post side.
            Assert.That(result.Group.Children.Count, Is.EqualTo(4));
            Assert.That(result.Group.Children[0], Is.SameAs(skew));
            Assert.That(result.Group.Children[1], Is.SameAs(translate));
            Assert.That(result.Group.Children[2], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[3], Is.TypeOf<ScaleTransform>());
            Assert.That(result.Translate, Is.SameAs(translate));
            Assert.That(result.StructureChanged, Is.True);
            // Skew sits before T (idx<tIdx, near the head) = included in PostMatrixOfT.
            Assert.That(result.PostMatrixOfT, Is.Not.EqualTo(Matrix.Identity));
        });
    }

    [Test]
    public void Ensure_LegacyRSTOrder_DoesNotReorder()
    {
        // A Drawable with the old [R, S, T] layout is not reorganized; the existing R/S/T are returned as the operative transforms.
        // Backward compatibility: opening an existing project does not change the order of Children.
        var drawable = CreateDrawable();
        var r = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var t = new TranslateTransform(10f, 20f);
        var group = new TransformGroup();
        group.Children.Add(r);
        group.Children.Add(s);
        group.Children.Add(t);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // Structure stays [R, S, T] with no additions.
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.SameAs(r));
            Assert.That(result.Group.Children[1], Is.SameAs(s));
            Assert.That(result.Group.Children[2], Is.SameAs(t));
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Scale, Is.SameAs(s));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.StructureChanged, Is.False);
        });
    }

    [Test]
    public void Ensure_MultipleRotationsAndScales_DoesNotInsertReusesFirst()
    {
        var drawable = CreateDrawable();
        var r1 = new RotationTransform(10f);
        var r2 = new RotationTransform(20f);
        var s1 = new ScaleTransform(150f, 150f);
        var s2 = new ScaleTransform(80f, 80f);
        var t = new TranslateTransform(5f, 5f);
        var group = new TransformGroup();
        group.Children.Add(r1);
        group.Children.Add(r2);
        group.Children.Add(s1);
        group.Children.Add(s2);
        group.Children.Add(t);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // Structures with duplicates are **preserved as-is**; the "first occurrence" of each type is adopted.
            Assert.That(result.Group.Children.Count, Is.EqualTo(5));
            Assert.That(result.Group.Children[0], Is.SameAs(r1));
            Assert.That(result.Group.Children[1], Is.SameAs(r2));
            Assert.That(result.Group.Children[2], Is.SameAs(s1));
            Assert.That(result.Group.Children[3], Is.SameAs(s2));
            Assert.That(result.Group.Children[4], Is.SameAs(t));
            Assert.That(result.Rotation, Is.SameAs(r1));
            Assert.That(result.Scale, Is.SameAs(s1));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.StructureChanged, Is.False);
        });
    }

    [Test]
    public void Ensure_ReverseOrder_DoesNotReorder()
    {
        // Even with the completely reversed [S, R, T], since all types are present no reorganization happens.
        // The first occurrence of each type is the operative one.
        var drawable = CreateDrawable();
        var s = new ScaleTransform(150f, 150f);
        var r = new RotationTransform(30f);
        var t = new TranslateTransform(5f, 5f);
        var group = new TransformGroup();
        group.Children.Add(s);
        group.Children.Add(r);
        group.Children.Add(t);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.SameAs(s));
            Assert.That(result.Group.Children[1], Is.SameAs(r));
            Assert.That(result.Group.Children[2], Is.SameAs(t));
            Assert.That(result.Scale, Is.SameAs(s));
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.StructureChanged, Is.False);
        });
    }

    [Test]
    public void Ensure_TranslateThenScale_InsertsRotationAfterT()
    {
        // [T, S] is missing R. Insert R right after T(0) → [T, R_new, S].
        var drawable = CreateDrawable();
        var t = new TranslateTransform(10f, 20f);
        var s = new ScaleTransform(400f, 200f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(s);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [T, R_new, S] = the new canonical layout
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.SameAs(t));
            Assert.That(result.Group.Children[1], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[2], Is.SameAs(s));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.Scale, Is.SameAs(s));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    [Test]
    public void Ensure_RotationAndTranslate_AppendsScaleAtEnd()
    {
        // [R, T] is missing S. Append S at the end → [R, T, S_new].
        // The existing R(0) is honored as the operative transform (no reorganization).
        var drawable = CreateDrawable();
        var r = new RotationTransform(45f);
        var t = new TranslateTransform(30f, 40f);
        var group = new TransformGroup();
        group.Children.Add(r);
        group.Children.Add(t);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Group.Children[0], Is.SameAs(r));
            Assert.That(result.Group.Children[1], Is.SameAs(t));
            Assert.That(result.Group.Children[2], Is.TypeOf<ScaleTransform>());
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Translate, Is.SameAs(t));
        });
    }

    [Test]
    public void Ensure_NewCanonicalTRSOrder_DoesNotInsert()
    {
        // [T, R, S] is already in canonical order, so nothing is added.
        var drawable = CreateDrawable();
        var t = new TranslateTransform(5f, 5f);
        var r = new RotationTransform(10f);
        var s = new ScaleTransform(150f, 150f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(r);
        group.Children.Add(s);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Scale, Is.SameAs(s));
            Assert.That(result.StructureChanged, Is.False);
        });
    }

    [Test]
    public void Ensure_MultipleTranslates_UsesFirstOccurrence()
    {
        // When there are multiple Translates, adopt the first occurrence (closest to the head of the list).
        // Input: [T_canonical, R, S, T_extra]
        // Expectation: structure unchanged, operative Translate is the leading T_canonical.
        var drawable = CreateDrawable();
        var tCanonical = new TranslateTransform(50f, 50f);
        var r = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var tExtra = new TranslateTransform(10f, 0f);
        var group = new TransformGroup();
        group.Children.Add(tCanonical);
        group.Children.Add(r);
        group.Children.Add(s);
        group.Children.Add(tExtra);
        drawable.Transform.CurrentValue = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.Group.Children.Count, Is.EqualTo(4));
            Assert.That(result.Group.Children[0], Is.SameAs(tCanonical));
            Assert.That(result.Group.Children[1], Is.SameAs(r));
            Assert.That(result.Group.Children[2], Is.SameAs(s));
            Assert.That(result.Group.Children[3], Is.SameAs(tExtra));
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Scale, Is.SameAs(s));
            Assert.That(result.Translate, Is.SameAs(tCanonical));
            Assert.That(result.StructureChanged, Is.False);
            // T(0), R(1), S(2) are contiguous
        });
    }

    [Test]
    public void Ensure_NonTransformGroupValue_WrapsInTransformGroup()
    {
        var drawable = CreateDrawable();
        var existing = new TranslateTransform(7f, 3f);
        drawable.Transform.CurrentValue = existing;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // Wrapped in a TransformGroup, becoming [T_existing, R_new, S_new]
            Assert.That(drawable.Transform.CurrentValue, Is.TypeOf<TransformGroup>());
            Assert.That(result.Group.Children, Has.Member(existing));
            Assert.That(result.Translate, Is.SameAs(existing));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    // ===== FindCanonicalTransforms (read-only) tests =====
    // Returns the first occurrence of R/S/T without checking order (same policy as Ensure).
    // The group is never mutated.

    [Test]
    public void FindCanonicalTransforms_DoesNotMutateGroup()
    {
        var t = new TranslateTransform(10f, 20f);
        var s = new ScaleTransform(400f, 200f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(s);

        int countBefore = group.Children.Count;
        _ = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.That(group.Children.Count, Is.EqualTo(countBefore));
        Assert.That(group.Children[0], Is.SameAs(t));
        Assert.That(group.Children[1], Is.SameAs(s));
    }

    [Test]
    public void FindCanonicalTransforms_CanonicalTRS_ReturnsAll()
    {
        // New canonical [T, R, S]
        var t = new TranslateTransform(5f, 5f);
        var r = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(r);
        group.Children.Add(s);

        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.SameAs(r));
            Assert.That(scale, Is.SameAs(s));
            Assert.That(translate, Is.SameAs(t));
        });
    }

    [Test]
    public void FindCanonicalTransforms_LegacyRST_ReturnsAll()
    {
        // Even with the old [R, S, T], return all without order checks (backward compatibility).
        var r = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var t = new TranslateTransform(5f, 5f);
        var group = new TransformGroup();
        group.Children.Add(r);
        group.Children.Add(s);
        group.Children.Add(t);

        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.SameAs(r));
            Assert.That(scale, Is.SameAs(s));
            Assert.That(translate, Is.SameAs(t));
        });
    }

    [Test]
    public void FindCanonicalTransforms_ReverseOrder_ReturnsAll()
    {
        // [S, R, T] also returns all.
        var s = new ScaleTransform(150f, 150f);
        var r = new RotationTransform(30f);
        var t = new TranslateTransform(5f, 5f);
        var group = new TransformGroup();
        group.Children.Add(s);
        group.Children.Add(r);
        group.Children.Add(t);

        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.SameAs(r));
            Assert.That(scale, Is.SameAs(s));
            Assert.That(translate, Is.SameAs(t));
        });
    }

    [Test]
    public void FindCanonicalTransforms_PartialMissing_ReturnsNullsForMissing()
    {
        // [T, S] (missing R) → R is null; T and S return their first occurrences.
        var t = new TranslateTransform(10f, 20f);
        var s = new ScaleTransform(400f, 200f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(s);

        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.Null);
            Assert.That(scale, Is.SameAs(s));
            Assert.That(translate, Is.SameAs(t));
        });
    }

    [Test]
    public void FindCanonicalTransforms_NonTransformGroup_HandlesSingleton()
    {
        var r = new RotationTransform(45f);
        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(r);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.SameAs(r));
            Assert.That(scale, Is.Null);
            Assert.That(translate, Is.Null);
        });
    }

    [Test]
    public void FindCanonicalTransforms_Null_ReturnsAllNull()
    {
        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(null);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.Null);
            Assert.That(scale, Is.Null);
            Assert.That(translate, Is.Null);
        });
    }

    [Test]
    public void FindCanonicalTransforms_AgreesWithEnsureOnNewCanonical()
    {
        // Under the new canonical [T, R, S], Find and Ensure return the same references and do not change the structure.
        var tForFind = new TranslateTransform(33f, 44f);
        var rForFind = new RotationTransform(10f);
        var sForFind = new ScaleTransform(120f, 120f);
        var groupForFind = new TransformGroup();
        groupForFind.Children.Add(tForFind);
        groupForFind.Children.Add(rForFind);
        groupForFind.Children.Add(sForFind);

        var (rotF, scaleF, translateF) = CanonicalTransformLayout.FindCanonicalTransforms(groupForFind);

        var drawable = CreateDrawable();
        var tForEnsure = new TranslateTransform(33f, 44f);
        var rForEnsure = new RotationTransform(10f);
        var sForEnsure = new ScaleTransform(120f, 120f);
        var groupForEnsure = new TransformGroup();
        groupForEnsure.Children.Add(tForEnsure);
        groupForEnsure.Children.Add(rForEnsure);
        groupForEnsure.Children.Add(sForEnsure);
        drawable.Transform.CurrentValue = groupForEnsure;

        var ensured = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(groupForFind.Children.IndexOf(rotF!), Is.EqualTo(groupForEnsure.Children.IndexOf(ensured.Rotation)));
            Assert.That(groupForFind.Children.IndexOf(scaleF!), Is.EqualTo(groupForEnsure.Children.IndexOf(ensured.Scale)));
            Assert.That(groupForFind.Children.IndexOf(translateF!), Is.EqualTo(groupForEnsure.Children.IndexOf(ensured.Translate)));
            Assert.That(ensured.StructureChanged, Is.False);
        });
    }
}
