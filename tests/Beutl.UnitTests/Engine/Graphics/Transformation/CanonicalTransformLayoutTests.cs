using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.Transformation;

public class CanonicalTransformLayoutTests
{
    private static CompositionContext Context => CompositionContext.Default;

    private static Drawable CreateDrawable()
    {
        return new FallbackDrawable();
    }

    // A group holding one Translate, Rotation and Scale, in the order the letters of order name them.
    private static (TransformGroup Group, TranslateTransform T, RotationTransform R, ScaleTransform S) CreateGroup(
        string order)
    {
        var t = new TranslateTransform(5f, 5f);
        var r = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var group = new TransformGroup();
        foreach (char kind in order)
        {
            group.Children.Add(kind switch
            {
                'T' => (Transform)t,
                'R' => r,
                'S' => s,
                _ => throw new ArgumentOutOfRangeException(nameof(order)),
            });
        }

        return (group, t, r, s);
    }

    [Test]
    public void Ensure_EmptyTransform_AppendsTRSInCanonicalOrder()
    {
        var drawable = CreateDrawable();
        drawable.Transform.Value = null;

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
        drawable.Transform.Value = group;

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
        drawable.Transform.Value = group;

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
        drawable.Transform.Value = group;

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
        drawable.Transform.Value = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // [Skew, T_existing, R_new, S_new]
            // Skew sits before T, so PostMatrixOfT includes it.
            Assert.That(result.Group.Children.Count, Is.EqualTo(4));
            Assert.That(result.Group.Children[0], Is.SameAs(skew));
            Assert.That(result.Group.Children[1], Is.SameAs(translate));
            Assert.That(result.Group.Children[2], Is.TypeOf<RotationTransform>());
            Assert.That(result.Group.Children[3], Is.TypeOf<ScaleTransform>());
            Assert.That(result.Translate, Is.SameAs(translate));
            Assert.That(result.StructureChanged, Is.True);
            Assert.That(result.PostMatrixOfT, Is.Not.EqualTo(Matrix.Identity));
        });
    }

    // With all three present nothing is added or reordered, whatever the order: opening an existing project keeps
    // the legacy [R, S, T] layout it was saved with. The first occurrence of each type is the operative one.
    [TestCase("TRS", TestName = "Ensure_NewCanonicalTRSOrder_DoesNotInsert")]
    [TestCase("RST", TestName = "Ensure_LegacyRSTOrder_DoesNotReorder")]
    [TestCase("SRT", TestName = "Ensure_ReverseOrder_DoesNotReorder")]
    public void Ensure_AllThreePresent_DoesNotReorder(string order)
    {
        var drawable = CreateDrawable();
        (TransformGroup group, TranslateTransform t, RotationTransform r, ScaleTransform s) = CreateGroup(order);
        Transform[] children = [.. group.Children];
        drawable.Transform.Value = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.Group.Children.Count, Is.EqualTo(3));
            for (int i = 0; i < children.Length; i++)
                Assert.That(result.Group.Children[i], Is.SameAs(children[i]));
            Assert.That(result.Translate, Is.SameAs(t));
            Assert.That(result.Rotation, Is.SameAs(r));
            Assert.That(result.Scale, Is.SameAs(s));
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
        drawable.Transform.Value = group;

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
    public void Ensure_TranslateThenScale_InsertsRotationAfterT()
    {
        // [T, S] is missing R. Insert R right after T(0) → [T, R_new, S].
        var drawable = CreateDrawable();
        var t = new TranslateTransform(10f, 20f);
        var s = new ScaleTransform(400f, 200f);
        var group = new TransformGroup();
        group.Children.Add(t);
        group.Children.Add(s);
        drawable.Transform.Value = group;

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
        drawable.Transform.Value = group;

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
        drawable.Transform.Value = group;

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
        });
    }

    [Test]
    public void Ensure_NonTransformGroupValue_WrapsInTransformGroup()
    {
        var drawable = CreateDrawable();
        var existing = new TranslateTransform(7f, 3f);
        drawable.Transform.Value = existing;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            // Wrapped in a TransformGroup, becoming [T_existing, R_new, S_new]
            Assert.That(drawable.Transform.Value, Is.TypeOf<TransformGroup>());
            Assert.That(result.Group.Children, Has.Member(existing));
            Assert.That(result.Translate, Is.SameAs(existing));
            Assert.That(result.StructureChanged, Is.True);
        });
    }

    // A drawable in a document is attached to its root. Its transform has to end up under the new group, attached
    // with it, rather than left without a parent when the drawable lets it go.
    [Test]
    public void Ensure_NonTransformGroupValue_OfAnAttachedDrawable_MovesItUnderTheGroup()
    {
        var root = new TestHierarchicalRoot();
        var drawable = CreateDrawable();
        var existing = new TranslateTransform(7f, 3f);
        drawable.Transform.Value = existing;
        ((IModifiableHierarchical)root).AddChild(drawable);

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(drawable.Transform.Value, Is.SameAs(result.Group));
            Assert.That(result.Group.Children[0], Is.SameAs(existing));
            Assert.That(existing.HierarchicalParent, Is.SameAs(result.Group));
            Assert.That(((IHierarchical)existing).FindHierarchicalRoot(), Is.SameAs(root));
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

    // Find returns all three in any order, without order checks (backward compatibility).
    [TestCase("TRS", TestName = "FindCanonicalTransforms_CanonicalTRS_ReturnsAll")]
    [TestCase("RST", TestName = "FindCanonicalTransforms_LegacyRST_ReturnsAll")]
    [TestCase("SRT", TestName = "FindCanonicalTransforms_ReverseOrder_ReturnsAll")]
    public void FindCanonicalTransforms_AllThreePresent_ReturnsAll(string order)
    {
        (TransformGroup group, TranslateTransform t, RotationTransform r, ScaleTransform s) = CreateGroup(order);

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
    public void FindCanonicalTransforms_DisabledChildren_AreSkipped()
    {
        // Disabled T/R/S do not contribute to TransformGroup.CreateMatrix, so the handle code must not adopt them.
        var disabledT = new TranslateTransform(10f, 20f) { IsEnabled = false };
        var enabledT = new TranslateTransform(30f, 40f);
        var disabledR = new RotationTransform(15f) { IsEnabled = false };
        var enabledR = new RotationTransform(45f);
        var s = new ScaleTransform(150f, 150f);
        var group = new TransformGroup();
        group.Children.Add(disabledT);
        group.Children.Add(enabledT);
        group.Children.Add(disabledR);
        group.Children.Add(enabledR);
        group.Children.Add(s);

        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(group);

        Assert.Multiple(() =>
        {
            Assert.That(translate, Is.SameAs(enabledT));
            Assert.That(rotation, Is.SameAs(enabledR));
            Assert.That(scale, Is.SameAs(s));
        });
    }

    [Test]
    public void FindCanonicalTransforms_DisabledSingleton_ReturnsAllNull()
    {
        var r = new RotationTransform(45f) { IsEnabled = false };
        var (rotation, scale, translate) = CanonicalTransformLayout.FindCanonicalTransforms(r);

        Assert.Multiple(() =>
        {
            Assert.That(rotation, Is.Null);
            Assert.That(scale, Is.Null);
            Assert.That(translate, Is.Null);
        });
    }

    [Test]
    public void Ensure_DisabledExisting_InsertsNewEnabledTransforms()
    {
        // If the existing T/R/S are all disabled, Ensure must insert fresh enabled ones rather than adopt the disabled ones.
        var drawable = CreateDrawable();
        var disabledT = new TranslateTransform(10f, 20f) { IsEnabled = false };
        var disabledR = new RotationTransform(30f) { IsEnabled = false };
        var disabledS = new ScaleTransform(200f, 200f) { IsEnabled = false };
        var group = new TransformGroup();
        group.Children.Add(disabledT);
        group.Children.Add(disabledR);
        group.Children.Add(disabledS);
        drawable.Transform.Value = group;

        CanonicalTransformLayoutResult result = CanonicalTransformLayout.Ensure(drawable, Context);

        Assert.Multiple(() =>
        {
            Assert.That(result.StructureChanged, Is.True);
            Assert.That(result.Translate, Is.Not.SameAs(disabledT));
            Assert.That(result.Rotation, Is.Not.SameAs(disabledR));
            Assert.That(result.Scale, Is.Not.SameAs(disabledS));
            Assert.That(result.Translate.IsEnabled, Is.True);
            Assert.That(result.Rotation.IsEnabled, Is.True);
            Assert.That(result.Scale.IsEnabled, Is.True);
        });
    }
}
