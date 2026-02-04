using System.Reactive;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Rendering;

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
            .Select(t => property.GetValue(new RenderContext(t.Second)));
    }

    public static IObservable<TResource> SubscribeEngineResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, RenderContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        var renderContext = new RenderContext(TimeSpan.Zero);
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

    public static IObservable<(TResource Resource, int Version)> SubscribeEngineVersionedResource<T, TResource>(
        this T obj, IObservable<TimeSpan> time, Func<T, RenderContext, TResource> createResource)
        where T : EngineObject
        where TResource : EngineObject.Resource
    {
        var renderContext = new RenderContext(TimeSpan.Zero);
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
            .DistinctUntilChanged(t => t.Version);
    }
}
