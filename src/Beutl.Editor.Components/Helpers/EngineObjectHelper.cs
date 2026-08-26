using System.Reactive;
using System.Reactive.Disposables;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Threading;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

public static class EngineObjectHelper
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(EngineObjectHelper));

    public static IObservable<IExpression<T>?> SubscribeExpressionChange<T>(this IProperty<T> property)
    {
        return Observable.FromEvent<IExpression<T>?>(
                h => property.ExpressionChanged += h,
                h => property.ExpressionChanged -= h)
            .Select(s => s)
            .Publish(property.Expression).RefCount();
    }

    public static IObservable<T> SubscribeCurrentValueChange<T>(this IProperty<T> property)
    {
        return Observable.FromEventPattern<PropertyValueChangedEventArgs<T>>(
                h => property.ValueChanged += h,
                h => property.ValueChanged -= h)
            .Select(s => s.EventArgs.NewValue)
            .Publish(property.CurrentValue).RefCount();
    }

    public static IObservable<T> SubscribeEngineProperty<T>(
        this IProperty<T> property, EngineObject obj, IObservable<TimeSpan> time)
    {
        return Observable.FromEventPattern(
                h => obj.Edited += h,
                h => obj.Edited -= h)
            .Select(_ => Unit.Default)
            .Publish(Unit.Default).RefCount()
            .CombineLatest(time)
            .Select(t => property.GetValue(new CompositionContext(t.Second)));
    }

    public static IObservable<TResource> SubscribeEngineResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, CompositionContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        var renderContext = new CompositionContext(TimeSpan.Zero);
        TResource? resource = null;
        return Observable.FromEventPattern(
                h => obj.Edited += h,
                h => obj.Edited -= h)
            .Select(_ => Unit.Default)
            .Publish(Unit.Default).RefCount()
            .CombineLatest(time)
            .Select(t =>
            {
                renderContext.Time = t.Second;
                if (resource == null)
                {
                    resource = createResource(obj, renderContext);
                }
                else
                {
                    bool updateOnly = false;
                    resource.Update(obj, renderContext, ref updateOnly);
                }

                return (resource, resource.Version);
            })
            .DistinctUntilChanged(t => t.Version)
            .Select(t => t.resource);
    }

    /// <summary>
    /// Observes <paramref name="obj"/> as a versioned resource whose creation, update, and disposal all run on
    /// the render dispatcher, so a subscriber never races the renderer for the same resource.
    /// </summary>
    /// <remarks>
    /// Disposing the subscription cancels work that has not started yet, so a resource is never created for a
    /// subscription that was already gone.
    /// </remarks>
    public static IObservable<(TResource Resource, int Version)> SubscribeEngineVersionedResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, CompositionContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        return obj.SubscribeEngineVersionedResource(time, createResource, RenderThread.Dispatcher);
    }

    /// <param name="renderDispatcher">
    /// The dispatcher that owns the resource. Only a test passes anything but the render thread's, which is
    /// the one shared dispatcher a test must not shut down.
    /// </param>
    /// <inheritdoc cref="SubscribeEngineVersionedResource{T, TResource}(T, IObservable{TimeSpan}, Func{T, CompositionContext, TResource})"/>
    internal static IObservable<(TResource Resource, int Version)> SubscribeEngineVersionedResource<T, TResource>(
        this T obj,
        IObservable<TimeSpan> time,
        Func<T, CompositionContext, TResource> createResource,
        Dispatcher renderDispatcher)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        return Observable.Create<(TResource Resource, int Version)>(observer =>
            {
                var renderContext = new CompositionContext(TimeSpan.Zero);
                var cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;
                TResource? resource = null;
                var gate = new object();
                int runningUpdates = 0;
                bool workCancelled = false;
                bool releaseRequested = false;
                var release = new DispatcherCleanup(renderDispatcher, ReleaseResource);

                IDisposable trigger = Observable.FromEventPattern(
                        h => obj.Edited += h,
                        h => obj.Edited -= h)
                    .Select(_ => Unit.Default)
                    .Publish(Unit.Default).RefCount()
                    .CombineLatest(time)
                    .Subscribe(onNext: t =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        renderDispatcher.Dispatch(
                            () =>
                            {
                                lock (gate)
                                {
                                    runningUpdates++;
                                }

                                try
                                {
                                    if (token.IsCancellationRequested)
                                        return;

                                    renderContext.Time = t.Second;
                                    if (resource is null)
                                    {
                                        resource = createResource(obj, renderContext);
                                    }
                                    else
                                    {
                                        bool updateOnly = false;
                                        resource.Update(obj, renderContext, ref updateOnly);
                                    }

                                    observer.OnNext((resource, resource.Version));
                                }
                                catch (Exception ex)
                                {
                                    // An escaping exception unwinds the shared render-thread loop, which
                                    // installs no unhandled-exception handler.
                                    CancelPendingWork();
                                    try
                                    {
                                        resource?.Dispose();
                                    }
                                    catch (Exception disposeFailure)
                                    {
                                        ex.Data["EngineVersionedResourceDisposeFailure"] = disposeFailure;
                                    }

                                    resource = null;
                                    observer.OnError(ex);
                                }
                                finally
                                {
                                    lock (gate)
                                    {
                                        runningUpdates--;
                                    }

                                    ReleaseWhenSettled();
                                }
                            },
                            DispatchPriority.Low);
                    },
                    // Without an explicit handler Rx throws the trigger's failure on the source thread,
                    // leaving this subscription uninformed and still holding its resource.
                    onError: ex =>
                    {
                        RequestRelease();
                        observer.OnError(ex);
                    });

                return Disposable.Create(() =>
                {
                    CancelPendingWork();
                    trigger.Dispose();
                    RequestRelease();
                });

                // The release disposes the token source, and a Cancel reaching a disposed source throws.
                // Latching the cancellation under the same gate that admits the release orders the two: a
                // caller arriving after the release sees the latch and leaves the source alone.
                void CancelPendingWork()
                {
                    lock (gate)
                    {
                        if (workCancelled)
                            return;

                        workCancelled = true;
                        cts.Cancel();
                    }
                }

                void RequestRelease()
                {
                    CancelPendingWork();
                    lock (gate)
                    {
                        releaseRequested = true;
                    }

                    ReleaseWhenSettled();
                }

                void ReleaseWhenSettled()
                {
                    lock (gate)
                    {
                        // An update already recording from the resource has to settle first, even during a
                        // shutdown that releases inline; only work that never starts can be written off.
                        if (!releaseRequested || runningUpdates > 0)
                            return;
                    }

                    release.Request();
                }

                void ReleaseResource()
                {
                    // The render loop installs no unhandled-exception handler, so a throwing
                    // Dispose here would take the render thread down with it.
                    try
                    {
                        resource?.Dispose();
                    }
                    catch (Exception disposeFailure)
                    {
                        s_logger.LogWarning(
                            disposeFailure,
                            "Releasing the versioned resource for '{Object}' failed.",
                            obj);
                    }

                    resource = null;
                    cts.Dispose();
                }
            })
            .DistinctUntilChanged(t => t.Version);
    }
}
