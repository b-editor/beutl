using Beutl.Animation;
using Beutl.Audio;
using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;
using static Beutl.UnitTests.TestInfrastructure.TrimTestMedia;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementSlipServiceTests
{
    private SceneHistoryHarness _harness = null!;
    private Scene _scene = null!;
    private HistoryManager _history = null!;
    private ElementSlipService _service = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [SetUp]
    public void Setup()
    {
        _harness = new SceneHistoryHarness("beutl_slip", start: MediaTime.Zero, duration: MediaTime.FromSeconds(30));
        _scene = _harness.Scene;
        _history = _harness.History;
        _service = new ElementSlipService(_history);
    }

    [TearDown]
    public void TearDown()
    {
        _harness.Dispose();
    }

    private Element AddElement(MediaTime start, MediaTime length, int zIndex = 0)
        => _harness.AddElement(start, length, zIndex);

    // Adds the SourceVideo, SourceSound or SceneSound that mediaType names without a source, so its length is unknown.
    private static IProperty<MediaTime> AddMediaWithoutSource(Element element, string mediaType)
    {
        if (mediaType == nameof(SourceVideo))
        {
            var video = new SourceVideo();
            element.Objects.Add(video);
            return video.OffsetPosition;
        }

        Sound sound = mediaType == nameof(SourceSound) ? new SourceSound() : new SceneSound();
        element.Objects.Add(sound);
        return sound.OffsetPosition;
    }

    [Test]
    public void Constructor_NullHistoryManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ElementSlipService(null!));
    }

    [Test]
    public void Slip_NullScene_Throws()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Assert.Throws<ArgumentNullException>(() => _service.Slip(null!, [element], MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Slip_NullElements_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Slip(_scene, null!, MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Slip_ElementsContainingNull_Throws()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        Assert.Throws<ArgumentNullException>(() => _service.Slip(_scene, [element, null!], MediaTime.FromSeconds(1)));
    }

    [Test]
    public void Slip_LockedElement_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        element.Objects.Add(new SourceVideo());
        element.IsLocked.Value = true;
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_LockedLayer_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 3);
        element.Objects.Add(new SourceVideo());
        _scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_ElementNotInScene_NoCommit()
    {
        var element = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };
        element.Objects.Add(new SourceVideo());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_ZeroDelta_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        element.Objects.Add(new SourceVideo());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_NoSplittableMedia_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(nameof(SourceVideo), 1.0)]
    [TestCase(nameof(SourceSound), 0.5)]
    [TestCase(nameof(SceneSound), 1.0)]
    public void Slip_MediaWithoutSource_ShiftsOffsetPositionAndCommits(string mediaType, double delta)
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        IProperty<MediaTime> offset = AddMediaWithoutSource(element, mediaType);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(delta));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(offset.Value, Is.EqualTo(MediaTime.FromSeconds(delta)));
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(nameof(SourceVideo))]
    [TestCase(nameof(SourceSound))]
    [TestCase(nameof(SceneSound))]
    public void Slip_ClampsToUsableSourceDuration(string mediaType)
    {
        // A 3s source with a 2s clip leaves 1s of headroom.
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var media = AddMedia(element, mediaType, 100, durationSeconds: 3);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(media.Offset.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [TestCase(2, 1, false)]
    [TestCase(3, 1, false)]
    [TestCase(2, -1, true)]
    [TestCase(3, -1, true)]
    public void Slip_ExhaustedVideo_PreservesKnownSourceBounds(int offset, int delta, bool expectedApplied)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(1));
        var source = CreateVideoSource(2);
        var video = new SourceVideo
        {
            Source = { Value = source },
            OffsetPosition = { Value = MediaTime.FromSeconds(offset) }
        };
        element.Objects.Add(video);
        int before = _history.UndoCount;
        Assert.That(video.TryGetOriginalDuration(out _), Is.False);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(delta));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedApplied));
            Assert.That(video.OffsetPosition.Value,
                Is.EqualTo(MediaTime.FromSeconds(expectedApplied ? offset + delta : offset)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedApplied ? 1 : 0)));
        });
    }

    [Test]
    public void Slip_SubFrameSourceTail_ClampsToWholeSourceFlicks()
    {
        // At half speed an odd length of flicks uses a source span that ends half a flick past a whole one.
        Element element = AddElement(MediaTime.Zero, new MediaTime(23_519_977));
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 0.05)));
        var sound = new SourceSound { Source = { Value = source }, Speed = { Value = 50 } };
        var linked = new SourceSound();
        element.Objects.Add(sound);
        element.Objects.Add(linked);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(new MediaTime(23_520_011)));
            Assert.That(linked.OffsetPosition.Value, Is.EqualTo(new MediaTime(47_040_022)));
        });
    }

    [Test]
    public void Slip_FallbackSound_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        element.Objects.Add(new FallbackSound());
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_SoundGroup_ShiftsSourceChildrenAndCommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var source = new SourceSound();
        var group = new SoundGroup();
        group.Children.Add(source);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(0.5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(source.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(group.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_MultipleMedia_ShiftsAllAndCommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        var sound = new SourceSound();
        element.Objects.Add(video);
        element.Objects.Add(sound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_NegativeDelta_ShiftsBackward()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(3);
        element.Objects.Add(video);

        _service.Slip(_scene, [element], MediaTime.FromSeconds(-1));

        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [Test]
    public void Slip_NegativeDeltaAtZero_NoCommit()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [Test]
    public void Slip_NegativeDeltaPastZero_ClampsAndCommitsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(0.5);
        element.Objects.Add(video);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(-1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_UndoRestoresOffsetPosition()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);
        _service.Slip(_scene, [element], MediaTime.FromSeconds(1));
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));

        _history.Undo();

        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
    }

    [Test]
    public void Slip_LinkedStreamsWithDifferentBounds_ShiftsAllByTheTighterDelta()
    {
        // Video source allows a 1s offset (3s source - 2s element); audio source allows 3s
        // (5s - 2s). A +5s request must land both at the tighter 1s so the streams stay in sync.
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var videoSource = CreateVideoSource(3);
        var video = new SourceVideo { Source = { Value = videoSource } };
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
        var sound = new SourceSound { Source = { Value = soundSource } };
        element.Objects.Add(video);
        element.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [TestCase(nameof(DrawableGroup))]
    [TestCase(nameof(DrawablePresenter))]
    [TestCase(nameof(DrawableTimeController))]
    public void Slip_VideoNestedInContainer_ShiftsOffsetPositionAndCommits(string container)
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        Drawable holder;
        switch (container)
        {
            case nameof(DrawableGroup):
                var group = new DrawableGroup();
                group.Children.Add(video);
                holder = group;
                break;
            case nameof(DrawablePresenter):
                AddElement(element.Start.Value, element.Length.Value).Objects.Add(new DrawableGroup { Children = { video } });
                holder = new DrawablePresenter { Target = { Value = video } };
                break;
            default:
                AddElement(element.Start.Value, element.Length.Value).Objects.Add(new DrawableGroup { Children = { video } });
                holder = new DrawableTimeController { Target = { Value = video } };
                break;
        }

        element.Objects.Add(holder);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(false, 100f, 1, 1)]
    [TestCase(false, 100f, 5, 1)]
    [TestCase(true, 100f, 5, 1)]
    [TestCase(false, 50f, 20, 3)]
    [TestCase(true, 50f, 20, 3)]
    public void Slip_NestedVideo_ClampsToRenderedParentWindow(bool presenter, float speed, double delta, double expectedOffset)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            Speed = { Value = speed },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2) }
        };
        Drawable container;
        if (presenter)
        {
            AddElement(element.Start.Value, element.Length.Value).Objects.Add(new DrawableGroup { Children = { video } });
            container = new DrawablePresenter { Target = { Value = video } };
        }
        else
        {
            var group = new DrawableGroup();
            group.Children.Add(video);
            container = group;
        }
        element.Objects.Add(container);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(delta));
        using var resource = container.ToResource(new CompositionContext(MediaTime.FromSeconds(3.9)));
        var sampled = resource switch
        {
            DrawableGroup.Resource group => (SourceVideo.Resource)group.Children.Single(),
            DrawablePresenter.Resource wrapper => (SourceVideo.Resource)wrapper.Target!,
            _ => throw new InvalidOperationException()
        };

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(video.TimeRange.Value, Is.EqualTo(TimeRange.FromSeconds(2)));
            Assert.That(element.Range, Is.EqualTo(TimeRange.FromSeconds(4)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });

        _history.Undo();
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        _history.Redo();
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
    }

    [TestCase(100f, 1, 1)]
    [TestCase(100f, 5, 3)]
    [TestCase(50f, 20, 4)]
    [TestCase(200f, 20, 1)]
    public void Slip_NestedSound_ClampsToOwnVisibleDuration(float speed, double delta, double expectedOffset)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        var source = new SoundSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
        var sound = new SourceSound
        {
            Source = { Value = source },
            Speed = { Value = speed },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2, 2) }
        };
        var group = new SoundGroup();
        group.Children.Add(sound);
        element.Objects.Add(group);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(delta));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(sound.TimeRange.Value, Is.EqualTo(TimeRange.FromSeconds(2, 2)));
            Assert.That(element.Range, Is.EqualTo(TimeRange.FromSeconds(10)));
        });
    }

    [TestCase(50f, 4)]
    [TestCase(100f, 3)]
    [TestCase(200f, 1)]
    public void Slip_NestedSceneSound_ClampsToOwnVisibleDuration(float speed, double expectedOffset)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        var sound = new SceneSound
        {
            ReferencedScene = { Value = new Scene { Duration = { Value = MediaTime.FromSeconds(5) } } },
            Speed = { Value = speed },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2, 2) }
        };
        var group = new SoundGroup();
        group.Children.Add(sound);
        element.Objects.Add(group);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(sound.TimeRange.Value, Is.EqualTo(TimeRange.FromSeconds(2, 2)));
        });
    }

    [TestCase("video", 0, 0, 10, 3)]
    [TestCase("sound", 0, 0, 10, 3)]
    [TestCase("scene", 0, 0, 10, 3)]
    [TestCase("video", 2, 1, 10, 2)]
    [TestCase("sound", 2, 1, 10, 2)]
    [TestCase("scene", 2, 1, 10, 2)]
    [TestCase("video", 2, 3, 10, 0)]
    [TestCase("video", 0, 3, 10, 1)]
    [TestCase("sound", 2, 3, 10, 4)]
    [TestCase("scene", 2, 3, 10, 4)]
    public void Slip_PartiallyVisibleNestedMedia_ClampsToVisibleSourceEnd(
        string kind, int elementStart, int sourceStart, int sourceLength, int expectedOffset)
    {
        Element element = AddElement(MediaTime.FromSeconds(elementStart), MediaTime.FromSeconds(2));
        EngineObject media;
        IProperty<MediaTime> offset;
        if (kind == "video")
        {
            var source = CreateVideoSource(5);
            var video = new SourceVideo { Source = { Value = source } };
            var group = new DrawableGroup();
            group.Children.Add(video);
            element.Objects.Add(group);
            media = video;
            offset = video.OffsetPosition;
        }
        else
        {
            Sound sound;
            if (kind == "sound")
            {
                var source = new SoundSource();
                source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
                sound = new SourceSound { Source = { Value = source } };
            }
            else
            {
                sound = new SceneSound
                {
                    ReferencedScene = { Value = new Scene { Duration = { Value = MediaTime.FromSeconds(5) } } }
                };
            }

            var group = new SoundGroup();
            group.Children.Add(sound);
            element.Objects.Add(group);
            media = sound;
            offset = sound.OffsetPosition;
        }

        media.IsTimeAnchor.Value = true;
        media.TimeRange.Value = TimeRange.FromSeconds(sourceStart, sourceLength);

        Assert.That(_service.Slip(_scene, [element], MediaTime.FromSeconds(5)), Is.EqualTo(expectedOffset > 0));
        Assert.That(offset.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
        if (media is SourceVideo sampledVideo)
        {
            using var lastFrame = (SourceVideo.Resource)sampledVideo.ToResource(
                new CompositionContext(element.Range.End - MediaTime.FromSeconds(0.1)));
            Assert.That(lastFrame.RequestedPosition + lastFrame.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            if (element.Range.Start < media.Start && media.Start < element.Range.End)
            {
                using var beforeStart = (SourceVideo.Resource)sampledVideo.ToResource(
                    new CompositionContext(media.Start - MediaTime.FromSeconds(0.1)));
                Assert.That(beforeStart.RequestedPosition + beforeStart.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            }
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_TimeController_DoesNotRestrictIndependentSound(bool sceneAudio)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        // The unsupported controller rejects the whole edit when video takes part, so this case is audio only.
        element.Objects.Add(new DrawableTimeController
        {
            Target = { Value = new DrawableGroup() },
            Speed = { Value = 50f }
        });
        Sound sound;
        if (sceneAudio)
        {
            sound = new SceneSound
            {
                ReferencedScene = { Value = new Scene { Duration = { Value = MediaTime.FromSeconds(5) } } }
            };
        }
        else
        {
            var soundSource = new SoundSource();
            soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
            sound = new SourceSound { Source = { Value = soundSource } };
        }
        sound.IsTimeAnchor.Value = true;
        sound.TimeRange.Value = TimeRange.FromSeconds(2);
        var group = new SoundGroup();
        group.Children.Add(sound);
        element.Objects.Add(group);

        Assert.That(_service.Slip(_scene, [element], MediaTime.FromSeconds(3)), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(3)));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Slip_ControllerPreservesPlainVideoBoundsInEitherTraversalOrder(bool controllerFirst, bool sharedTarget)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2, 2) }
        };
        var plain = new DrawablePresenter { Target = { Value = video } };
        var controlledVideo = sharedTarget ? video : new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2) }
        };
        var controller = new DrawableTimeController
        {
            Target = { Value = controlledVideo },
            Speed = { Value = 100f }
        };
        element.Objects.Add(controllerFirst ? controller : plain);
        element.Objects.Add(controllerFirst ? plain : controller);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));
        using var resource = (DrawablePresenter.Resource)plain.ToResource(
            new CompositionContext(MediaTime.FromSeconds(1.9)));
        var sampled = (SourceVideo.Resource)resource.Target!;

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(controlledVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_IdentityControllerOnlyTarget_UsesControllerWindowThroughNestedPresenter(bool nestedPresenter)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(4));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2, 2) }
        };
        Drawable target = nestedPresenter ? new DrawablePresenter
        {
            Target = { Value = video },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = video.TimeRange.Value }
        } : video;
        var controller = new DrawableTimeController
        {
            Target = { Value = target },
            Speed = { Value = 100f }
        };
        element.Objects.Add(controller);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));
        using var resource = (DrawableTimeController.Resource)controller.ToResource(
            new CompositionContext(MediaTime.FromSeconds(3.9)));
        var sampled = resource.Target is DrawablePresenter.Resource wrapper
            ? (SourceVideo.Resource)wrapper.Target!
            : (SourceVideo.Resource)resource.Target!;

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase(0, 4, 2, "video", 0)]
    [TestCase(0, 4, 2, "presenter", 0)]
    [TestCase(0, 4, 2, "group", 0)]
    [TestCase(0, 4, 2, "controller", 0)]
    [TestCase(10, 2, 9, "video", 2)]
    [TestCase(10, 2, 10, "video", 3)]
    [TestCase(10, 2, 11, "video", 0)]
    [TestCase(10, 2, 8, "presenter", 3)]
    [TestCase(10, 2, 8, "group", 3)]
    [TestCase(10, 2, 9, "controller", 2)]
    public void Slip_AnchoredIdentityController_UsesItsEvaluationWindow(
        int start, int length, int controllerStart, string targetKind, int expectedOffset)
    {
        Element element = AddElement(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(10, 2) }
        };
        Drawable target = targetKind switch
        {
            "presenter" => new DrawablePresenter { Target = { Value = video } },
            "group" => new DrawableGroup { Children = { video } },
            "controller" => new DrawableTimeController { Target = { Value = video } },
            _ => video
        };
        if (target != video)
        {
            target.IsTimeAnchor.Value = true;
            target.TimeRange.Value = TimeRange.FromSeconds(8, 2);
        }
        var controller = new DrawableTimeController
        {
            Target = { Value = target },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(controllerStart, 2) }
        };
        var group = new DrawableGroup();
        group.Children.Add(controller);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedOffset > 0));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedOffset > 0 ? 1 : 0)));
            foreach (MediaTime time in new[]
                     {
                         element.Start.Value,
                         element.Range.End - MediaTime.FromSeconds(0.1),
                         controller.Start - MediaTime.FromSeconds(0.1)
                     }.Where(element.Range.Contains))
            {
                using var resource = (DrawableGroup.Resource)group.ToResource(new CompositionContext(time));
                Drawable.Resource current = resource.Children.Single();
                while (current is not SourceVideo.Resource)
                {
                    current = current switch
                    {
                        DrawableTimeController.Resource controlled => controlled.Target!,
                        DrawablePresenter.Resource presented => presented.Target!,
                        DrawableGroup.Resource container => container.Children.Single(),
                        _ => throw new InvalidOperationException()
                    };
                }
                var sampled = (SourceVideo.Resource)current;
                Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.GreaterThanOrEqualTo(MediaTime.Zero));
                Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            }
        });
    }

    [Test]
    public void Slip_NestedTimeController_RetainsElementBoundsUntilTimeMappingIsSupported()
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2) }
        };
        // Reach the same source through a plain presenter first, so traversal order cannot
        // hide the enclosing time mapping when collection deduplicates the media object.
        var presenter = new DrawablePresenter { Target = { Value = video } };
        element.Objects.Add(presenter);
        var controller = new DrawableTimeController
        {
            Target = { Value = video },
            Speed = { Value = 50f }
        };
        element.Objects.Add(controller);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(3));
        using var resource = (DrawableTimeController.Resource)controller.ToResource(
            new CompositionContext(MediaTime.FromSeconds(9.9)));
        var sampled = (SourceVideo.Resource)resource.Target!;

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }

    [TestCase(0, 2, 1, 100f, 0)]
    [TestCase(0, 2, 1, 50f, 0)]
    [TestCase(0, 2, 1, 200f, 0)]
    [TestCase(1, 1, 8, 100f, 1)]
    [TestCase(6, 2, 0, 100f, 2)]
    [TestCase(0, 12, 0, 100f, 0)]
    [TestCase(1, 1, 0, 100f, 3)]
    [TestCase(0, 1, 5, 100f, 4)]
    [TestCase(0, 1, 10, 100f, 4)]
    [TestCase(0, 1, 10, 50f, 4.5)]
    [TestCase(0, 1, 2.5, 200f, 3)]
    public void Slip_LoopingVideo_ClampsToMaximumMappedSample(
        double start, double length, double videoStart, float speed, double expectedOffset)
    {
        Element element = AddElement(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            Speed = { Value = speed },
            IsLoop = { Value = true },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(videoStart, 2) }
        };
        var group = new DrawableGroup();
        group.Children.Add(video);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedOffset > 0));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedOffset > 0 ? 1 : 0)));
            foreach (MediaTime time in new[]
                     {
                         element.Start.Value,
                         element.Start.Value + MediaTime.FromSeconds(0.1),
                         element.Range.End - MediaTime.FromSeconds(0.1),
                         video.Start - MediaTime.FromSeconds(0.1)
                     }.Where(element.Range.Contains))
            {
                using var resource = (DrawableGroup.Resource)group.ToResource(new CompositionContext(time));
                var sampled = (SourceVideo.Resource)resource.Children.Single();
                Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
            }
        });
    }

    [TestCase(false, true, true, 2)]
    [TestCase(true, true, true, 2)]
    [TestCase(true, false, false, 0)]
    [TestCase(true, null, null, 0)]
    [TestCase(false, false, true, 0)]
    [TestCase(false, true, false, 0)]
    public void Slip_AnimatedLoopFlag_UsesConstantValueOrBothVaryingMappings(
        bool storedLoop, bool? first, bool? last, int expectedOffset)
    {
        Element element = AddElement(MediaTime.FromSeconds(6), MediaTime.FromSeconds(2));
        var source = CreateVideoSource(5);
        var animation = new KeyFrameAnimation<bool>();
        if (first.HasValue && last.HasValue)
        {
            animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = first.Value } });
            animation.KeyFrames.Add(new KeyFrame<bool> { KeyTime = { Value = MediaTime.FromSeconds(7) }, Value = { Value = last.Value } });
        }
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsLoop = { Value = storedLoop },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(2) }
        };
        video.IsLoop.SetKeyFrameAnimation(animation);
        var group = new DrawableGroup();
        group.Children.Add(video);
        element.Objects.Add(group);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(20));

        using var resource = (DrawableGroup.Resource)group.ToResource(new CompositionContext(MediaTime.FromSeconds(7.9)));
        var sampled = (SourceVideo.Resource)resource.Children.Single();
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(expectedOffset > 0));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedOffset)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (expectedOffset > 0 ? 1 : 0)));
            Assert.That(sampled.IsLoop, Is.EqualTo(last ?? false));
            Assert.That(sampled.RequestedPosition, Is.EqualTo(MediaTime.FromSeconds(last == true ? 2.9 : 7.9)));
            if (expectedOffset > 0)
                Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
        });
    }

    [TestCase(false, false, 100f)]
    [TestCase(true, false, 100f)]
    [TestCase(false, true, 100f)]
    [TestCase(true, true, 100f)]
    [TestCase(false, false, 200f)]
    [TestCase(true, false, 200f)]
    [TestCase(false, true, 200f)]
    [TestCase(true, true, 200f)]
    public void Slip_DisabledController_PreservesTargetBoundsForReenabling(bool nestedPresenter, bool loop, float speed)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var source = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = source },
            IsLoop = { Value = loop },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(10, 2) }
        };
        Drawable target = nestedPresenter ? new DrawablePresenter
        {
            Target = { Value = video },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = video.TimeRange.Value }
        } : video;
        var controller = new DrawableTimeController
        {
            IsEnabled = { Value = false },
            Speed = { Value = speed },
            Target = { Value = target }
        };
        element.Objects.Add(controller);
        var linked = new SourceSound();
        element.Objects.Add(linked);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(5));

        int after = _history.UndoCount;
        controller.IsEnabled.Value = true;
        using var resource = (DrawableTimeController.Resource)controller.ToResource(
            new CompositionContext(MediaTime.FromSeconds(1.9)));
        var sampled = resource.Target is DrawablePresenter.Resource wrapper
            ? (SourceVideo.Resource)wrapper.Target!
            : (SourceVideo.Resource)resource.Target!;
        double expectedVideoOffset = speed == 100 ? 3 : 1;
        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedVideoOffset)));
            Assert.That(linked.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(expectedVideoOffset * 100 / speed)));
            Assert.That(after, Is.EqualTo(before + 1));
            Assert.That(sampled.RequestedPosition + sampled.OffsetPosition, Is.LessThan(MediaTime.FromSeconds(5)));
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void Slip_NestedMedia_CombinesVisibleWindowsWithSpeedAndLoopBounds(bool audio, bool loop)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(10));
        IProperty<MediaTime> offset;
        if (audio)
        {
            var source = new SoundSource();
            source.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
            var sound = new SourceSound
            {
                Source = { Value = source },
                Speed = { Value = 200f },
                IsTimeAnchor = { Value = true },
                TimeRange = { Value = TimeRange.FromSeconds(2) }
            };
            var group = new SoundGroup();
            offset = sound.OffsetPosition;
            group.Children.Add(sound);
            element.Objects.Add(group);
        }
        else
        {
            var source = CreateVideoSource(5);
            var video = new SourceVideo
            {
                Source = { Value = source },
                Speed = { Value = loop ? 100f : 50f },
                IsLoop = { Value = loop },
                IsTimeAnchor = { Value = true },
                TimeRange = { Value = TimeRange.FromSeconds(2) }
            };
            var group = new DrawableGroup();
            offset = video.OffsetPosition;
            group.Children.Add(video);
            element.Objects.Add(group);
        }

        Assert.That(_service.Slip(_scene, [element], MediaTime.FromSeconds(3)), Is.EqualTo(audio));
        Assert.That(offset.Value, Is.EqualTo(MediaTime.FromSeconds(audio ? 1 : 0)));
    }

    [Test]
    public void Slip_NestedLinkedMedia_ClampsToTightestSampledWindowAndOffset()
    {
        var app = new BeutlApplication { Project = { Value = new Project() } };
        app.Project.Value!.Items.Add(_scene);
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(3));
        var videoSource = CreateVideoSource(5);
        var video = new SourceVideo
        {
            Source = { Value = videoSource },
            OffsetPosition = { Value = MediaTime.FromSeconds(1) }
        };
        // Inherit the independent range from an inner group rather than anchoring the source itself.
        var inner = new DrawableGroup { IsTimeAnchor = { Value = true }, TimeRange = { Value = TimeRange.FromSeconds(2) } };
        inner.Children.Add(video);
        var outer = new DrawableGroup();
        outer.Children.Add(inner);
        element.Objects.Add(outer);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: 5)));
        var sound = new SourceSound
        {
            Source = { Value = soundSource },
            IsTimeAnchor = { Value = true },
            TimeRange = { Value = TimeRange.FromSeconds(4) }
        };
        var soundGroup = new SoundGroup();
        soundGroup.Children.Add(sound);
        element.Objects.Add(soundGroup);
        Assert.That(video.TimeRange.Value, Is.EqualTo(TimeRange.FromSeconds(2)));
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(video.TimeRange.Value, Is.EqualTo(TimeRange.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_SharedSourceReachableViaTwoPresenters_ShiftsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        var firstPresenter = new DrawablePresenter();
        firstPresenter.Target.Value = video;
        var secondPresenter = new DrawablePresenter();
        secondPresenter.Target.Value = video;
        element.Objects.Add(firstPresenter);
        element.Objects.Add(secondPresenter);

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_TimeRemappedSharedSource_RequiresCompatibleOffsetChanges(bool directFirst)
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        var controller = new DrawableTimeController
        {
            Speed = { Value = 50 },
            Target = { Value = video }
        };
        if (directFirst)
            element.Objects.Add(video);
        element.Objects.Add(controller);
        if (!directFirst)
            element.Objects.Add(video);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.EqualTo(directFirst));
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(directFirst ? 1 : 0)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + (directFirst ? 1 : 0)));
        });
    }

    [Test]
    public void Slip_TimeRemappedFlowWithoutExplicitTarget_UsesPlaybackSpeed()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        element.Objects.Add(video);
        element.AddObject(new DrawableTimeController { Speed = { Value = 50 } });
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_SpeedExpression_RejectsBeforeMutation()
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Expression = Expression.Create<float>("200") } };
        element.Objects.Add(video);
        using var playback = (SourceVideo.Resource)video.ToResource(new CompositionContext(MediaTime.FromSeconds(1)));
        Assert.That(playback.Speed, Is.EqualTo(200));
        Assert.That(playback.RequestedPosition, Is.EqualTo(MediaTime.FromSeconds(2)));
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.That(applied, Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Slip_TimeControllerBeforeVideo_DoesNotBlockLaterSource()
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var source = CreateVideoSource(6);
        var video = new SourceVideo { Source = { Value = source }, Speed = { Value = 200 } };
        var controller = new DrawableTimeController { Speed = { Value = 50 } };
        element.Objects.Add(controller);
        element.Objects.Add(video);

        // Replay the compositor's order: the controller has no preceding drawable to
        // consume, so the later video keeps its own 200% source-time mapping.
        var context = new CompositionContext(MediaTime.FromSeconds(1)) { Flow = [] };
        using var controllerResource = (DrawableTimeController.Resource)controller.ToResource(context);
        context.Flow.Add(controllerResource);
        using var videoResource = (SourceVideo.Resource)video.ToResource(context);
        Assert.That(controllerResource.Target, Is.Null);
        Assert.That(videoResource.RequestedPosition, Is.EqualTo(MediaTime.FromSeconds(2)));
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(element.Start.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [TestCase("Speed")]
    [TestCase("OffsetPosition")]
    [TestCase("AdjustTimeRange")]
    [TestCase("Reverse")]
    [TestCase("Loop")]
    [TestCase("HoldFirstFrame")]
    [TestCase("HoldLastFrame")]
    [TestCase("FrameRate")]
    public void Slip_ControllerExpression_RejectsBeforeMutation(string property)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        var controller = new DrawableTimeController { Target = { Value = video } };
        switch (property)
        {
            case "Speed": controller.Speed.Expression = Expression.Create<float>("200"); break;
            case "OffsetPosition": controller.OffsetPosition.Expression = Expression.Create<MediaTime>("TimeSpan.FromSeconds(1)"); break;
            case "AdjustTimeRange": controller.AdjustTimeRange.Expression = Expression.Create<bool>("true"); break;
            case "Reverse": controller.Reverse.Expression = Expression.Create<bool>("true"); break;
            case "Loop": controller.Loop.Expression = Expression.Create<bool>("true"); break;
            case "HoldFirstFrame": controller.HoldFirstFrame.Expression = Expression.Create<bool>("true"); break;
            case "HoldLastFrame": controller.HoldLastFrame.Expression = Expression.Create<bool>("true"); break;
            case "FrameRate": controller.FrameRate.Expression = Expression.Create<float>("30"); break;
        }
        element.Objects.Add(controller);
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.That(applied, Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Slip_ContainerController_DoesNotRemapSiblingVideo(bool decorator, bool videoFirst)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var video = new SourceVideo { Speed = { Value = 200 } };
        var controller = new DrawableTimeController { Speed = { Value = 50 } };
        Drawable container = decorator ? new DrawableDecorator() : new DrawableGroup();
        var children = container is DrawableDecorator d ? d.Children : ((DrawableGroup)container).Children;
        children.Add(videoFirst ? video : controller);
        children.Add(videoFirst ? controller : video);
        element.Objects.Add(container);

        using var resource = container.ToResource(new CompositionContext(MediaTime.FromSeconds(1)) { Flow = [] });
        var childResources = resource is DrawableDecorator.Resource dr
            ? dr.Children : ((DrawableGroup.Resource)resource).Children;
        Assert.That(childResources.OfType<DrawableTimeController.Resource>().Single().Target, Is.Null);
        Assert.That(childResources.OfType<SourceVideo.Resource>().Single().RequestedPosition,
            Is.EqualTo(MediaTime.FromSeconds(2)));

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.That(applied, Is.True);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Slip_DisabledControllerReferencedByPresenter_RejectsActiveMapping(bool controllerFirst)
    {
        Element element = AddElement(MediaTime.Zero, MediaTime.FromSeconds(2));
        var video = new SourceVideo
        {
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
            Speed = { Value = 200 }
        };
        var controller = new DrawableTimeController
        {
            TimeRange = { Value = video.TimeRange.Value },
            IsEnabled = { Value = false },
            Speed = { Value = 50 },
            Target = { Value = video }
        };
        var presenter = new DrawablePresenter { Target = { Value = controller } };
        element.Objects.Add(video);
        element.Objects.Add(controllerFirst ? controller : presenter);
        element.Objects.Add(controllerFirst ? presenter : controller);

        var context = new CompositionContext(MediaTime.FromSeconds(1)) { Flow = [] };
        using var videoResource = video.ToResource(context);
        context.Flow.Add(videoResource);
        using var presenterResource = (DrawablePresenter.Resource)presenter.ToResource(context);
        var mapped = (DrawableTimeController.Resource)presenterResource.Target!;
        Assert.That(((SourceVideo.Resource)mapped.Target!).RequestedPosition, Is.EqualTo(MediaTime.FromSeconds(1)));
        _history.Commit();
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [element], MediaTime.FromSeconds(1));

        Assert.That(applied, Is.False);
        Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(_history.UndoCount, Is.EqualTo(before));
    }

    [Test]
    public void Slip_SharedSourceAcrossElements_ShiftsOnce()
    {
        Element first = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        first.Objects.Add(video);

        Element second = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        var presenter = new DrawablePresenter();
        presenter.Target.Value = video;
        second.Objects.Add(presenter);

        bool applied = _service.Slip(_scene, [first, second], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_MultipleElements_ShiftsAllAndCommitsOnce()
    {
        Element first = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        Element second = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        var video = new SourceVideo();
        var sound = new SourceSound();
        first.Objects.Add(video);
        second.Objects.Add(sound);
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [first, second], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_MultipleElements_ClampsToTightestElement()
    {
        // First element's video source only allows a 1s slip (3s source - 2s element); the
        // second element's media is unbounded. The shared delta must land both at 1s.
        Element first = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        var videoSource = CreateVideoSource(3);
        var video = new SourceVideo { Source = { Value = videoSource } };
        first.Objects.Add(video);

        Element second = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        var sound = new SourceSound();
        second.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [first, second], MediaTime.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_MultipleElements_NegativeDelta_ClampsToTightestOffset()
    {
        Element first = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        video.OffsetPosition.Value = MediaTime.FromSeconds(3);
        first.Objects.Add(video);

        Element second = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        var sound = new SourceSound();
        sound.OffsetPosition.Value = MediaTime.FromSeconds(0.5);
        second.Objects.Add(sound);

        bool applied = _service.Slip(_scene, [first, second], MediaTime.FromSeconds(-2));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(2.5)));
            Assert.That(sound.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
        });
    }

    [Test]
    public void Slip_LockedMember_IsDroppedNotBlocking()
    {
        Element unlocked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 0);
        var video = new SourceVideo();
        unlocked.Objects.Add(video);

        Element locked = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), zIndex: 1);
        var lockedVideo = new SourceVideo();
        locked.Objects.Add(lockedVideo);
        locked.IsLocked.Value = true;
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [unlocked, locked], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(lockedVideo.OffsetPosition.Value, Is.EqualTo(MediaTime.Zero));
            Assert.That(_history.UndoCount, Is.EqualTo(before + 1));
        });
    }

    [Test]
    public void Slip_DuplicateElement_ShiftsOnce()
    {
        Element element = AddElement(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2));
        var video = new SourceVideo();
        element.Objects.Add(video);

        bool applied = _service.Slip(_scene, [element, element], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(video.OffsetPosition.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        });
    }

    [Test]
    public void Slip_EmptyElements_NoCommit()
    {
        int before = _history.UndoCount;

        bool applied = _service.Slip(_scene, [], MediaTime.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(_history.UndoCount, Is.EqualTo(before));
        });
    }
}
