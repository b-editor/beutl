using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.SoundPannerProperties;
using Beutl.Editor.Components.SoundPannerProperties.ViewModels;
using Beutl.Editor.Components.SoundPannerProperties.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SoundPannerEditorTests
{
    private static PropertyEditorExtension[] Extensions => LoadPrimitiveExtensionTask.PrimitiveExtensions
        .OfType<PropertyEditorExtension>().ToArray();

    [AvaloniaTest]
    public void RegistrationReplacesExactlyTheFourSoundEditorsAndRequiresOneCompleteOwner()
    {
        var sound = new SourceSound();
        List<IPropertyAdapter> adapters = [.. PropertyAdapterFactory.CreateAdapters(sound)];
        var contexts = new List<IPropertyEditorContext>();
        PropertyEditorResolver.CreateContexts(adapters, Extensions, PropertyEditorSurface.Panel, contexts.Add);
        try
        {
            Assert.That(contexts.OfType<SoundPannerViewModel>().Count(), Is.EqualTo(1));
            Assert.That(contexts.OfType<PropertyEditorViewModel>().Any(c => IsPannerProperty(c.PropertyAdapter)), Is.False);
            Assert.That(contexts.OfType<PropertyEditorViewModel>().Any(c => ReferenceEquals(c.PropertyAdapter.GetEngineProperty(), sound.Gain)), Is.True);
        }
        finally { foreach (var context in contexts) context.Dispose(); }

        var registration = PropertyEditorResolver.RegistrationsOf(SoundPannerPropertiesExtension.Instance).Single();
        IPropertyAdapter[] first = PannerAdapters(sound);
        IPropertyAdapter[] second = PannerAdapters(new SourceSound());
        Assert.That(registration.Match!([.. first.Take(3), second[3]]), Is.Empty);
        var listItem = new Mock<IPropertyAdapter>();
        listItem.SetupGet(x => x.ImplementedType).Throws<NotSupportedException>();
        Assert.That(registration.Match!([listItem.Object, .. first]), Is.EquivalentTo(first));
    }

    [AvaloniaTest]
    [TestCase(SpeakerLayout.Surround51, 2, 75f)]
    [TestCase(SpeakerLayout.Surround71, 1, 75f)]
    [TestCase(SpeakerLayout.Stereo, 2, 20f)]
    [TestCase(SpeakerLayout.Surround51, 6, 20f)]
    public void DragCommitsBothAxesOnceOrOnlyPanForHorizontalLayouts(SpeakerLayout layout, int channels, float expectedBack)
    {
        using var host = new PannerHost(layout, channels, sound => sound.SurroundPan.Value = 20);
        host.BeginDrag();
        host.Move(25, 40);
        host.Move(60, 75);
        Assert.That(host.History.UndoCount, Is.Zero, "A drag stays pending until release.");
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(60).Within(0.01));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(expectedBack).Within(0.01));
        host.Release();
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        Assert.That(host.History.HasPendingOperations, Is.False);
        host.History.Undo();
        Assert.That(host.Sound.Pan.Value, Is.Zero);
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(20));
        host.History.Redo();
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(60).Within(0.01));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(expectedBack).Within(0.01));
    }

    [AvaloniaTest]
    [TestCase(false, -1, 0)]
    [TestCase(true, -1, 0)]
    [TestCase(false, 1, 1)]
    [TestCase(true, 1, 1)]
    [TestCase(false, 3, 1)]
    [TestCase(true, 3, 1)]
    public void DragEditsAndDisplaysTheKeyAtOrAfterThePlayhead(bool global, int seconds, int index)
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2, sound =>
        {
            AddKeys(sound.Pan, global, -40, 40);
            AddKeys(sound.SurroundPan, global, 20, 80);
        });
        double origin = global ? 0 : 3;
        host.Time.Value = MediaTime.FromSeconds(origin + seconds);
        HeadlessTestHelpers.Render();
        Assert.That(host.ViewModel.CanDrag.Value, Is.True);
        float expectedPan = index == 0 ? -40 : 40;
        float expectedBack = index == 0 ? 20 : 80;
        Assert.That(host.ViewModel.Pan.Value, Is.EqualTo(expectedPan));
        Assert.That(host.ViewModel.SurroundPan.Value, Is.EqualTo(expectedBack));
        Rect area = host.Panner.PanningBounds;
        Assert.That(host.Panner.PuckCenter.X, Is.EqualTo(area.X + (expectedPan + 100) / 200 * area.Width).Within(0.01));
        Assert.That(host.Panner.PuckCenter.Y, Is.EqualTo(area.Y + expectedBack / 100 * area.Height).Within(0.01));
        var numbers = host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().ToArray();
        Assert.That(numbers.Single(n => ReferenceEquals(n.DataContext, host.ViewModel.PanEditor.Value)).Value, Is.EqualTo(expectedPan));
        Assert.That(numbers.Single(n => ReferenceEquals(n.DataContext, host.ViewModel.SurroundPanEditor.Value)).Value, Is.EqualTo(expectedBack));
        host.Drag(-60, 35);
        Assert.That(GetKey(host.Sound.Pan, index).Value.Value, Is.EqualTo(-60).Within(0.01));
        Assert.That(GetKey(host.Sound.SurroundPan, index).Value.Value, Is.EqualTo(35).Within(0.01));
        Assert.That(GetKey(host.Sound.Pan, 1 - index).Value.Value, Is.EqualTo(index == 0 ? 40 : -40));
        Assert.That(GetKey(host.Sound.SurroundPan, 1 - index).Value.Value, Is.EqualTo(index == 0 ? 80 : 20));
        Assert.That(host.Sound.Pan.Value, Is.Zero);
        Assert.That(host.Sound.SurroundPan.Value, Is.Zero);
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        host.History.Undo();
        Assert.That(GetKey(host.Sound.Pan, index).Value.Value, Is.EqualTo(expectedPan));
        Assert.That(GetKey(host.Sound.SurroundPan, index).Value.Value, Is.EqualTo(expectedBack));
        host.Time.Value = MediaTime.FromSeconds(origin);
        HeadlessTestHelpers.Render();
        Assert.That(host.ViewModel.CanDrag.Value, Is.True);
    }

    [AvaloniaTest]
    public void KeyFrameCollectionChangesRefreshThePuckWithoutSeeking([Values] bool surround, [Values] bool last)
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2,
            sound => AddKeys(surround ? sound.SurroundPan : sound.Pan, false, 10, 90));
        var property = surround ? host.Sound.SurroundPan : host.Sound.Pan;
        var animation = property.GetKeyFrameAnimation()!;
        var time = MediaTime.FromSeconds(last ? 5 : 3);
        host.Time.Value = time;
        AssertDisplayed(last ? 90 : 10);

        AnimationOperations.RemoveKeyFrame(animation, animation.KeyFrames[last ? 1 : 0], NullLogger.Instance);
        AssertDisplayed(last ? 10 : 90);

        var added = new KeyFrame<float>
        {
            KeyTime = { Value = animation.ToLocal(time) }, Value = { Value = 45 }
        };
        animation.KeyFrames.Add(added, out _);
        AssertDisplayed(45);
        host.Drag(60, 65);
        Assert.That(added.Value.Value, Is.EqualTo(surround ? 65 : 60).Within(0.01));
        Assert.That(animation.KeyFrames.Single(k => !ReferenceEquals(k, added)).Value, Is.EqualTo(last ? 10f : 90f));

        void AssertDisplayed(float value)
        {
            HeadlessTestHelpers.Render();
            Assert.That(host.Time.Value, Is.EqualTo(time));
            Assert.That(surround ? host.ViewModel.SurroundPan.Value : host.ViewModel.Pan.Value, Is.EqualTo(value));
            Rect area = host.Panner.PanningBounds;
            Assert.That(surround ? host.Panner.PuckCenter.Y : host.Panner.PuckCenter.X,
                Is.EqualTo(surround ? area.Y + value / 100 * area.Height : area.X + (value + 100) / 200 * area.Width).Within(0.01));
        }
    }

    [AvaloniaTest]
    public void KeyFrameCollectionSubscriptionsFollowAnimationReplacementAndRemoval([Values] bool surround)
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2, sound =>
        {
            var property = surround ? sound.SurroundPan : sound.Pan;
            property.Value = 7;
            AddKeys(property, false, 10, 90);
        });
        var property = surround ? host.Sound.SurroundPan : host.Sound.Pan;
        var displayed = surround ? host.ViewModel.SurroundPan : host.ViewModel.Pan;
        AddKeys(property, false, 30, 70);
        Assert.That(displayed.Value, Is.EqualTo(30));
        var replacement = property.GetKeyFrameAnimation()!;
        AnimationOperations.RemoveKeyFrame(replacement, replacement.KeyFrames[0], NullLogger.Instance);
        Assert.That(displayed.Value, Is.EqualTo(70));

        property.SetKeyFrameAnimation(null);
        Assert.That(displayed.Value, Is.EqualTo(7));
        AddKeys(property, false, 20, 80);
        var reattached = property.GetKeyFrameAnimation()!;
        AnimationOperations.RemoveKeyFrame(reattached, reattached.KeyFrames[0], NullLogger.Instance);
        Assert.That(displayed.Value, Is.EqualTo(80));
        Assert.That(host.Time.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void AnExpressionDisablesDraggingAndShowsItsEvaluatedValue(bool surround)
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2, sound =>
        {
            var property = surround ? sound.SurroundPan : sound.Pan;
            AddKeys(property, false, 10, 90);
            property.Value = 7;
            property.Expression = new StringExpression<float>("(float)Time * 10");
        });
        Assert.That(host.ViewModel.CanDrag.Value, Is.False);
        // Expression time is local to the element, which starts at 3s.
        Assert.That(surround ? host.ViewModel.SurroundPan.Value : host.ViewModel.Pan.Value, Is.Zero);
        host.Time.Value = MediaTime.FromSeconds(4);
        HeadlessTestHelpers.Render();
        Assert.That(surround ? host.ViewModel.SurroundPan.Value : host.ViewModel.Pan.Value, Is.EqualTo(10));
        host.Drag(70, 90);
        var editedProperty = surround ? host.Sound.SurroundPan : host.Sound.Pan;
        Assert.That(editedProperty.Value, Is.EqualTo(7));
        Assert.That(GetKey(editedProperty, 0).Value.Value, Is.EqualTo(10));
        Assert.That(GetKey(editedProperty, 1).Value.Value, Is.EqualTo(90));
        Assert.That(host.History.UndoCount, Is.Zero);
        Assert.That(host.History.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase(SpeakerLayout.Surround51)]
    [TestCase(SpeakerLayout.Surround71)]
    public void GainEditsAndSeekingDoNotReadTheClipLayout(SpeakerLayout layout)
    {
        int calls = 0;
        var probe = new Mock<Sound> { CallBase = true };
        probe.Setup(s => s.GetClipLayout(It.IsAny<AudioChannelLayout>(), It.IsAny<MediaTime>())).Returns(() =>
        {
            calls++;
            return AudioChannelLayout.Stereo;
        });
        Sound sound = probe.Object;
        var project = new Project { ChannelLayout = { Value = layout } };
        var scene = new Scene();
        var element = new Element { Objects = { sound } };
        scene.Children.Add(element);
        project.Items.Add(scene);
        ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(project);
        using var vm = new SoundPannerViewModel(PannerAdapters(sound));
        int initialCalls = calls;
        Assert.That(initialCalls, Is.GreaterThan(0));

        for (int i = 0; i < 10; i++) sound.Gain.Value = 50 + i;
        sound.Pan.Value = 20;
        sound.SurroundPan.Value = 30;
        element.Start.Value = MediaTime.FromSeconds(3);
        for (int i = 0; i < 10; i++) vm.CurrentTime.Value = MediaTime.FromSeconds(i);
        element.IsLocked.Value = true;
        element.IsLocked.Value = false;
        Assert.That(calls, Is.EqualTo(initialCalls));
        Assert.That(vm.Pan.Value, Is.EqualTo(20));
        Assert.That(vm.SurroundPan.Value, Is.EqualTo(30));

        project.ChannelLayout.Value = layout == SpeakerLayout.Surround51 ? SpeakerLayout.Surround71 : SpeakerLayout.Surround51;
        Assert.That(calls, Is.EqualTo(initialCalls + 1));
    }

    [AvaloniaTest]
    [TestCase(SpeakerLayout.Surround51, 2, false)]
    [TestCase(SpeakerLayout.Stereo, 2, false)]
    [TestCase(SpeakerLayout.Surround51, 6, false)]
    [TestCase(SpeakerLayout.Surround51, 2, true)]
    public void DoubleClickResetsBothAxesInOneUndoStep(SpeakerLayout layout, int channels, bool animated)
    {
        using var host = new PannerHost(layout, channels, sound =>
        {
            if (animated)
            {
                AddKeys(sound.Pan, false, 45, 80);
                AddKeys(sound.SurroundPan, false, 65, 90);
            }
            else
            {
                sound.Pan.Value = 45;
                sound.SurroundPan.Value = 65;
            }
        });
        Point point = host.Panner.TranslatePoint(host.Panner.PuckCenter, host.Window)!.Value;
        host.Window.MouseMove(point);
        for (int i = 0; i < 2; i++)
        {
            host.Window.MouseDown(point, MouseButton.Left);
            host.Window.MouseUp(point, MouseButton.Left);
            if (i == 0) Assert.That(host.History.UndoCount, Is.Zero);
        }
        Assert.That(host.ViewModel.Pan.Value, Is.Zero);
        Assert.That(host.ViewModel.SurroundPan.Value, Is.Zero);
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        host.History.Undo();
        Assert.That(host.ViewModel.Pan.Value, Is.EqualTo(45));
        Assert.That(host.ViewModel.SurroundPan.Value, Is.EqualTo(65));
        if (animated)
        {
            Assert.That(GetKey(host.Sound.Pan, 1).Value.Value, Is.EqualTo(80));
            Assert.That(GetKey(host.Sound.SurroundPan, 1).Value.Value, Is.EqualTo(90));
        }
    }

    [AvaloniaTest]
    public void LayoutAndChannelMapChangesUpdateEnabledControlsWithoutChangingValues()
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 6);
        Assert.That(host.ViewModel.IsHorizontalOnly.Value, Is.True);
        Assert.That(host.ViewModel.Message.Value, Is.EqualTo(AudioStrings.Panner_Multichannel));
        using (host.History.SuppressRecording())
        {
            host.Sound.ChannelMap.Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, AudioChannelLayout.Surround51);
            HeadlessTestHelpers.Render();
            Assert.That(host.ViewModel.IsHorizontalOnly.Value, Is.False);
            Assert.That(host.ViewModel.CanPosition.Value, Is.True);
            host.Project.ChannelLayout.Value = SpeakerLayout.Mono;
            HeadlessTestHelpers.Render();
            Assert.That(host.Panner.IsEffectivelyEnabled, Is.False);
            Assert.That(host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().All(n => !n.IsEffectivelyEnabled), Is.True);
            host.Drag(80, 90);
            Assert.That(host.Sound.Pan.Value, Is.Zero);
            Assert.That(host.Sound.SurroundPan.Value, Is.Zero);
            host.Project.ChannelLayout.Value = SpeakerLayout.Stereo;
            HeadlessTestHelpers.Render();
            Assert.That(host.ViewModel.CanPan.Value, Is.True);
            Assert.That(host.ViewModel.CanPosition.Value, Is.False);
            host.Project.ChannelLayout.Value = SpeakerLayout.Surround71;
            HeadlessTestHelpers.Render();
            Assert.That(host.ViewModel.Layout.Value, Is.EqualTo(SpeakerLayout.Surround71));
            Assert.That(host.ViewModel.CanPosition.Value, Is.True);
            var source = host.Sound.Source.Value;
            host.Sound.Source.Value = null;
            Assert.That(host.ViewModel.IsHorizontalOnly.Value, Is.True);
            host.Sound.Source.Value = source;
            Assert.That(host.ViewModel.CanPosition.Value, Is.True);
        }
        Assert.That(host.History.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public void AChannelMapExpressionEnablesPositioningAtTheCurrentTime()
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 6);
        Assert.That(host.ViewModel.IsHorizontalOnly.Value, Is.True);
        host.Time.Value = MediaTime.FromSeconds(5);
        using (host.History.SuppressRecording())
        {
            host.Sound.ChannelMap.Expression = new StringExpression<AudioChannelMap>(
                "Time < 2 ? Beutl.Audio.AudioChannelMap.Auto : Beutl.Audio.AudioChannelMap.CreateDefault(Beutl.Audio.AudioChannelFormat.Stereo, Beutl.Audio.AudioChannelLayout.Surround51)");
        }
        HeadlessTestHelpers.Render();
        Assert.That(host.Sound.ChannelMap.Value, Is.EqualTo(AudioChannelMap.Auto));
        Assert.That(host.ViewModel.IsHorizontalOnly.Value, Is.False);
        Assert.That(host.ViewModel.CanPosition.Value, Is.True);
        Assert.That(host.ViewModel.CanDrag.Value, Is.True);
        Assert.That(host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().All(n => n.IsEffectivelyEnabled), Is.True);
        host.Drag(60, 75);
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(60).Within(0.01));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(75).Within(0.01));
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    [TestCase("element")]
    [TestCase("layer")]
    [TestCase("recovery")]
    public void ProtectedElementsRejectDraggingAndResetUntilUnlocked(string protection)
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2,
            sound => { sound.Pan.Value = 45; sound.SurroundPan.Value = 65; });
        var element = host.Sound.FindHierarchicalParent<Element>()!;
        var scene = element.FindHierarchicalParent<Scene>()!;
        var layer = new TimelineLayer { ZIndex = { Value = element.ZIndex.Value } };
        scene.Layers.Add(layer);
        SetProtected(true);
        HeadlessTestHelpers.Render();
        Assert.That(host.ViewModel.CanPan.Value, Is.False);
        Assert.That(host.ViewModel.CanDrag.Value, Is.False);
        Assert.That(host.Panner.IsEffectivelyEnabled, Is.False);
        host.Drag(-60, 35);
        DoubleClick();

        // Writes must also reject a stale enabled state.
        host.ViewModel.CanPan.Value = true;
        host.ViewModel.CanDrag.Value = true;
        Assert.That(host.ViewModel.SetPosition(-60, 35), Is.False);
        host.ViewModel.ResetPosition();
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(45));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(65));
        Assert.That(host.History.UndoCount, Is.Zero);
        Assert.That(host.History.HasPendingOperations, Is.False);

        host.ViewModel.CanPan.Value = false;
        host.ViewModel.CanDrag.Value = false;
        SetProtected(false);
        HeadlessTestHelpers.Render();
        Assert.That(host.ViewModel.CanPan.Value, Is.True);
        Assert.That(host.ViewModel.CanDrag.Value, Is.True);
        Assert.That(host.Panner.IsEffectivelyEnabled, Is.True);
        host.Drag(-60, 35);
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(-60).Within(0.01));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(35).Within(0.01));
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        DoubleClick();
        Assert.That(host.Sound.Pan.Value, Is.Zero);
        Assert.That(host.Sound.SurroundPan.Value, Is.Zero);
        Assert.That(host.History.UndoCount, Is.EqualTo(2));

        void SetProtected(bool value)
        {
            switch (protection)
            {
                case "element": element.IsLocked.Value = value; break;
                case "layer": layer.IsLocked.Value = value; break;
                case "recovery":
                    element.SuppressedStorageSource = value
                        ? new SuppressedStorageSource([], new Uri(Path.Combine(Path.GetTempPath(), "panner-protected.belm"))) : null;
                    break;
            }
        }

        void DoubleClick()
        {
            Point point = host.Panner.TranslatePoint(host.Panner.PuckCenter, host.Window)!.Value;
            host.Window.MouseMove(point);
            for (int i = 0; i < 2; i++)
            {
                host.Window.MouseDown(point, MouseButton.Left);
                host.Window.MouseUp(point, MouseButton.Left);
            }
        }
    }

    [AvaloniaTest]
    public void DragClampsAndTheLevelNumberEditorsRemainEditable()
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2);
        host.BeginDrag();
        host.Move(300, 200);
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(100));
        Assert.That(host.Sound.SurroundPan.Value, Is.EqualTo(100));
        host.Move(-300, -100);
        host.Release();
        Assert.That(host.Sound.Pan.Value, Is.EqualTo(-100));
        Assert.That(host.Sound.SurroundPan.Value, Is.Zero);
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        EditNumber(host.ViewModel.CenterLevelEditor.Value!, "65");
        EditNumber(host.ViewModel.LfeLevelEditor.Value!, "30");
        Assert.That(host.Sound.CenterLevel.Value, Is.EqualTo(65));
        Assert.That(host.Sound.LfeLevel.Value, Is.EqualTo(30));
        Assert.That(host.History.UndoCount, Is.EqualTo(3));

        void EditNumber(IPropertyEditorContext vm, string value)
        {
            var number = host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().Single(n => ReferenceEquals(n.DataContext, vm));
            var input = number.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.SelectAll();
            host.Window.KeyTextInput(value);
            host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().First()
                .GetVisualDescendants().OfType<TextBox>().Single().Focus();
            HeadlessTestHelpers.Render();
        }
    }

    [AvaloniaTest]
    public void SeekingDuringDragDoesNotEditTheNewKeyOrLeavePendingHistory()
    {
        using var host = new PannerHost(SpeakerLayout.Surround51, 2, sound => AddKeys(sound.Pan, true, -40, 40));
        host.Time.Value = MediaTime.Zero;
        HeadlessTestHelpers.Render();
        host.BeginDrag();
        host.Move(-10, 25);
        host.Time.Value = MediaTime.FromSeconds(2);
        host.Move(80, 80);
        host.Release();
        Assert.That(GetKey(host.Sound.Pan, 0).Value.Value, Is.EqualTo(-10).Within(0.01));
        Assert.That(GetKey(host.Sound.Pan, 1).Value.Value, Is.EqualTo(40));
        Assert.That(host.History.UndoCount, Is.EqualTo(1));
        Assert.That(host.History.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase("en-US", SpeakerLayout.Stereo, 2, 400)]
    [TestCase("en-US", SpeakerLayout.Surround51, 2, 400)]
    [TestCase("en-US", SpeakerLayout.Surround71, 2, 400)]
    [TestCase("en-US", SpeakerLayout.Surround51, 6, 400)]
    [TestCase("en-US", SpeakerLayout.Mono, 2, 400)]
    [TestCase("ja-JP", SpeakerLayout.Stereo, 2, 280)]
    [TestCase("ja-JP", SpeakerLayout.Surround51, 2, 280)]
    [TestCase("ja-JP", SpeakerLayout.Surround71, 2, 280)]
    [TestCase("ja-JP", SpeakerLayout.Surround51, 6, 280)]
    [TestCase("ja-JP", SpeakerLayout.Mono, 2, 280)]
    [TestCase("es-ES", SpeakerLayout.Surround51, 6, 280)]
    [TestCase("ko-KR", SpeakerLayout.Surround51, 6, 280)]
    [TestCase("zh-CN", SpeakerLayout.Surround51, 6, 280)]
    public void RenderProjectLayouts(string culture, SpeakerLayout layout, int channels, int width)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            using var host = new PannerHost(layout, channels,
                sound => { sound.Pan.Value = 20; sound.SurroundPan.Value = 35; }, width);
            var numbers = host.Editor.GetVisualDescendants().OfType<NumberEditor<float>>().ToArray();
            Assert.That(numbers.Length, Is.EqualTo(4));
            Assert.That(numbers.Count(n => n.IsEffectivelyEnabled), Is.EqualTo(layout == SpeakerLayout.Mono ? 0
                : layout == SpeakerLayout.Stereo || channels >= 3 ? 1 : 4));
            foreach (var number in numbers)
            {
                var input = number.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.That(input.Bounds.Width, Is.GreaterThan(25));
                Assert.That(input.TranslatePoint(new Point(input.Bounds.Width, 0), host.Window)!.Value.X, Is.LessThanOrEqualTo(width));
                Assert.That(input.TranslatePoint(new Point(0, input.Bounds.Height), host.Window)!.Value.Y, Is.LessThanOrEqualTo(host.Window.Height - 12));
            }
            HeadlessCapture.SaveIfRequested(host.Window, nameof(SoundPannerEditorTests), $"{layout}-{channels}ch-{culture}-{width}");
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    private static bool IsPannerProperty(IPropertyAdapter adapter) => adapter.GetEngineProperty()?.Name is
        nameof(Sound.Pan) or nameof(Sound.SurroundPan) or nameof(Sound.CenterLevel) or nameof(Sound.LfeLevel);

    private static IPropertyAdapter[] PannerAdapters(Sound sound)
        => PropertyAdapterFactory.CreateAdapters(sound).Where(IsPannerProperty).ToArray();

    private static KeyFrame<float> GetKey(IExpressionProperty<float> property, int index)
        => (KeyFrame<float>)property.GetKeyFrameAnimation()!.KeyFrames[index];

    private static void AddKeys(IExpressionProperty<float> property, bool global, float first, float last)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = { Value = global } };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = first } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = last } });
        property.SetKeyFrameAnimation(animation);
    }

    private sealed class PannerHost : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"beutl-panner-{Guid.NewGuid():N}.wav");
        private readonly WaveDecoderInfo _decoder = new();
        private readonly PropertyEditorTestHost.HistoryScope _history;
        private Point _pointer;

        public PannerHost(SpeakerLayout layout, int channels, Action<SourceSound>? configure = null, int width = 400)
        {
            ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(Project);
            DecoderRegistry.Register(_decoder);
            using (var writer = new BinaryWriter(File.Create(_path)))
            {
                int bytes = 128 * channels * sizeof(float);
                writer.Write("RIFF"u8);
                writer.Write(60 + bytes);
                writer.Write("WAVEfmt "u8);
                writer.Write(40);
                writer.Write((ushort)0xFFFE);
                writer.Write((ushort)channels);
                writer.Write(44100);
                writer.Write(44100 * channels * sizeof(float));
                writer.Write((ushort)(channels * sizeof(float)));
                writer.Write((ushort)32);
                writer.Write((ushort)22);
                writer.Write((ushort)32);
                writer.Write((uint)AudioChannelLayout.GetDefault(channels).Speakers);
                writer.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());
                writer.Write("data"u8);
                writer.Write(bytes);
                writer.Write(new byte[bytes]);
            }
            Sound = new SourceSound { Source = { Value = SoundSource.Open(_path) } };
            configure?.Invoke(Sound);
            var element = new Element { Start = { Value = MediaTime.FromSeconds(3) }, Length = { Value = MediaTime.FromSeconds(10) } };
            element.Objects.Add(Sound);
            var scene = new Scene();
            scene.Children.Add(element);
            Project.Items.Add(scene);
            Project.ChannelLayout.Value = layout;
            _history = new PropertyEditorTestHost.HistoryScope(Sound);
            var contexts = new List<IPropertyEditorContext>();
            PropertyEditorResolver.CreateContexts(PannerAdapters(Sound).ToList(), Extensions, PropertyEditorSurface.Panel, contexts.Add);
            ViewModel = (SoundPannerViewModel)contexts.Single();
            var clock = new Mock<IEditorClock>();
            clock.SetupGet(c => c.CurrentTime).Returns(Time);
            clock.Setup(c => c.Seek(It.IsAny<MediaTime>())).Callback<MediaTime>(time => Time.Value = time);
            var factory = new Mock<IPropertyEditorFactory>();
            factory.Setup(f => f.CreateEditor(It.IsAny<IPropertyAdapter>())).Returns((IPropertyAdapter p) =>
                PropertyEditorResolver.CreateContext(p, Extensions, PropertyEditorSurface.Panel));
            ViewModel.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(HistoryManager), History),
                (typeof(Element), element), (typeof(IEditorClock), clock.Object), (typeof(IPropertyEditorFactory), factory.Object)));
            Assert.That(PropertyEditorHost.TryCreateControl(ViewModel, PropertyEditorSurface.Panel, out var control), Is.True);
            Editor = (SoundPannerEditor)control!;
            Editor.DataContext = ViewModel;
            PropertyEditorGrid.SetIsAlignmentScope(Editor, true);
            Window = new Window
            {
                Content = new Border { Padding = new Thickness(12), Child = Editor }, Width = width, Height = 540,
                RequestedThemeVariant = ThemeVariant.Dark
            };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Panner = Editor.GetVisualDescendants().OfType<SoundPanner>().Single();
        }

        public Project Project { get; } = new();
        public SourceSound Sound { get; }
        public ReactivePropertySlim<MediaTime> Time { get; } = new(MediaTime.FromSeconds(3));
        public HistoryManager History => _history.History;
        public SoundPannerViewModel ViewModel { get; }
        public SoundPannerEditor Editor { get; }
        public SoundPanner Panner { get; }
        public Window Window { get; }

        public void BeginDrag()
        {
            _pointer = Panner.TranslatePoint(Panner.PuckCenter, Window)!.Value;
            Window.MouseMove(_pointer);
            Window.MouseDown(_pointer, MouseButton.Left);
        }

        public void Move(float pan, float back)
        {
            Rect area = Panner.PanningBounds;
            _pointer = Panner.TranslatePoint(new Point(area.X + (pan + 100) / 200 * area.Width, area.Y + back / 100 * area.Height), Window)!.Value;
            Window.MouseMove(_pointer, RawInputModifiers.LeftMouseButton);
        }

        public void Release()
        {
            Window.MouseUp(_pointer, MouseButton.Left);
            HeadlessTestHelpers.Render();
        }

        public void Drag(float pan, float back)
        {
            BeginDrag();
            Move(pan, back);
            Release();
        }

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
            _history.Dispose();
            Time.Dispose();
            DecoderRegistry.Unregister(_decoder);
            File.Delete(_path);
        }
    }
}
