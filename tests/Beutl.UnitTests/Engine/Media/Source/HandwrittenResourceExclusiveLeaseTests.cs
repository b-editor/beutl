using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Media.Source;

[TestFixture]
public sealed class HandwrittenResourceExclusiveLeaseTests
{
    [TestCase(OwnedSourceKind.Image)]
    [TestCase(OwnedSourceKind.Sound)]
    [TestCase(OwnedSourceKind.Video)]
    public async Task DisposeWhileCounterReleaseBlocks_FailsFastThenRetryCompletesCleanup(
        OwnedSourceKind sourceKind)
    {
        var releaseGate = new BlockingReleaseGate();
        OwnedSourceFixture fixture = CreateOwnedSourceFixture(sourceKind, releaseGate.Block);

        Task<Exception?> updateTask = Task.Run(() =>
            UpdateAndCapture(fixture.Resource, fixture.Source, CompositionContext.Default));

        Assert.That(releaseGate.Entered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Throws<InvalidOperationException>(fixture.Resource.Dispose);
            AssertOwnedSourceStateThrows<InvalidOperationException>(fixture.Resource);
            Assert.Multiple(() =>
            {
                Assert.That(fixture.Resource.IsDisposed, Is.False,
                    "a rejected cleanup must leave the resource retryable");
                Assert.That(fixture.IsOwnedValueDisposed(), Is.False,
                    "cleanup must not race the update's outstanding counter release");
            });
        }
        finally
        {
            releaseGate.Continue.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(fixture.IsOwnedValueDisposed(), Is.True);

        fixture.Resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Resource.IsDisposed, Is.True);
            Assert.That(releaseGate.InvocationCount, Is.EqualTo(1));
        });
        AssertOwnedSourceStateThrows<ObjectDisposedException>(fixture.Resource);
    }

    [TestCase(OwnedSourceKind.Image)]
    [TestCase(OwnedSourceKind.Video)]
    public void CounterReleaseFailure_DetachesTerminalCounterAndLeavesUpdateRetryable(
        OwnedSourceKind sourceKind)
    {
        var failure = new InvalidOperationException("release callback failed");
        OwnedSourceFixture fixture = CreateOwnedSourceFixture(sourceKind, () => throw failure);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() =>
        {
            bool updateOnly = false;
            fixture.Resource.Update(
                fixture.Source,
                CompositionContext.Default,
                ref updateOnly);
        });

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(fixture.Resource.IsDisposed, Is.False);
            Assert.That(fixture.IsOwnedValueDisposed(), Is.True,
                "release callback failure must not skip disposal of the wrapped native value");
            if (fixture.Resource is ImageSource.Resource image)
                Assert.That(image.Bitmap, Is.Null);
            else if (fixture.Resource is VideoSource.Resource video)
                Assert.That(video.MediaReader, Is.Null);
        });

        Assert.DoesNotThrow(() =>
        {
            bool updateOnly = false;
            fixture.Resource.Update(
                fixture.Source,
                CompositionContext.Default,
                ref updateOnly);
        });
        Assert.DoesNotThrow(fixture.Resource.Dispose);
        Assert.That(fixture.Resource.IsDisposed, Is.True);
    }

    [TestCase(OwnedSourceKind.Sound)]
    [TestCase(OwnedSourceKind.Video)]
    public async Task ReadWhileDecoderBlocks_RejectsConcurrentUpdateAndDisposeThenCompletes(
        OwnedSourceKind sourceKind)
    {
        var reader = new BlockingReadMediaReader();
        EngineObject source;
        EngineObject.Resource resource;
        Func<bool> read;

        switch (sourceKind)
        {
            case OwnedSourceKind.Sound:
                {
                    source = new SoundSource();
                    var soundResource = new SoundSource.Resource();
                    resource = soundResource;
                    read = () => soundResource.Read(0, 1, out _);
                    break;
                }

            case OwnedSourceKind.Video:
                {
                    source = new VideoSource();
                    var videoResource = new VideoSource.Resource();
                    resource = videoResource;
                    read = () => videoResource.Read(0, out _);
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null);
        }

        SetPrivateField(resource, "_counter", new Counter<MediaReader>(reader, null));
        Task<(bool Result, Exception? Failure)> readTask = Task.Run(() => ReadAndCapture(read));

        Assert.That(reader.ReadEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(resource.Dispose);
                Assert.That(UpdateAndCapture(resource, source, CompositionContext.Default),
                    Is.TypeOf<InvalidOperationException>());
                AssertOwnedSourceStateThrows<InvalidOperationException>(resource);
                Assert.That(resource.IsDisposed, Is.False);
                Assert.That(reader.IsDisposed, Is.False);
            });
        }
        finally
        {
            reader.ContinueRead.Set();
        }

        (bool result, Exception? failure) = await readTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.Null);
            Assert.That(result, Is.False);
            Assert.That(reader.IsDisposed, Is.False);
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(reader.IsDisposed, Is.True);
        });
        AssertOwnedSourceStateThrows<ObjectDisposedException>(resource);
        AssertReadOverloadsThrowDisposed(resource);
    }

    [Test]
    public void ImageSource_CounterReleaseCallbackCannotReenterUpdateOnTheSameThread()
    {
        ImageSource.Resource? resource = null;
        var source = new ImageSource();
        source.ReadFrom(CreateMissingFileUri("image-reentrant-new.png"));
        var bitmap = new Bitmap(1, 1);
        Exception? reentryFailure = null;
        var counter = new Counter<Bitmap>(bitmap, () =>
        {
            reentryFailure = UpdateAndCapture(resource!, source, CompositionContext.Default);
        });
        resource = new ImageSource.Resource();
        SetPrivateField(resource, "_counter", counter);
        SetPrivateField(resource, "_loadedUri", CreateMissingFileUri("image-reentrant-old.png"));

        Assert.DoesNotThrow(() =>
        {
            bool updateOnly = false;
            resource.Update(source, CompositionContext.Default, ref updateOnly);
        });

        Assert.Multiple(() =>
        {
            Assert.That(reentryFailure, Is.TypeOf<InvalidOperationException>());
            Assert.That(resource.IsDisposed, Is.False);
            Assert.That(bitmap.IsDisposed, Is.True);
        });

        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public void Transform_CreateMatrixCallbackCannotReenterUpdateOnTheSameThread()
    {
        var transform = new ReentrantTransform();
        var resource = (Transform.Resource)transform.ToResource(CompositionContext.Default);
        transform.ResourceToReenter = resource;
        transform.ReenterNextUpdate = true;

        Assert.Throws<InvalidOperationException>(() =>
        {
            bool updateOnly = false;
            resource.Update(transform, CompositionContext.Default, ref updateOnly);
        });

        Assert.Multiple(() =>
        {
            Assert.That(resource.IsDisposed, Is.False);
            Assert.That(transform.CreateMatrixCount, Is.EqualTo(2),
                "the rejected inner update must not reach CreateMatrix");
        });

        Assert.DoesNotThrow(() =>
        {
            bool updateOnly = false;
            resource.Update(transform, CompositionContext.Default, ref updateOnly);
        });
        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public async Task Transform_StateRejectsConcurrentUpdateAndDisposedAccess()
    {
        var transform = new BlockingTransform();
        var resource = (Transform.Resource)transform.ToResource(CompositionContext.Default);
        Matrix retained = resource.Matrix;
        transform.BlockNextCreate = true;

        Task<Exception?> updateTask = Task.Run(() =>
            UpdateAndCapture(resource, transform, CompositionContext.Default));

        Assert.That(transform.CreateEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.Matrix);
                Assert.Throws<InvalidOperationException>(() => resource.Matrix = Matrix.Identity);
                Assert.Throws<InvalidOperationException>(resource.Dispose);
            });
        }
        finally
        {
            transform.ContinueCreate.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.That(resource.Matrix, Is.Not.EqualTo(retained));
        Assert.DoesNotThrow(() => resource.Matrix = Matrix.Identity);
        Assert.That(resource.Matrix, Is.EqualTo(Matrix.Identity));

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Matrix);
            Assert.Throws<ObjectDisposedException>(() => resource.Matrix = Matrix.Identity);
        });
    }

    [Test]
    public void ShakeEffect_PropertyCallbackCannotReenterUpdateOnTheSameThread()
    {
        var effect = new ShakeEffect();
        var resource = (ShakeEffect.Resource)effect.ToResource(CompositionContext.Default);
        var context = new ReentrantCompositionContext();
        context.Callback = () =>
        {
            bool nestedUpdateOnly = false;
            resource.Update(effect, context, ref nestedUpdateOnly);
        };

        Assert.Throws<InvalidOperationException>(() =>
        {
            bool updateOnly = false;
            resource.Update(effect, context, ref updateOnly);
        });

        Assert.That(resource.IsDisposed, Is.False);
        Assert.DoesNotThrow(() =>
        {
            bool updateOnly = false;
            resource.Update(effect, context, ref updateOnly);
        });
        resource.Dispose();
        Assert.That(resource.IsDisposed, Is.True);
    }

    [Test]
    public async Task ShakeEffect_StateRejectsConcurrentUpdateAndDisposedAccess()
    {
        var effect = new ShakeEffect();
        var resource = (ShakeEffect.Resource)effect.ToResource(CompositionContext.Default);
        var context = new BlockingCompositionContext { BlockNextGet = true };

        Task<Exception?> updateTask = Task.Run(() => UpdateAndCapture(resource, effect, context));

        Assert.That(context.GetEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.Throws<InvalidOperationException>(() => _ = resource.StrengthX);
                Assert.Throws<InvalidOperationException>(() => _ = resource.StrengthY);
                Assert.Throws<InvalidOperationException>(() => _ = resource.Speed);
                Assert.Throws<InvalidOperationException>(() => _ = resource.Time);
                Assert.Throws<InvalidOperationException>(resource.Dispose);
            });
        }
        finally
        {
            context.ContinueGet.Set();
        }

        Assert.That(await updateTask.WaitAsync(TimeSpan.FromSeconds(10)), Is.Null);
        Assert.DoesNotThrow(() =>
        {
            _ = resource.StrengthX;
            _ = resource.StrengthY;
            _ = resource.Speed;
            _ = resource.Time;
        });

        resource.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => _ = resource.StrengthX);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.StrengthY);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Speed);
            Assert.Throws<ObjectDisposedException>(() => _ = resource.Time);
        });
    }

    [Test]
    public void CubeSource_StateRejectsDisposedAccess()
    {
        var resource = new CubeSource.Resource();

        Assert.That(resource.Cube, Is.Null);

        resource.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = resource.Cube);
    }

    private static OwnedSourceFixture CreateOwnedSourceFixture(
        OwnedSourceKind sourceKind,
        Action onRelease)
    {
        Uri oldUri = CreateMissingFileUri($"{sourceKind}-old.dat");
        Uri newUri = CreateMissingFileUri($"{sourceKind}-new.dat");

        switch (sourceKind)
        {
            case OwnedSourceKind.Image:
                {
                    var source = new ImageSource();
                    source.ReadFrom(newUri);
                    var resource = new ImageSource.Resource();
                    var bitmap = new Bitmap(1, 1);
                    var counter = new Counter<Bitmap>(bitmap, onRelease);
                    SetPrivateField(resource, "_counter", counter);
                    SetPrivateField(resource, "_loadedUri", oldUri);
                    return new OwnedSourceFixture(source, resource, () => bitmap.IsDisposed);
                }

            case OwnedSourceKind.Sound:
                {
                    var source = new SoundSource();
                    source.ReadFrom(newUri);
                    var resource = new SoundSource.Resource();
                    var reader = new LeaseProbeMediaReader();
                    var counter = new Counter<MediaReader>(reader, onRelease);
                    SetPrivateField(resource, "_counter", counter);
                    SetPrivateField(resource, "_loadedUri", oldUri);
                    return new OwnedSourceFixture(source, resource, () => reader.IsDisposed);
                }

            case OwnedSourceKind.Video:
                {
                    var source = new VideoSource();
                    source.ReadFrom(newUri);
                    var resource = new VideoSource.Resource();
                    var reader = new LeaseProbeMediaReader();
                    var counter = new Counter<MediaReader>(reader, onRelease);
                    SetPrivateField(resource, "_counter", counter);
                    SetPrivateField(resource, "_loadedUri", oldUri);
                    return new OwnedSourceFixture(source, resource, () => reader.IsDisposed);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, null);
        }
    }

    private static Exception? UpdateAndCapture(
        EngineObject.Resource resource,
        EngineObject source,
        CompositionContext context)
    {
        try
        {
            bool updateOnly = false;
            resource.Update(source, context, ref updateOnly);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static (bool Result, Exception? Failure) ReadAndCapture(Func<bool> read)
    {
        try
        {
            return (read(), null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private static void AssertOwnedSourceStateThrows<TException>(EngineObject.Resource resource)
        where TException : Exception
    {
        switch (resource)
        {
            case ImageSource.Resource image:
                Assert.Multiple(() =>
                {
                    Assert.Throws<TException>(() => _ = image.FrameSize);
                    Assert.Throws<TException>(() => _ = image.Bitmap);
                });
                break;

            case SoundSource.Resource sound:
                Assert.Multiple(() =>
                {
                    Assert.Throws<TException>(() => _ = sound.Duration);
                    Assert.Throws<TException>(() => _ = sound.SampleRate);
                    Assert.Throws<TException>(() => _ = sound.NumChannels);
                    Assert.Throws<TException>(() => _ = sound.MediaReader);
                });
                break;

            case VideoSource.Resource video:
                Assert.Multiple(() =>
                {
                    Assert.Throws<TException>(() => _ = video.Duration);
                    Assert.Throws<TException>(() => _ = video.FrameRate);
                    Assert.Throws<TException>(() => _ = video.FrameSize);
                    Assert.Throws<TException>(() => _ = video.LogicalFrameSize);
                    Assert.Throws<TException>(() => _ = video.ProxyResolution);
                    Assert.Throws<TException>(() => _ = video.SupplyDensity);
                    Assert.Throws<TException>(() => _ = video.MediaReader);
                });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
        }
    }

    private static void AssertReadOverloadsThrowDisposed(EngineObject.Resource resource)
    {
        switch (resource)
        {
            case SoundSource.Resource sound:
                Assert.Multiple(() =>
                {
                    Assert.Throws<ObjectDisposedException>(() => sound.Read(0, 1, out _));
                    Assert.Throws<ObjectDisposedException>(() => sound.Read(TimeSpan.Zero, TimeSpan.Zero, out _));
                    Assert.Throws<ObjectDisposedException>(() => sound.Read(TimeSpan.Zero, 1, out _));
                    Assert.Throws<ObjectDisposedException>(() => sound.Read(0, TimeSpan.Zero, out _));
                });
                break;

            case VideoSource.Resource video:
                Assert.Multiple(() =>
                {
                    Assert.Throws<ObjectDisposedException>(() => video.Read(TimeSpan.Zero, out _));
                    Assert.Throws<ObjectDisposedException>(() => video.Read(0, out _));
                });
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(resource), resource, null);
        }
    }

    private static void SetPrivateField(object target, string name, object? value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
    }

    private static Uri CreateMissingFileUri(string fileName)
        => new($"file:///tmp/beutl-exclusive-resource-{fileName}");

    public enum OwnedSourceKind
    {
        Image,
        Sound,
        Video,
    }

    private sealed record OwnedSourceFixture(
        EngineObject Source,
        EngineObject.Resource Resource,
        Func<bool> IsOwnedValueDisposed);

    private sealed class BlockingReleaseGate
    {
        private int _invocationCount;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Continue { get; } = new();

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public void Block()
        {
            Interlocked.Increment(ref _invocationCount);
            Entered.Set();
            if (!Continue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked counter release was not resumed.");
        }
    }

    private sealed class ReentrantTransform : Transform
    {
        public Transform.Resource? ResourceToReenter { get; set; }

        public bool ReenterNextUpdate { get; set; }

        public int CreateMatrixCount { get; private set; }

        public override Matrix CreateMatrix(CompositionContext context)
        {
            CreateMatrixCount++;
            if (ReenterNextUpdate)
            {
                ReenterNextUpdate = false;
                bool updateOnly = false;
                ResourceToReenter!.Update(this, context, ref updateOnly);
            }

            return Matrix.Identity;
        }
    }

    private sealed class BlockingTransform : Transform
    {
        private int _createCount;

        public bool BlockNextCreate { get; set; }

        public ManualResetEventSlim CreateEntered { get; } = new();

        public ManualResetEventSlim ContinueCreate { get; } = new();

        public override Matrix CreateMatrix(CompositionContext context)
        {
            int createCount = Interlocked.Increment(ref _createCount);
            if (BlockNextCreate)
            {
                BlockNextCreate = false;
                CreateEntered.Set();
                if (!ContinueCreate.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The blocked transform update was not released.");
            }

            return Matrix.CreateTranslation(new Vector(createCount, 0));
        }
    }

    private sealed class ReentrantCompositionContext : CompositionContext
    {
        public ReentrantCompositionContext()
            : base(TimeSpan.Zero)
        {
        }

        public Action? Callback { get; set; }

        public override T Get<T>(IProperty<T> property)
        {
            Action? callback = Callback;
            Callback = null;
            callback?.Invoke();
            return base.Get(property);
        }
    }

    private sealed class BlockingCompositionContext : CompositionContext
    {
        private int _blockNextGet;

        public BlockingCompositionContext()
            : base(TimeSpan.Zero)
        {
        }

        public bool BlockNextGet
        {
            get => Volatile.Read(ref _blockNextGet) != 0;
            set => Volatile.Write(ref _blockNextGet, value ? 1 : 0);
        }

        public ManualResetEventSlim GetEntered { get; } = new();

        public ManualResetEventSlim ContinueGet { get; } = new();

        public override T Get<T>(IProperty<T> property)
        {
            if (Interlocked.Exchange(ref _blockNextGet, 0) != 0)
            {
                GetEntered.Set();
                if (!ContinueGet.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The blocked composition-context read was not released.");
            }

            return base.Get(property);
        }
    }

    private sealed class LeaseProbeMediaReader : MediaReader
    {
        public override VideoStreamInfo VideoInfo { get; } = new(
            codecName: "test",
            duration: Rational.Zero,
            frameSize: new PixelSize(1, 1),
            frameRate: new Rational(1, 1));

        public override AudioStreamInfo AudioInfo { get; } = new(
            CodecName: "test",
            Duration: Rational.Zero,
            SampleRate: 48_000,
            NumChannels: 2);

        public override bool HasVideo => true;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            sound = null;
            return false;
        }
    }

    private sealed class BlockingReadMediaReader : MediaReader
    {
        public ManualResetEventSlim ReadEntered { get; } = new();

        public ManualResetEventSlim ContinueRead { get; } = new();

        public override VideoStreamInfo VideoInfo { get; } = new(
            codecName: "test",
            duration: Rational.Zero,
            frameSize: new PixelSize(1, 1),
            frameRate: new Rational(1, 1));

        public override AudioStreamInfo AudioInfo { get; } = new(
            CodecName: "test",
            Duration: Rational.Zero,
            SampleRate: 48_000,
            NumChannels: 2);

        public override bool HasVideo => true;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            BlockRead();
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out Ref<IPcm>? sound)
        {
            BlockRead();
            sound = null;
            return false;
        }

        private void BlockRead()
        {
            ReadEntered.Set();
            if (!ContinueRead.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocked media read was not released.");
        }
    }
}
