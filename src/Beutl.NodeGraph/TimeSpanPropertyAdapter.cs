using System.ComponentModel.DataAnnotations;
using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Extensibility;

namespace Beutl.NodeGraph;

// A MediaTime property as the TimeSpan port that drives it sees it, until #2885 step 4.
internal sealed class TimeSpanPropertyAdapter(IExpressionProperty<MediaTime> property, EngineObject obj)
    : IPropertyAdapter<TimeSpan>, IChildObjectSource
{
    private IObservable<TimeSpan>? _observable;

    public Type ImplementedType => obj.GetType();

    public Type PropertyType => typeof(TimeSpan);

    public string DisplayName => Display?.GetName() ?? property.Name;

    public string? Description => Display?.GetDescription();

    public bool IsReadOnly => false;

    private DisplayAttribute? Display => property.GetAttributes()?.OfType<DisplayAttribute>().FirstOrDefault();

    IProperty IPropertyAdapter.GetEngineProperty() => property;

    IEnumerable<object?> IChildObjectSource.GetChildObjects() => [property];

    public object? GetDefaultValue() => property.DefaultValue.ToTimeSpan();

    public TimeSpan GetValue() => property.Value.ToTimeSpan();

    // A time past the range of MediaTime clamps.
    public void SetValue(TimeSpan value) => property.Value = MediaTime.FromTicks(value.Ticks, MidpointRounding.AwayFromZero);

    public IObservable<TimeSpan> GetObservable() => _observable ??= Observable
        .FromEventPattern<PropertyValueChangedEventArgs<MediaTime>>(
            handler => property.ValueChanged += handler,
            handler => property.ValueChanged -= handler)
        .Select(e => e.EventArgs.NewValue.ToTimeSpan())
        .Publish(property.Value.ToTimeSpan())
        .RefCount();
}
