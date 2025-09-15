using System;
using System.Linq;
using Beutl.Operators;
using Beutl.Services;

namespace Beutl.UnitTests.Operators;

public class LibraryRegistrarTests
{
    [Test]
    public void RegisterAll_RegistersSourceOperatorsAndDrawables()
    {
        // Act
        LibraryRegistrar.RegisterAll();

        // Assert some representative bindings
        var sources = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.SourceOperator);
        Assert.That(sources.Contains(typeof(Beutl.Operators.Source.RectOperator)), Is.True);
        Assert.That(sources.Contains(typeof(Beutl.Operators.Source.TextBlockOperator)), Is.True);

        var drawables = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.Drawable);
        Assert.That(drawables.Contains(typeof(Beutl.Graphics.Shapes.RectShape)), Is.True);
        Assert.That(drawables.Contains(typeof(Beutl.Graphics.Shapes.TextBlock)), Is.True);

        // Filter effects sample
        var effects = LibraryService.Current.GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect);
        Assert.That(effects.Contains(typeof(Beutl.Graphics.Effects.Blur)), Is.True);
    }

    [Test]
    public void FindItem_ReturnsItemForRegisteredType()
    {
        LibraryRegistrar.RegisterAll();
        var item = LibraryService.Current.FindItem(typeof(Beutl.Operators.Source.RectOperator));
        Assert.That(item, Is.Not.Null);
        Assert.That(item!.DisplayName, Is.Not.Empty);
    }
}

