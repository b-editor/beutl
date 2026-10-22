using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Graphics;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ElementFallbackJsonTests
{
    // Any fallback in an element's objects, not only FallbackEngineObject, shows the JSON it kept.
    [AvaloniaTest]
    public async Task A_fallback_drawable_shows_its_json()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"fallback-json-{Guid.NewGuid():N}");
        var fallback = new FallbackDrawable
        {
            Json = new JsonObject { ["$type"] = "Missing.Extension.Drawable", ["Size"] = 12 },
            Reason = FallbackReason.TypeNotFound,
        };
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"fallback-json-{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(fallback);
        editor.Scene.Children.Add(element);
        using var model = new ElementPropertyTabViewModel(editor);
        model.Element.Value = element;

        string? json = await model.Items.Single(item => item.Model == fallback).GetJsonString().FirstAsync();

        Assert.That(json, Is.EqualTo(fallback.Json.ToJsonString(JsonHelper.SerializerOptions)));
    }

    // The message, type name and JSON come before the rows of the fallback's base type instead of over them.
    [AvaloniaTest]
    public async Task A_fallback_drawable_shows_its_message_above_its_property_rows()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"fallback-layout-{Guid.NewGuid():N}");
        var fallback = new FallbackDrawable
        {
            Json = new JsonObject { ["$type"] = "Missing.Extension.Drawable" },
            Reason = FallbackReason.TypeNotFound,
        };
        var element = new Element
        {
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"fallback-layout-{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(fallback);
        editor.Scene.Children.Add(element);
        using var model = new ElementPropertyTabViewModel(editor);
        model.Element.Value = element;
        var window = new Window { Content = new ElementPropertyTabView { DataContext = model }, Width = 400, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            EngineObjectPropertyView entry = window.GetVisualDescendants().OfType<EngineObjectPropertyView>().Single();
            FallbackObjectView message = entry.GetVisualDescendants().OfType<FallbackObjectView>().Single();
            ItemsControl rows = entry.FindControl<ItemsControl>("propertiesList")!;

            Assert.Multiple(() =>
            {
                Assert.That(rows.Bounds.Height, Is.GreaterThan(0));
                Assert.That(
                    message.TranslatePoint(new Avalonia.Point(0, message.Bounds.Height), entry)!.Value.Y,
                    Is.LessThanOrEqualTo(rows.TranslatePoint(default, entry)!.Value.Y));
            });
        }
        finally
        {
            window.Close();
        }
    }
}
