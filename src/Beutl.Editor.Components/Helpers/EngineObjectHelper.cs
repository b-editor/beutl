using System.Reactive;
using System.Reactive.Disposables;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Rendering;
using Beutl.Threading;

namespace Beutl.Editor.Components.Helpers;

public static class EngineObjectHelper
{
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
        return Observable.Create<(TResource Resource, int Version)>(observer =>
            {
                var renderContext = new CompositionContext(TimeSpan.Zero);
                var cts = new CancellationTokenSource();
                CancellationToken token = cts.Token;
                TResource? resource = null;

                IDisposable trigger = Observable.FromEventPattern(
                        h => obj.Edited += h,
                        h => obj.Edited -= h)
                    .Select(_ => Unit.Default)
                    .Publish(Unit.Default).RefCount()
                    .CombineLatest(time)
                    .Subscribe(t =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        RenderThread.Dispatcher.Dispatch(
                            () =>
                            {
                                if (token.IsCancellationRequested)
                                    return;

                                try
                                {
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
                                    cts.Cancel();
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
                            },
                            DispatchPriority.Low);
                    });

                return Disposable.Create(() =>
                {
                    cts.Cancel();
                    trigger.Dispose();
                    RenderThread.Dispatcher.Dispatch(
                        () =>
                        {
                            resource?.Dispose();
                            resource = null;
                            cts.Dispose();
                        },
                        DispatchPriority.Low);
                });
            })
            .DistinctUntilChanged(t => t.Version);
    }
}
