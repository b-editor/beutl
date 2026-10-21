using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// An element whose file holds a value that could not be loaded and that saving would not write back (here a
// keyframe easing from a plugin that is not installed) keeps that file until it is repaired, so its edits would
// not be saved. The editor says so once when the scene opens, shows the clip as read-only, refuses ordinary
// edits, and still lets the user remove or replace what could not be loaded, or delete the clip.
//
// An object whose type could not be loaded does not protect its element: it saves the JSON it was read from,
// so the clip stays editable, the object itself is read-only, and the project shows once that it keeps data it
// cannot show.
[TestFixture]
public sealed class RecoveryProtectedElementEditorTests
{
    private const string ElementName = "Protected clip";

    private const string MissingEasingType = "[Missing.Plugin]Missing.Namespace:MissingEasing";

    [AvaloniaTest]
    public async Task Opening_the_scene_shows_one_notice_that_names_the_element()
    {
        (string projectPath, _) = await CreateProjectWithUnreadableEasing("recovery-notice");
        using var notifications = new NotificationCapture();
        try
        {
            EditViewModel editor = await OpenProjectAsync(projectPath);
            Notification[] notices = notifications.All
                .Where(n => n.Title == MessageStrings.RecoveryProtectedElementsTitle)
                .ToArray();

            Assert.That(notices, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(notices[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(notices[0].Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
                Assert.That(notices[0].Message, Does.Contain(ElementName));
                Assert.That(notices[0].Message, Does.Contain(editor.Scene.Name));
                Assert.That(notices[0].CancellationToken.IsCancellationRequested, Is.False);
            });

            await TestReset.ResetShellAsync();
            Assert.That(notices[0].CancellationToken.IsCancellationRequested, Is.True,
                "Closing the editor must dismiss its notice.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Timeline_shows_the_clip_as_read_only_and_still_deletes_it()
    {
        (string projectPath, string elementPath) = await CreateProjectWithUnreadableEasing("recovery-timeline");
        byte[] original = File.ReadAllBytes(elementPath);
        EditViewModel editor = await OpenProjectAsync(projectPath);
        var window = new Window { Content = new EditView { DataContext = editor }, Width = 1200, Height = 800 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            ElementView view = window.GetVisualDescendants().OfType<ElementView>().Single();
            var viewModel = (ElementViewModel)view.DataContext!;
            Control border = Named(view, "border");
            Control lockIcon = Named(view, "lockIcon");
            Control recoveryIcon = Named(view, "recoveryIcon");
            Control label = Named(view, "textBlock");

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsRecoveryProtected.Value, Is.True);
                Assert.That(viewModel.IsEditable.Value, Is.False);
                Assert.That(viewModel.IsLockedByUser.Value, Is.False);
                Assert.That(viewModel.CanToggleLock.Value, Is.False, "A lock on this clip would not be saved.");
                Assert.That(recoveryIcon.IsVisible, Is.True);
                Assert.That(lockIcon.IsVisible, Is.False, "The clip is not locked by the user.");
                Assert.That(ToolTip.GetTip(border), Is.EqualTo(MessageStrings.RecoveryProtectedElementToolTip));
                double glyphRight = recoveryIcon.TranslatePoint(new Point(recoveryIcon.Bounds.Width, 0), view)!.Value.X;
                double labelLeft = label.TranslatePoint(new Point(0, 0), view)!.Value.X;
                Assert.That(labelLeft, Is.GreaterThanOrEqualTo(glyphRight), "The name must clear the glyph.");
            });

            var menu = (FAMenuFlyout)border.ContextFlyout!;
            FAToggleMenuFlyoutItem lockItem = menu.Items.OfType<FAToggleMenuFlyoutItem>().Single(item => item.Name == "lockElement");
            // The menu updates its items each time it opens, so check a later opening as well.
            var lockOffered = new List<bool>();
            for (int i = 0; i < 2; i++)
            {
                menu.ShowAt(border);
                HeadlessTestHelpers.Render();
                lockOffered.Add(lockItem.IsEnabled);
                menu.Hide();
                HeadlessTestHelpers.Settle();
            }

            Assert.That(lockOffered, Is.EqualTo(new[] { false, false }), "Locking this clip would not be saved.");

            viewModel.Timeline.SelectElement(viewModel);
            viewModel.Delete.Execute();
            HeadlessTestHelpers.Settle();
            Assert.Multiple(() =>
            {
                Assert.That(editor.Scene.Children, Is.Empty);
                Assert.That(File.ReadAllBytes(elementPath), Is.EqualTo(original),
                    "Deleting a protected clip leaves its retained file alone.");
            });
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    [AvaloniaTest]
    public async Task Property_editors_refuse_edits_but_allow_removing_and_replacing_what_could_not_be_loaded()
    {
        (string projectPath, string elementPath) = await CreateProjectWithUnreadableEasings("recovery-properties");
        EditViewModel editor = await OpenProjectAsync(projectPath);
        Element element = editor.Scene.Children.Single();
        var shape = (RectShape)element.Objects[0];
        EngineObject healthy = element.Objects[1];
        EngineObject unavailable = element.Objects[2];
        Assert.That(element.SuppressedStorageSource, Is.Not.Null);
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        HeadlessTestHelpers.Settle();
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        editor.HistoryManager.Commit("test setup");
        int before = editor.HistoryManager.UndoCount;

        NumberEditorViewModel<float> width = PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<NumberEditorViewModel<float>>()
            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), shape.Width));
        TransformEditorViewModel transform = PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<TransformEditorViewModel>().Single();
        float oldWidth = shape.Width.CurrentValue;
        width.SetValue(oldWidth, oldWidth + 10);
        transform.IsEnabled.Value = !transform.IsEnabled.Value;
        var objectView = new EngineObjectPropertyView { DataContext = tab.Items[1] };
        objectView.Remove_Click(null, new RoutedEventArgs());

        Assert.Multiple(() =>
        {
            Assert.That(tab.CanEdit.Value, Is.False);
            Assert.That(width.CanEdit.Value, Is.False);
            Assert.That(shape.Width.CurrentValue, Is.EqualTo(oldWidth));
            Assert.That(shape.Transform.CurrentValue!.IsEnabled, Is.True, "Toggling the transform would not be saved.");
            Assert.That(tab.Items[0].CanReplace.Value, Is.True, "The shape holds the transform with the unreadable easing.");
            Assert.That(tab.Items[1].CanReplace.Value, Is.False);
            Assert.That(element.Objects, Does.Contain(healthy));
            Assert.That(tab.Items[2].CanReplace.Value, Is.True);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });

        // Replacing the transform is a repair. Another unreadable easing remains, so the element stays
        // protected until the object that holds it is removed too.
        transform.SetNull();
        HeadlessTestHelpers.Settle();
        objectView.DataContext = tab.Items[0];
        objectView.Remove_Click(null, new RoutedEventArgs());
        Assert.Multiple(() =>
        {
            Assert.That(shape.Transform.CurrentValue, Is.Null);
            Assert.That(element.SuppressedStorageSource, Is.Not.Null);
            Assert.That(tab.CanEdit.Value, Is.False);
            Assert.That(element.Objects, Does.Contain(shape), "The shape no longer holds anything to repair.");
        });

        objectView.DataContext = tab.Items.Single(item => item.Model == unavailable);
        objectView.Remove_Click(null, new RoutedEventArgs());
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(element.Objects, Does.Not.Contain(unavailable));
            Assert.That(element.SuppressedStorageSource, Is.Null, "Removing the last unreadable easing completes the repair.");
            Assert.That(tab.CanEdit.Value, Is.True);
            Assert.That(width.CanEdit.Value, Is.True);
        });

        width.SetValue(oldWidth, oldWidth + 10);
        await editor.SaveAsync();
        Element saved = CoreSerializer.RestoreFromUri<Element>(new Uri(elementPath));
        Assert.Multiple(() =>
        {
            Assert.That(((RectShape)saved.Objects[0]).Width.CurrentValue, Is.EqualTo(oldWidth + 10));
            Assert.That(((RectShape)saved.Objects[0]).Transform.CurrentValue, Is.Null);
            Assert.That(saved.Objects, Has.Count.EqualTo(2));
            Assert.That(saved.SuppressedStorageSource, Is.Null);
        });
        await TestReset.ResetShellAsync();
    }

    // A clip whose effect and one of whose objects have types that could not be loaded stays editable, and so do
    // the fallbacks' known members: turning them off is saved, and the rest of them is saved as it was read.
    [AvaloniaTest]
    public async Task Property_editors_keep_a_clip_with_missing_types_editable_and_save_turning_its_fallbacks_off()
    {
        (string projectPath, string elementPath) = await CreateProjectWithMissingTypes("missing-types-properties");
        JsonObject written = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        EditViewModel editor = await OpenProjectAsync(projectPath);
        Element element = editor.Scene.Children.Single();
        var shape = (RectShape)element.Objects[0];
        EngineObject healthy = element.Objects[1];
        EngineObject unavailable = element.Objects[2];
        Assert.That(shape.FilterEffect.CurrentValue, Is.InstanceOf<IFallback>());
        Assert.That(unavailable, Is.InstanceOf<IFallback>());
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        HeadlessTestHelpers.Settle();
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        EngineObjectPropertyViewModel unavailableItem = tab.Items.Single(item => item.Model == unavailable);

        NumberEditorViewModel<float> width = PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<NumberEditorViewModel<float>>()
            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), shape.Width));
        FilterEffectEditorViewModel filter = PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<FilterEffectEditorViewModel>().Single();
        float oldWidth = shape.Width.CurrentValue;
        width.SetValue(oldWidth, oldWidth + 10);
        filter.IsEnabled.Value = false;
        unavailableItem.IsEnabled.Value = false;
        var objectView = new EngineObjectPropertyView { DataContext = tab.Items[1] };
        objectView.Remove_Click(null, new RoutedEventArgs());
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(element.SuppressedStorageSource, Is.Null);
            Assert.That(tab.CanEdit.Value, Is.True);
            Assert.That(shape.Width.CurrentValue, Is.EqualTo(oldWidth + 10));
            Assert.That(shape.FilterEffect.CurrentValue!.IsEnabled, Is.False);
            Assert.That(unavailable.IsEnabled, Is.False);
            Assert.That(unavailableItem.CanToggleEnabled.Value, Is.True);
            Assert.That(element.Objects, Does.Not.Contain(healthy));
        });

        var window = new Window
        {
            Width = 360,
            Height = 220,
            Content = new EngineObjectPropertyView { DataContext = unavailableItem },
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            ToggleButton visibility = window.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "VisibilityButton");
            Assert.That(visibility.IsEnabled, Is.True);
        }
        finally
        {
            window.Close();
        }

        await editor.SaveAsync();
        JsonObject saved = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray writtenObjects = written[nameof(Element.Objects)]!.AsArray();
        JsonArray savedObjects = saved[nameof(Element.Objects)]!.AsArray();
        JsonNode effect = writtenObjects[0]![nameof(RectShape.FilterEffect)]!.DeepClone();
        effect[nameof(EngineObject.IsEnabled)] = false;
        JsonNode turnedOff = writtenObjects[2]!.DeepClone();
        turnedOff[nameof(EngineObject.IsEnabled)] = false;
        Assert.Multiple(() =>
        {
            Assert.That(savedObjects, Has.Count.EqualTo(2));
            Assert.That(savedObjects[0]![nameof(RectShape.Width)]!.GetValue<float>(), Is.EqualTo(oldWidth + 10));
            Assert.That(JsonNode.DeepEquals(savedObjects[0]![nameof(RectShape.FilterEffect)], effect), Is.True,
                "The effect is saved turned off and otherwise as it was read.");
            Assert.That(JsonNode.DeepEquals(savedObjects[1], turnedOff), Is.True,
                "The object is saved turned off and otherwise as it was read.");
        });
        await TestReset.ResetShellAsync();
    }

    // Opening a project whose clip holds types that could not be loaded tells the user once that the project
    // keeps data it cannot show, and names the version that last saved it. No element is read-only.
    [AvaloniaTest]
    public async Task Opening_a_project_with_missing_types_shows_once_that_it_keeps_data_it_cannot_show()
    {
        (string projectPath, _) = await CreateProjectWithMissingTypes("missing-types-notice");
        using var notifications = new NotificationCapture();
        try
        {
            await OpenProjectAsync(projectPath);
            Notification[] kept = notifications.All
                .Where(n => n.Title == MessageStrings.KeptUnknownDataTitle)
                .ToArray();

            Assert.That(kept, Has.Length.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(kept[0].Type, Is.EqualTo(NotificationType.Warning));
                Assert.That(kept[0].Expiration, Is.EqualTo(Timeout.InfiniteTimeSpan));
                Assert.That(kept[0].Message, Does.Contain(BeutlApplication.Version));
                Assert.That(notifications.All.Where(n => n.Title == MessageStrings.RecoveryProtectedElementsTitle),
                    Is.Empty);
                Assert.That(kept[0].CancellationToken.IsCancellationRequested, Is.False);
            });

            await TestReset.ResetShellAsync();
            Assert.That(kept[0].CancellationToken.IsCancellationRequested, Is.True,
                "Closing the project must dismiss its notice.");
        }
        finally
        {
            await TestReset.ResetShellAsync();
        }
    }

    // Removing an animation that holds an easing that could not be read repairs the element, so the
    // property menu offers it although the property is otherwise read-only.
    [AvaloniaTest]
    public async Task Property_menu_offers_removing_the_animation_that_holds_an_unreadable_easing()
    {
        (string projectPath, _) = await CreateProjectWithUnreadableEasing("recovery-easing");
        EditViewModel editor = await OpenProjectAsync(projectPath);
        Element element = editor.Scene.Children.Single();
        var shape = (RectShape)element.Objects.Single();
        Assert.That(element.SuppressedStorageSource, Is.Not.Null);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        NumberEditorViewModel<float> width = NumberEditor(tab, shape.Width);
        NumberEditorViewModel<float> height = NumberEditor(tab, shape.Height);
        var widthMenu = new PropertyEditorMenu { DataContext = width };
        var heightMenu = new PropertyEditorMenu { DataContext = height };
        var window = new Window
        {
            Width = 320,
            Height = 260,
            Content = new StackPanel { Children = { widthMenu, heightMenu } },
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button widthButton = widthMenu.GetVisualDescendants().OfType<Button>().First();
            Button heightButton = heightMenu.GetVisualDescendants().OfType<Button>().First();
            // On a read-only property the button opens the menu instead of editing keyframes.
            Click(window, heightButton);
            bool healthyOffered = MenuItem(heightButton, "removeAnimationItem").IsEnabled;
            heightButton.ContextFlyout!.Hide();
            HeadlessTestHelpers.Settle();
            Click(window, widthButton);
            FAMenuFlyoutItem removeAnimation = MenuItem(widthButton, "removeAnimationItem");

            Assert.Multiple(() =>
            {
                Assert.That(width.CanEditProperty.Value, Is.False);
                Assert.That(removeAnimation.IsEnabled, Is.True);
                Assert.That(MenuItem(widthButton, "editAnimationItem").IsEnabled, Is.False);
                Assert.That(healthyOffered, Is.False, "Removing a healthy animation would not be saved.");
            });

            element.IsLocked = true;
            HeadlessTestHelpers.Settle();
            bool offeredWhileLocked = removeAnimation.IsEnabled;
            element.IsLocked = false;
            HeadlessTestHelpers.Settle();
            removeAnimation.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(offeredWhileLocked, Is.False, "The user's lock still wins.");
                Assert.That(shape.Width.Animation, Is.Null);
                Assert.That(shape.Height.Animation, Is.Not.Null);
                Assert.That(element.SuppressedStorageSource, Is.Null);
            });
        }
        finally
        {
            window.Close();
            await TestReset.ResetShellAsync();
        }
    }

    // Saves a project with one clip holding a rectangle whose width and height are animated, then
    // rewrites the easing of one width keyframe in the clip's file to a type from an assembly that is
    // not loaded, as when the plugin that provides it is not installed.
    private static async Task<(string ProjectPath, string ElementPath)> CreateProjectWithUnreadableEasing(string name)
    {
        var unreadable = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 200 };
        (string projectPath, string elementPath) = await CreateSavedProjectAsync(name, () => new RectShape(), element =>
        {
            var shape = (RectShape)element.Objects.Single();
            var widthAnimation = new KeyFrameAnimation<float>();
            widthAnimation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
            widthAnimation.KeyFrames.Add(unreadable);
            shape.Width.Animation = widthAnimation;
            var heightAnimation = new KeyFrameAnimation<float>();
            heightAnimation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 50 });
            heightAnimation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 80 });
            shape.Height.Animation = heightAnimation;
        });

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        FindById(json, unreadable.Id)![nameof(KeyFrame.Easing)] = MissingEasingType;
        File.WriteAllText(elementPath, json.ToJsonString());
        return (projectPath, elementPath);
    }

    private static JsonObject? FindById(JsonNode? node, Guid id)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj[nameof(CoreObject.Id)] is JsonValue value
                    && value.TryGetValue(out string? text)
                    && Guid.TryParse(text, out Guid parsed)
                    && parsed == id)
                {
                    return obj;
                }

                return obj.Select(pair => FindById(pair.Value, id)).FirstOrDefault(found => found != null);
            case JsonArray array:
                return array.Select(item => FindById(item, id)).FirstOrDefault(found => found != null);
            default:
                return null;
        }
    }

    private static NumberEditorViewModel<float> NumberEditor(ElementPropertyTabViewModel tab, IProperty property)
        => PropertyEditorTestHost.Flatten(tab.Items[0].Properties, includeGroups: false)
            .OfType<NumberEditorViewModel<float>>()
            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), property));

    private static FAMenuFlyoutItem MenuItem(Button button, string name)
        => ((FAMenuFlyout)button.ContextFlyout!).Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == name);

    private static void Click(Window window, Control control)
    {
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }

    // Saves a project with one clip holding a rectangle with a blur, an ellipse and a second rectangle,
    // then rewrites the blur's type and the second rectangle's type in the clip's file to ones from an
    // assembly that is not loaded, as when the plugin that provides them is not installed.
    private static async Task<(string ProjectPath, string ElementPath)> CreateProjectWithMissingTypes(string name)
    {
        (string projectPath, string elementPath) = await CreateSavedProjectAsync(
            name, () => new RectShape { FilterEffect = { CurrentValue = new Blur() } }, element =>
            {
                element.AddObject(new EllipseShape());
                element.AddObject(new RectShape());
            });

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonArray objects = json[nameof(Element.Objects)]!.AsArray();
        objects[0]![nameof(RectShape.FilterEffect)]!.AsObject()["$type"] = "[Missing.Plugin]Missing.Namespace:MissingEffect";
        objects[2]!.AsObject()["$type"] = "[Missing.Plugin]Missing.Namespace:MissingShape";
        File.WriteAllText(elementPath, json.ToJsonString());
        return (projectPath, elementPath);
    }

    // Saves a project with one clip holding a rectangle with a translation, an ellipse and a second rectangle,
    // then gives the translation and the second rectangle a keyframe whose easing type is from an assembly that is
    // not loaded, as when the plugin that provides it is not installed.
    private static async Task<(string ProjectPath, string ElementPath)> CreateProjectWithUnreadableEasings(string name)
    {
        var translation = new TranslateTransform();
        translation.X.Animation = CreateAnimation(0f);
        var second = new RectShape();
        second.Width.Animation = CreateAnimation(100f);
        (string projectPath, string elementPath) = await CreateSavedProjectAsync(
            name, () => new RectShape { Transform = { CurrentValue = translation } }, element =>
            {
                element.AddObject(new EllipseShape());
                element.AddObject(second);
            });
        IKeyFrame[] unreadable =
        [
            ((KeyFrameAnimation<float>)translation.X.Animation!).KeyFrames.Single(),
            ((KeyFrameAnimation<float>)second.Width.Animation!).KeyFrames.Single(),
        ];

        JsonObject json = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        foreach (IKeyFrame keyFrame in unreadable)
        {
            FindById(json, ((CoreObject)keyFrame).Id)![nameof(KeyFrame.Easing)] = MissingEasingType;
        }

        File.WriteAllText(elementPath, json.ToJsonString());
        return (projectPath, elementPath);
    }

    // Saves a project with one clip, named ElementName, that holds what create returns and what configure adds, and
    // closes it.
    private static async Task<(string ProjectPath, string ElementPath)> CreateSavedProjectAsync(
        string name, Func<EngineObject> create, Action<Element> configure)
    {
        (Project project, Scene scene, EditViewModel editor) = await TestShell.OpenNewSceneAsync(name);
        await TestShell.AddElementAsync(editor, create, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Element element = scene.Children.Single();
        element.Name = ElementName;
        configure(element);
        await editor.SaveAsync();
        string projectPath = project.Uri!.LocalPath;
        string elementPath = element.Uri!.LocalPath;
        await TestReset.ResetShellAsync();
        return (projectPath, elementPath);
    }

    private static KeyFrameAnimation<float> CreateAnimation(float value)
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = value });
        return animation;
    }

    private static async Task<EditViewModel> OpenProjectAsync(string projectPath)
    {
        await TestShell.Project.OpenProject(projectPath);
        Scene scene = TestShell.Project.CurrentProject.Value!.Items.OfType<Scene>().Single();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    private static Control Named(Control root, string name)
        => root.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
}
