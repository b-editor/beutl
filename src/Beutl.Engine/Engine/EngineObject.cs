using System.Linq.Expressions;
using System.Reflection;
using Beutl;
using Beutl.Media;

namespace Beutl.Engine;

public class EngineObject : Hierarchical
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<TimeRange> TimeRangeProperty;
    private bool _isEnabled = true;
    private int _zIndex;
    private TimeRange _timeRange;

    private readonly List<IProperty> _properties = new();

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    static EngineObject()
    {
        IsEnabledProperty = ConfigureProperty<bool, EngineObject>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        ZIndexProperty = ConfigureProperty<int, EngineObject>(nameof(ZIndex))
            .Accessor(o => o.ZIndex, (o, v) => o.ZIndex = v)
            .Register();

        TimeRangeProperty = ConfigureProperty<TimeRange, EngineObject>(nameof(TimeRange))
            .Accessor(o => o.TimeRange, (o, v) => o.TimeRange = v)
            .Register();

        AffectsRender<EngineObject>(IsEnabledProperty);
    }

    protected EngineObject()
    {
        // TODO: Propertiesの変更を検出する
        // AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public virtual IReadOnlyList<IProperty> Properties => _properties;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public int ZIndex
    {
        get => _zIndex;
        set => SetAndRaise(ZIndexProperty, ref _zIndex, value);
    }

    public TimeRange TimeRange
    {
        get => _timeRange;
        set => SetAndRaise(TimeRangeProperty, ref _timeRange, value);
    }

    internal int Version { get; private set; }

    private void AffectsRender_Invalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        RaiseInvalidated(e);
    }

    // TODO: 不要になる予定
    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : EngineObject
    {
        foreach (CoreProperty item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                    {
                        oldAffectsRender.Invalidated -= s.AffectsRender_Invalidated;
                    }

                    if (e.NewValue is IAffectsRender newAffectsRender)
                    {
                        newAffectsRender.Invalidated += s.AffectsRender_Invalidated;
                    }
                }
            });
        }
    }

    public void Invalidate()
    {
        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
        unchecked
        {
            Version++;
        }
    }

    protected void ScanProperties<T>() where T : EngineObject
    {
        var type = typeof(T);
        var propertyInfos = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var propertyInfo in propertyInfos)
        {
            if (!typeof(IProperty).IsAssignableFrom(propertyInfo.PropertyType)) continue;

            var func = ReflectionCache<T>.Properties.FirstOrDefault(x => x.Item1 == propertyInfo).Item2;
            if (func == null)
            {
                var param = Expression.Parameter(typeof(object), "o");
                var cast = Expression.Convert(param, type);
                var propertyAccess = Expression.Property(cast, propertyInfo);
                var convertResult = Expression.Convert(propertyAccess, typeof(IProperty));
                var lambda = Expression.Lambda<Func<object, IProperty?>>(convertResult, param);
                func = lambda.Compile();
                ReflectionCache<T>.Properties.Add((propertyInfo, func));
            }

            var property = func(this);
            if (property != null)
            {
                RegisterProperty(property);

                if (propertyInfo.PropertyType.IsGenericType
                    && propertyInfo.PropertyType.GetGenericTypeDefinition() == typeof(IProperty<>))
                {
                    Type valueType = propertyInfo.PropertyType.GetGenericArguments()[0];
                    MethodInfo registerCore = typeof(EngineObject)
                        .GetMethod(nameof(RegisterPropertyCore), BindingFlags.NonPublic | BindingFlags.Instance)!;
                    MethodInfo generic = registerCore.MakeGenericMethod(valueType);
                    generic.Invoke(this, new object[] { property, propertyInfo });
                }
            }
        }
    }

    protected void RegisterProperty(IProperty property)
    {
        if (!_properties.Contains(property))
        {
            _properties.Add(property);
        }
    }

    private void RegisterPropertyCore<T>(IProperty<T> property, PropertyInfo propertyInfo)
    {
        property.SetPropertyInfo(propertyInfo);

        AttachPropertyValue(property.CurrentValue);

        property.ValueChanged += (sender, args) =>
        {
            DetachPropertyValue(args.OldValue);
            AttachPropertyValue(args.NewValue);
            RaiseInvalidated(new RenderInvalidatedEventArgs(this, property.Name));
        };
    }

    private void AttachPropertyValue(object? value)
    {
        if (value is IAffectsRender affectsRender)
        {
            affectsRender.Invalidated += AffectsRender_Invalidated;
        }

        if (value is Hierarchical hierarchical && this is IModifiableHierarchical modifiable)
        {
            modifiable.AddChild(hierarchical);
        }
    }

    private void DetachPropertyValue(object? value)
    {
        if (value is IAffectsRender affectsRender)
        {
            affectsRender.Invalidated -= AffectsRender_Invalidated;
        }

        if (value is Hierarchical hierarchical && this is IModifiableHierarchical modifiable)
        {
            modifiable.RemoveChild(hierarchical);
        }
    }

    private static class ReflectionCache<T>
    {
        public static readonly List<(PropertyInfo, Func<object, IProperty?>)> Properties = new();
    }
}
