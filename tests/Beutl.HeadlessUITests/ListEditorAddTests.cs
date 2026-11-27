using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Collections;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Primitives;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ListEditorAddTests
{
    [AvaloniaTest]
    [TestCase(typeof(Scene3D), nameof(Scene3D.Objects), typeof(Cube3D))]
    [TestCase(typeof(Scene3D), nameof(Scene3D.Lights), typeof(DirectionalLight3D))]
    [TestCase(typeof(DrawableGroup), nameof(DrawableGroup.Children), typeof(RectShape))]
    [TestCase(typeof(DrawableDecorator), nameof(DrawableDecorator.Children), typeof(RectShape))]
    [TestCase(typeof(DrawableObject3D), nameof(DrawableObject3D.Children), typeof(RectShape))]
    [TestCase(typeof(SoundGroup), nameof(SoundGroup.Children), typeof(SourceSound))]
    public async Task Confirming_a_child_type_adds_it_to_the_operator_and_survives_reloading(
        Type operatorType, string propertyName, Type childType)
    {
        var owner = (EngineObject)Activator.CreateInstance(operatorType)!;
        IPropertyAdapter adapter = PropertyAdapterFactory.CreateAdapters(owner)
            .Single(property => property.GetEngineProperty()?.Name == propertyName);
        using var history = new HistoryScope(owner);
        (PropertyEditorViewModel viewModel, Control control) = CreateControl(adapter);
        using var context = viewModel;
        viewModel.Accept(new ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
            (typeof(ExtensionProvider), TestShell.Extensions)));
        var view = (ListEditor)control;
        var window = new Window { Content = view, Width = 480, Height = 580 };
        string displayName = TestShell.Library.FindItem(childType)!.DisplayName;
        string captureName = $"{operatorType.Name}-{propertyName}";
        var uri = new Uri(Path.Combine(Path.GetTempPath(), $"beutl-list-{Guid.NewGuid():N}.belm"));
        try
        {
            window.Show();
            view.FindControl<ToggleButton>("expandToggle")!.IsChecked = true;
            HeadlessTestHelpers.Render();
            view.AddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            LibraryItemPickerFlyoutPresenter? picker = null;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                picker = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().SingleOrDefault();
                return picker?.Items?.Any(item => item.DisplayName == displayName) == true;
            }, message: $"The picker should offer {childType.Name}.");

            picker!.SelectedItem = picker.Items!.Single(item => item.DisplayName == displayName);
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(picker, nameof(ListEditorAddTests), $"{captureName}-picker");
            picker.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            HeadlessTestHelpers.Settle();

            var child = ((IEnumerable<EngineObject>)adapter.GetValue()!).SingleOrDefault();
            Assert.That(child, Is.TypeOf(childType));
            HeadlessTestHelpers.Render();
            HeadlessCapture.SaveIfRequested(window, nameof(ListEditorAddTests), $"{captureName}-added");

            var element = new Element { Uri = uri };
            element.AddObject(owner);
            CoreSerializer.StoreToUri(element, uri);
            var restored = CoreSerializer.RestoreFromUri<Element>(uri);
            EngineObject restoredOperator = restored.Objects.Single(obj => obj.GetType() == operatorType);
            IPropertyAdapter restoredAdapter = PropertyAdapterFactory.CreateAdapters(restoredOperator)
                .Single(property => property.GetEngineProperty()?.Name == propertyName);
            var restoredChild = ((IEnumerable<EngineObject>)restoredAdapter.GetValue()!).Single();
            Assert.That(restoredChild, Is.TypeOf(childType));
            Assert.That(restoredChild.Id.Value, Is.EqualTo(child!.Id.Value));
        }
        finally
        {
            window.Close();
            File.Delete(uri.LocalPath);
        }
    }

    [AvaloniaTest]
    public async Task Closing_the_type_picker_adds_nothing_and_reports_nothing()
    {
        var group = new FilterEffectGroup();
        using var history = new HistoryScope(group);
        var adapter = new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group);
        using var list = new ListEditorViewModel<FilterEffect>(adapter);
        list.Accept(new ServiceVisitor(
            (typeof(HistoryManager), history.History),
            (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
            (typeof(ExtensionProvider), TestShell.Extensions)));
        var view = new ListEditor { DataContext = list };
        var window = new Window { Content = view, Width = 480, Height = 600 };
        using var notifications = new NotificationCapture(TestShell.Notifications);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            view.GetVisualDescendants().OfType<Button>().Single(button => button is not ToggleButton)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            LibraryItemPickerFlyoutPresenter? picker = null;
            for (int i = 0; i < 200 && picker is null; i++)
            {
                await Task.Delay(25);
                HeadlessTestHelpers.Render();
                picker = window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().SingleOrDefault();
            }
            Assert.That(picker, Is.Not.Null, "A list of an abstract type asks for the type to add.");

            picker!.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(group.Children, Is.Empty);
                Assert.That(notifications.All, Is.Empty, "Closing the picker is not an error.");
            });
        }
        finally
        {
            window.Close();
        }
    }
}
