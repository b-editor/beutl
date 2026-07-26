using System.Collections.Specialized;
using System.Reactive;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Threading;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Dispatcher = Avalonia.Threading.Dispatcher;
using ImageBrush = Avalonia.Media.ImageBrush;
using PixelSize = Avalonia.PixelSize;
using Point = Avalonia.Point;

namespace Beutl;

public static class AvaloniaTypeConverter
{
    public static Media.GradientStop ToBtlGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new Media.GradientStop(obj.Color.ToBtlColor(), (float)obj.Offset);
    }

    public static GradientStop.Resource ToBtlImmutableGradientStop(this Avalonia.Media.IGradientStop obj)
    {
        return new GradientStop.Resource { Color = obj.Color.ToBtlColor(), Offset = (float)obj.Offset, };
    }

    private static IDisposable AdaptEngineObject<T, TResource>(T obj, IObservable<TimeSpan> time,
        Func<T, CompositionContext, TResource> createResource, Action<TResource> onUpdated)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        return obj.SubscribeEngineVersionedResource(time, createResource)
            .ObserveOnUIDispatcher()
            .Subscribe(t => onUpdated(t.Resource));
    }

    public static (Avalonia.Media.GradientStop, IDisposable) ToAvaGradientStopSync(
        this Media.GradientStop obj, IObservable<TimeSpan> time)
    {
        var s = new Avalonia.Media.GradientStop();
        var d = AdaptEngineObject(
            obj, time,
            (o, rc) => o.ToResource(rc),
            r =>
            {
                s.Color = r.Color.ToAvaColor();
                s.Offset = r.Offset;
            });

        return (s, d);
    }

    public static (IObservable<Avalonia.Media.Geometry>, IDisposable) ToAvaGeometrySync(
        this PathFigure obj, IObservable<TimeSpan> time)
    {
        var reactiveProperty = new ReactivePropertySlim<Avalonia.Media.Geometry>();
        var d = AdaptEngineObject(
            obj, time,
            (o, rc) => o.ToResource(rc),
            r =>
            {
                using var context = new GeometryContext();
                var original = r.GetOriginal();
                original.ApplyTo(context, r);

                string svgPath = context.NativeObject.ToSvgPathData();
                reactiveProperty.Value = Avalonia.Media.Geometry.Parse(svgPath);
            });

        return (reactiveProperty, d);
    }

    public static Matrix ToAvaMatrix(this in Graphics.Matrix matrix)
    {
        return new Matrix(
            matrix.M11, matrix.M12, matrix.M13,
            matrix.M21, matrix.M22, matrix.M23,
            matrix.M31, matrix.M32, matrix.M33);
    }

    public static Graphics.Point ToBtlPoint(this in Point point)
    {
        return new Graphics.Point((float)point.X, (float)point.Y);
    }

    public static (Avalonia.Media.GradientStops, IDisposable) ToAvaGradientStopsSync(
        this ICoreList<Media.GradientStop> obj,
        IObservable<TimeSpan> time)
    {
        var d = new CompositeDisposable();
        var stops = new Avalonia.Media.GradientStops();
        var subscription = new Dictionary<Media.GradientStop, IDisposable>();

        for (int i = 0; i < obj.Count; i++)
        {
            Media.GradientStop item = obj[i];
            var t = item.ToAvaGradientStopSync(time);
            subscription[item] = t.Item2;
            stops.Insert(i, t.Item1);
        }

        obj.CollectionChangedAsObservable()
            .Subscribe(e =>
            {
                int index;
                switch (e.Action)
                {
                    case NotifyCollectionChangedAction.Add:
                        index = e.NewStartingIndex;
                        foreach (Media.GradientStop? item in e.NewItems!)
                        {
                            var t = item!.ToAvaGradientStopSync(time);
                            subscription[item!] = t.Item2;
                            stops.Insert(index++, t.Item1);
                        }

                        break;

                    case NotifyCollectionChangedAction.Remove:
                        index = e.OldStartingIndex;
                        for (int i = e.OldItems!.Count - 1; i >= 0; --i)
                        {
                            var item = (Media.GradientStop)e.OldItems[i]!;
                            if (subscription.TryGetValue(item, out var disposable))
                            {
                                disposable.Dispose();
                                subscription.Remove(item);
                            }

                            stops.RemoveAt(index + i);
                        }

                        break;

                    case NotifyCollectionChangedAction.Replace:
                        index = e.NewStartingIndex;
                        for (int i = 0; i < e.NewItems!.Count; i++)
                        {
                            var oldItem = (Media.GradientStop)e.OldItems![i]!;
                            var newItem = (Media.GradientStop)e.NewItems![i]!;
                            if (subscription.TryGetValue(oldItem, out var disposable))
                            {
                                disposable.Dispose();
                                subscription.Remove(oldItem);
                            }

                            (Avalonia.Media.GradientStop, IDisposable) t = newItem.ToAvaGradientStopSync(time);

                            stops[index] = t.Item1;
                            index++;
                        }

                        break;
                    case NotifyCollectionChangedAction.Move:
                        if (e.OldStartingIndex >= 0
                            && e.OldStartingIndex < stops.Count
                            && e.NewStartingIndex >= 0
                            && e.NewStartingIndex < stops.Count
                            && e.OldStartingIndex != e.NewStartingIndex
                            && e.OldItems is { Count: 1 })
                        {
                            stops.Move(e.OldStartingIndex, e.NewStartingIndex);
                        }
                        break;

                    case NotifyCollectionChangedAction.Reset:
                        stops.Clear();
                        foreach (var item in subscription.Values)
                        {
                            item.Dispose();
                        }

                        subscription.Clear();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(e));
                }
            })
            .DisposeWith(d);
        Disposable.Create(subscription, s =>
        {
            foreach (var item in s.Values)
            {
                item.Dispose();
            }
        }).DisposeWith(d);

        return (stops, d);
    }

    public static (Avalonia.Media.Brush?, IDisposable, Action?) ToAvaBrushSync(this Media.Brush? brush,
        IObservable<TimeSpan> time)
    {
        switch (brush)
        {
            case Media.SolidColorBrush s:
                {
                    var ss = new Avalonia.Media.SolidColorBrush();
                    var d = AdaptEngineObject(
                        s, time,
                        (o, rc) => o.ToResource(rc),
                        r => ss.Color = r.Color.ToAvaColor());
                    return (ss, d, null);
                }

            case Media.GradientBrush g:
                {
                    (Avalonia.Media.GradientStops stops, IDisposable d) = g.GradientStops.ToAvaGradientStopsSync(time);

                    switch (g)
                    {
                        case Media.LinearGradientBrush:
                            return (new Avalonia.Media.LinearGradientBrush { GradientStops = stops, }, d, null);

                        case Media.ConicGradientBrush:
                            return (new Avalonia.Media.ConicGradientBrush { GradientStops = stops, }, d, null);

                        case Media.RadialGradientBrush:
                            return (new Avalonia.Media.RadialGradientBrush { GradientStops = stops, }, d, null);
                    }
                }
                break;

            case Media.DrawableBrush db:
                {
                    var imageBrush = new ImageBrush();
                    var subscription = new DrawableBrushSubscription(
                        db,
                        time,
                        imageBrush,
                        RenderThread.Dispatcher);
                    return (imageBrush, subscription, null);
                }
        }

        return default;
    }

    private sealed class DrawableBrushSubscription : IDisposable
    {
        private readonly object _gate = new();
        private readonly Media.DrawableBrush _drawableBrush;
        private readonly ImageBrush _imageBrush;
        private readonly Beutl.Threading.Dispatcher _renderDispatcher;
        private readonly CompositionContext _compositionContext = new(TimeSpan.Zero);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly SingleAssignmentDisposable _subscription = new();
        private DrawableBrush.Resource? _resource;
        private DrawableImageBrushHandler? _handler;
        private int _lastVersion = -1;
        private bool _disposed;

        public DrawableBrushSubscription(
            Media.DrawableBrush drawableBrush,
            IObservable<TimeSpan> time,
            ImageBrush imageBrush,
            Beutl.Threading.Dispatcher renderDispatcher)
        {
            _drawableBrush = drawableBrush;
            _imageBrush = imageBrush;
            _renderDispatcher = renderDispatcher;
            _renderDispatcher.ShutdownStarted += OnRenderDispatcherShutdown;

            IObservable<Unit> edits = Observable.FromEventPattern(
                    h => _drawableBrush.Edited += h,
                    h => _drawableBrush.Edited -= h)
                .Select(static _ => Unit.Default)
                .Publish(Unit.Default)
                .RefCount();

            _subscription.Disposable = edits
                .CombineLatest(time)
                .Subscribe(value => QueueUpdate(value.Second));

            if (_renderDispatcher.HasShutdownStarted)
                Dispose();
        }

        public void Dispose()
        {
            DrawableImageBrushHandler? handler;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                _lifetime.Cancel();
                handler = _handler;
                _handler = null;
                _resource = null;
            }

            _renderDispatcher.ShutdownStarted -= OnRenderDispatcherShutdown;
            _subscription.Dispose();
            handler?.Dispose();
            _lifetime.Dispose();
        }

        private void QueueUpdate(TimeSpan time)
        {
            lock (_gate)
            {
                if (_disposed || _renderDispatcher.HasShutdownStarted)
                    return;

                _renderDispatcher.Dispatch(
                    () => ApplyUpdate(time),
                    DispatchPriority.Low,
                    _lifetime.Token);
            }
        }

        private void ApplyUpdate(TimeSpan time)
        {
            DrawableImageBrushHandler handler;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _compositionContext.Time = time;
                if (_resource is null)
                {
                    DrawableBrush.Resource resource =
                        (DrawableBrush.Resource)_drawableBrush.ToResource(_compositionContext);
                    try
                    {
                        handler = new DrawableImageBrushHandler(
                            resource,
                            _imageBrush,
                            _renderDispatcher);
                    }
                    catch
                    {
                        resource.Dispose();
                        throw;
                    }

                    _resource = resource;
                    _handler = handler;
                }
                else
                {
                    bool updateOnly = false;
                    _resource.Update(_drawableBrush, _compositionContext, ref updateOnly);
                    handler = _handler!;
                }

                if (_resource.Version == _lastVersion)
                    return;

                _lastVersion = _resource.Version;
            }

            handler.Update();
        }

        private void OnRenderDispatcherShutdown(object? sender, EventArgs e)
        {
            Dispose();
        }
    }

    public sealed class DrawableImageBrushHandler : IDisposable
    {
        private readonly object _gate = new();
        private WriteableBitmap? _bitmap;
        private CancellationTokenSource? _cts;
        private readonly ImageBrush _imageBrush;
        private readonly DrawableBrush.Resource _drawableBrush;
        private readonly Beutl.Threading.Dispatcher _renderDispatcher;
        private readonly HashSet<UpdateState> _activeUpdates = [];
        private bool _disposed;
        private bool _resourceDisposalScheduled;
        private bool _resourceDisposed;

        public DrawableImageBrushHandler(DrawableBrush.Resource drawableBrush, ImageBrush imageBrush)
            : this(drawableBrush, imageBrush, RenderThread.Dispatcher)
        {
        }

        internal DrawableImageBrushHandler(
            DrawableBrush.Resource drawableBrush,
            ImageBrush imageBrush,
            Beutl.Threading.Dispatcher renderDispatcher)
        {
            _imageBrush = imageBrush;
            _drawableBrush = drawableBrush;
            _renderDispatcher = renderDispatcher;
            _renderDispatcher.ShutdownStarted += OnRenderDispatcherShutdown;
            if (_renderDispatcher.HasShutdownStarted)
                OnRenderDispatcherShutdown(_renderDispatcher, EventArgs.Empty);
        }

        public void Update()
        {
            UpdateState update;
            lock (_gate)
            {
                if (_disposed || _renderDispatcher.HasShutdownStarted)
                    return;

                _cts?.Cancel();
                var updateCts = new CancellationTokenSource();
                _cts = updateCts;
                update = new UpdateState(updateCts);
                _activeUpdates.Add(update);
            }

            try
            {
                _renderDispatcher.Dispatch(
                    () => ExecuteUpdate(update),
                    DispatchPriority.Low,
                    CancellationToken.None);
            }
            catch
            {
                CompleteUpdate(update);
                throw;
            }
        }

        public void Dispose()
        {
            bool disposeResource;
            lock (_gate)
            {
                if (_disposed)
                    return;

                _disposed = true;
                foreach (UpdateState update in _activeUpdates)
                    update.Cancellation.Cancel();
                _cts = null;
                disposeResource = ScheduleResourceDisposalIfIdle();
            }

            DisposeUiResources();
            if (disposeResource)
                DispatchResourceDisposal();
        }

        private void ExecuteUpdate(UpdateState update)
        {
            lock (_gate)
            {
                if (!_activeUpdates.Contains(update)
                    || update.Phase != UpdatePhase.Queued)
                {
                    return;
                }

                update.Phase = UpdatePhase.Rendering;
            }

            WriteableBitmap? nextBitmap = null;
            bool publicationQueued = false;
            try
            {
                CancellationToken token = update.Cancellation.Token;
                var drawable = _drawableBrush.Drawable;
                if (token.IsCancellationRequested || drawable == null)
                    return;

                using var node = new DrawableRenderNode(drawable);
                // TODO: UI側の物理的なサイズをもとに描画するように変更する
                using (var context = new GraphicsContext2D(node, new Graphics.Size(1920, 1080)))
                {
                    drawable.GetOriginal()!.Render(context, drawable);
                }

                using var renderer = new RenderNodeRenderer(
                    node,
                    new RenderNodeRendererOptions
                    {
                        Intent = RenderIntent.Preview,
                        TargetDomain = new Graphics.Rect(0, 0, 1920, 1080),
                        UseRenderCache = false,
                    });
                using RenderNodeRasterization rasterization = renderer.Rasterize();
                Media.Bitmap? bitmap = rasterization.Bitmap;
                if (token.IsCancellationRequested || bitmap is null)
                    return;

                nextBitmap = bitmap.ToAvaWriteableBitmap(null);
                if (token.IsCancellationRequested)
                    return;

                Avalonia.Media.Stretch stretch = _drawableBrush.Stretch switch
                {
                    Stretch.Fill => Avalonia.Media.Stretch.Fill,
                    Stretch.Uniform => Avalonia.Media.Stretch.Uniform,
                    Stretch.UniformToFill => Avalonia.Media.Stretch.UniformToFill,
                    Stretch.None => Avalonia.Media.Stretch.None,
                    _ => Avalonia.Media.Stretch.Fill,
                };
                lock (_gate)
                {
                    if (!_activeUpdates.Contains(update))
                        return;

                    update.Phase = UpdatePhase.Publishing;
                }

                var publication = new BitmapPublication(
                    this,
                    nextBitmap,
                    stretch,
                    update);
                nextBitmap = null;
                publicationQueued = true;
                publication.Queue();

                bool disposeResource;
                lock (_gate)
                {
                    disposeResource = ScheduleResourceDisposalIfIdle();
                }

                if (disposeResource)
                    DispatchResourceDisposal();
            }
            finally
            {
                nextBitmap?.Dispose();
                if (!publicationQueued)
                    CompleteUpdate(update);
            }
        }

        private void PublishBitmap(
            WriteableBitmap bitmap,
            Avalonia.Media.Stretch stretch,
            UpdateState update)
        {
            // Avalonia property notifications are synchronous and may re-enter Dispose.
            // Reserve the old owner first, update the brush without the gate, then commit ownership.
            WriteableBitmap? previous;
            lock (_gate)
            {
                if (_disposed
                    || update.Cancellation.IsCancellationRequested
                    || !_activeUpdates.Contains(update))
                {
                    bitmap.Dispose();
                    return;
                }

                previous = _bitmap;
            }

            Avalonia.Media.Stretch previousStretch = _imageBrush.Stretch;
            bool stretchTouched = false;
            bool sourceTouched = false;
            bool committed = false;
            Exception? failure = null;
            try
            {
                if (CanContinueBitmapPublication(previous, update))
                {
                    stretchTouched = true;
                    _imageBrush.Stretch = stretch;
                    if (CanContinueBitmapPublication(previous, update))
                    {
                        sourceTouched = true;
                        _imageBrush.Source = bitmap;

                        lock (_gate)
                        {
                            if (!_disposed
                                && !update.Cancellation.IsCancellationRequested
                                && _activeUpdates.Contains(update)
                                && ReferenceEquals(_bitmap, previous)
                                && ReferenceEquals(_imageBrush.Source, bitmap))
                            {
                                _bitmap = bitmap;
                                committed = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                if (committed)
                {
                    previous?.Dispose();
                }
                else
                {
                    try
                    {
                        RollBackBitmapPublication(
                            bitmap,
                            previous,
                            previousStretch,
                            stretchTouched,
                            sourceTouched);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        bitmap.Dispose();
                    }
                }
            }

            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private bool CanContinueBitmapPublication(
            WriteableBitmap? previous,
            UpdateState update)
        {
            lock (_gate)
            {
                return !_disposed
                       && !update.Cancellation.IsCancellationRequested
                       && _activeUpdates.Contains(update)
                       && ReferenceEquals(_bitmap, previous);
            }
        }

        private void RollBackBitmapPublication(
            WriteableBitmap bitmap,
            WriteableBitmap? previous,
            Avalonia.Media.Stretch previousStretch,
            bool stretchTouched,
            bool sourceTouched)
        {
            Exception? rollbackFailure = null;

            if (sourceTouched && ReferenceEquals(_imageBrush.Source, bitmap))
            {
                bool restorePrevious;
                lock (_gate)
                {
                    restorePrevious = !_disposed && ReferenceEquals(_bitmap, previous);
                }

                try
                {
                    _imageBrush.Source = restorePrevious ? previous : null;
                }
                catch (Exception ex)
                {
                    rollbackFailure = ex;
                }
            }

            bool restoreStretch;
            lock (_gate)
            {
                restoreStretch = !_disposed && ReferenceEquals(_bitmap, previous);
            }

            if (stretchTouched && restoreStretch)
            {
                try
                {
                    _imageBrush.Stretch = previousStretch;
                }
                catch (Exception ex)
                {
                    rollbackFailure = rollbackFailure is null
                        ? ex
                        : new AggregateException(
                            "Bitmap publication rollback failed.",
                            rollbackFailure,
                            ex);
                }
            }

            if (rollbackFailure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(rollbackFailure).Throw();
        }

        private void CompleteUpdate(UpdateState update)
        {
            bool disposeResource;
            lock (_gate)
            {
                if (!_activeUpdates.Remove(update))
                    return;

                if (ReferenceEquals(_cts, update.Cancellation))
                    _cts = null;

                disposeResource = ScheduleResourceDisposalIfIdle();
            }

            update.Cancellation.Dispose();
            if (disposeResource)
                DispatchResourceDisposal();
        }

        private bool ScheduleResourceDisposalIfIdle()
        {
            if (!_disposed
                || _activeUpdates.Any(static update => update.Phase != UpdatePhase.Publishing)
                || _resourceDisposalScheduled)
            {
                return false;
            }

            _resourceDisposalScheduled = true;
            return true;
        }

        private void DispatchResourceDisposal()
        {
            if (_renderDispatcher.HasShutdownStarted)
            {
                DisposeResource();
                return;
            }

            try
            {
                _renderDispatcher.Dispatch(
                    DisposeResource,
                    DispatchPriority.Low,
                    CancellationToken.None);
            }
            catch
            {
                DisposeResource();
                throw;
            }
        }

        private void DisposeUiResources()
        {
            WriteableBitmap? bitmap;
            lock (_gate)
            {
                bitmap = _bitmap;
                _bitmap = null;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                try
                {
                    _imageBrush.Source = null;
                }
                finally
                {
                    bitmap?.Dispose();
                }
            }
            else
            {
                new BitmapCleanup(_imageBrush, bitmap).Queue();
            }
        }

        private void OnRenderDispatcherShutdown(object? sender, EventArgs e)
        {
            List<UpdateState> abandoned = [];
            bool disposeUiResources;
            bool disposeResource;
            lock (_gate)
            {
                disposeUiResources = !_disposed;
                _disposed = true;
                foreach (UpdateState update in _activeUpdates)
                {
                    update.Cancellation.Cancel();
                    if (update.Phase == UpdatePhase.Queued)
                        abandoned.Add(update);
                }

                foreach (UpdateState update in abandoned)
                    _activeUpdates.Remove(update);

                _cts = null;
                disposeResource = ScheduleResourceDisposalIfIdle();
            }

            foreach (UpdateState update in abandoned)
                update.Cancellation.Dispose();

            if (disposeUiResources)
                DisposeUiResources();

            if (disposeResource || _resourceDisposalScheduled)
                DisposeResource();
        }

        private void DisposeResource()
        {
            lock (_gate)
            {
                if (_resourceDisposed)
                    return;

                _resourceDisposed = true;
            }

            _renderDispatcher.ShutdownStarted -= OnRenderDispatcherShutdown;
            _drawableBrush.Dispose();
        }

        private sealed class BitmapPublication(
            DrawableImageBrushHandler owner,
            WriteableBitmap bitmap,
            Avalonia.Media.Stretch stretch,
            UpdateState update)
        {
            private DispatcherOperation? _operation;
            private WriteableBitmap? _bitmap = bitmap;
            private int _completed;

            public void Queue()
            {
                try
                {
                    DispatcherOperation operation = Dispatcher.UIThread.InvokeAsync(
                        Publish,
                        DispatcherPriority.Background);
                    _operation = operation;
                    operation.Completed += OnTerminal;
                    operation.Aborted += OnTerminal;
                    if (operation.Status is DispatcherOperationStatus.Completed
                        or DispatcherOperationStatus.Aborted)
                    {
                        Complete();
                    }
                }
                catch
                {
                    Complete();
                }
            }

            private void Publish()
            {
                WriteableBitmap? owned = Interlocked.Exchange(ref _bitmap, null);
                if (owned is not null)
                    owner.PublishBitmap(owned, stretch, update);
            }

            private void OnTerminal(object? sender, EventArgs e)
            {
                Complete();
            }

            private void Complete()
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0)
                    return;

                if (_operation is { } operation)
                {
                    operation.Completed -= OnTerminal;
                    operation.Aborted -= OnTerminal;
                }

                Interlocked.Exchange(ref _bitmap, null)?.Dispose();
                owner.CompleteUpdate(update);
            }
        }

        private sealed class BitmapCleanup(ImageBrush imageBrush, WriteableBitmap? bitmap)
        {
            private DispatcherOperation? _operation;
            private WriteableBitmap? _bitmap = bitmap;
            private int _completed;

            public void Queue()
            {
                try
                {
                    DispatcherOperation operation = Dispatcher.UIThread.InvokeAsync(
                        ClearSource,
                        DispatcherPriority.Background);
                    _operation = operation;
                    operation.Completed += OnTerminal;
                    operation.Aborted += OnTerminal;
                    if (operation.Status is DispatcherOperationStatus.Completed
                        or DispatcherOperationStatus.Aborted)
                    {
                        Complete();
                    }
                }
                catch
                {
                    Complete();
                }
            }

            private void ClearSource()
            {
                imageBrush.Source = null;
            }

            private void OnTerminal(object? sender, EventArgs e)
            {
                Complete();
            }

            private void Complete()
            {
                if (Interlocked.Exchange(ref _completed, 1) != 0)
                    return;

                if (_operation is { } operation)
                {
                    operation.Completed -= OnTerminal;
                    operation.Aborted -= OnTerminal;
                }

                Interlocked.Exchange(ref _bitmap, null)?.Dispose();
            }
        }

        private sealed class UpdateState(CancellationTokenSource cancellation)
        {
            public CancellationTokenSource Cancellation { get; } = cancellation;

            public UpdatePhase Phase { get; set; }
        }

        private enum UpdatePhase
        {
            Queued,
            Rendering,
            Publishing,
        }
    }
}
