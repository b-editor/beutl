using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
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
            Length = MediaTime.FromSeconds(2),
            Uri = new Uri(Path.Combine(BeutlHomeIsolation.CurrentHome!, $"fallback-json-{Guid.NewGuid():N}.belm")),
        };
        element.AddObject(fallback);
        editor.Scene.Children.Add(element);
        using var model = new ElementPropertyTabViewModel(editor);
        model.Element.Value = element;

        string? json = await model.Items.Single(item => item.Model == fallback).GetJsonString().FirstAsync();

        Assert.That(json, Is.EqualTo(fallback.Json.ToJsonString(JsonHelper.SerializerOptions)));
    }
}
