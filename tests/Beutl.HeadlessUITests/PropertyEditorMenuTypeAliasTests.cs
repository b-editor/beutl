using System.Numerics;
using Beutl.Collections;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class PropertyEditorMenuTypeAliasTests
{
    // The copied GetProperty<T> code must compile, so T is written as C# names it.
    [Test]
    [TestCase(typeof(float), "float")]
    [TestCase(typeof(int?), "int?")]
    [TestCase(typeof(Vector3[]), "System.Numerics.Vector3[]")]
    [TestCase(typeof(StrokeEffect.Resource), "Beutl.Graphics.Effects.StrokeEffect.Resource")]
    [TestCase(typeof(ICoreList<GradientStop>), "Beutl.Collections.ICoreList<Beutl.Media.GradientStop>")]
    [TestCase(typeof(Dictionary<string, List<int>>),
        "System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>")]
    public void Types_are_named_as_in_CSharp_code(Type type, string expected)
    {
        Assert.That(PropertyEditorMenu.GetTypeAlias(type), Is.EqualTo(expected));
    }
}
