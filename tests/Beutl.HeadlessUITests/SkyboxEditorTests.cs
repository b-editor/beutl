using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Skyboxes;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class SkyboxEditorTests
{
    [AvaloniaTest]
    public async Task SceneSkyboxPickerListsAllThreeTypesAndAppliesTheSelection()
    {
        await TestReset.ResetShellAsync();
        var scene = new Scene3D();
        using var history = new PropertyEditorTestHost.HistoryScope(scene);
        var adapter = new SimplePropertyAdapter<Skybox3D?>((SimpleProperty<Skybox3D?>)scene.Skybox, scene);
        var (model, editor) = PropertyEditorTestHost.CreateControl(adapter);
        using var lifetime = model;
        model.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(ExtensionProvider), TestShell.Extensions)));
        Assert.That(editor, Is.TypeOf<CoreObjectEditor>());
        Assert.That(model.Header, Is.EqualTo(GraphicsStrings.Scene3D_Skybox));

        var window = new Window
        {
            Width = 450,
            Height = 620,
            Content = new Border { Padding = new Thickness(16), Child = editor },
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            ToggleButton expander = editor.FindControl<ToggleButton>("expandToggle")!;
            var add = (Button)expander.Tag!;
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await HeadlessTestHelpers.WaitUntilAsync(() => GetPicker()?.Items?.Count == 3, TimeSpan.FromSeconds(15));

            LibraryItemPickerFlyoutPresenter picker = GetPicker()!;
            var items = picker.Items!;
            Assert.That(items.Select(item => ((SingleTypeLibraryItem)item.UserData).ImplementationType),
                Is.EquivalentTo(new[] { typeof(EquirectangularSkybox), typeof(CubemapSkybox), typeof(ProceduralSkybox) }));
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(picker, nameof(SkyboxEditorTests), "type-picker");

            picker.SelectedItem = items.Single(item => item.DisplayName == GraphicsStrings.ProceduralSkybox);
            picker.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "AcceptButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await HeadlessTestHelpers.WaitUntilAsync(() => scene.Skybox.Value is ProceduralSkybox, TimeSpan.FromSeconds(10));

            var skyboxModel = (CoreObjectEditorViewModel<Skybox3D>)model;
            skyboxModel.IsExpanded.Value = true;
            Panel content = editor.FindControl<Panel>("content")!;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render();
                return content.Opacity == 1 && content.GetVisualDescendants().OfType<PropertyEditor>()
                    .Any(row => row.Header == GraphicsStrings.ProceduralSkybox_SkyColor && row.IsEffectivelyVisible);
            }, TimeSpan.FromSeconds(10));
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(editor, nameof(SkyboxEditorTests), "procedural-properties");
        }
        finally
        {
            window.Close();
        }

        LibraryItemPickerFlyoutPresenter? GetPicker()
            => window.OpenedPopups.Select(popup => popup.Child).OfType<LibraryItemPickerFlyoutPresenter>().SingleOrDefault();
    }
}
