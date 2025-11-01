using System.ComponentModel;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using System.Reflection;
using Beutl;
using Beutl.Animation;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Reactive;
using Beutl.Serialization;

namespace Beutl.Engine;

public class EngineObject : Hierarchical, INotifyEdited
{
    // これらのプロパティは描画時ではなく編集時に更新されるべき
    public static readonly CoreProperty<bool> IsTimeAnchorProperty;
    public static readonly CoreProperty<bool> IsEnabledProperty;
    public static readonly CoreProperty<int> ZIndexProperty;
    public static readonly CoreProperty<TimeRange> TimeRangeProperty;
    private bool _isTimeAnchor;
    private bool _isEnabled = true;
    private int _zIndex;
    private TimeRange _timeRange;
    private IDisposable? _timeAnchorSubscription;
    private readonly List<IProperty> _properties = new();

    public event EventHandler? Edited;

    static EngineObject()
    {
        IsTimeAnchorProperty = ConfigureProperty<bool, EngineObject>(nameof(IsTimeAnchor))
            .Accessor(o => o.IsTimeAnchor, (o, v) => o.IsTimeAnchor = v)
            .DefaultValue(false)
            .Register();

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

        AffectsRender<EngineObject>(IsEnabledProperty, IsTimeAnchorProperty, ZIndexProperty, TimeRangeProperty);
    }

    public virtual IReadOnlyList<IProperty> Properties => _properties;

    public bool IsTimeAnchor
    {
        get => _isTimeAnchor;
        set => SetAndRaise(IsTimeAnchorProperty, ref _isTimeAnchor, value);
    }

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

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<bool> boolArgs)
        {
            if (boolArgs.Property.Id == IsTimeAnchorProperty.Id)
            {
                if (boolArgs.NewValue)
                {
                    RevokeTimeAnchorSubscription();
                }
                else
                {
                    SubscribeTimeAnchor();
                }
            }
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(in args);
        if (IsTimeAnchor) return;

        SubscribeTimeAnchor();
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(in args);
        RevokeTimeAnchorSubscription();
    }

    private void RevokeTimeAnchorSubscription()
    {
        _timeAnchorSubscription?.Dispose();
        _timeAnchorSubscription = null;
    }

    private void SubscribeTimeAnchor()
    {
        _timeAnchorSubscription?.Dispose();

        var parent = this.FindHierarchicalParent<EngineObject>();
        if (parent == null) return;

        var d1 = parent.GetObservable(TimeRangeProperty)
            .Subscribe(t => TimeRange = t);

        var d2 = parent.GetObservable(ZIndexProperty)
            .Subscribe(z => ZIndex = z);

        _timeAnchorSubscription = Disposable.Create((d1, d2), t => t.DisposeAll());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        Dictionary<string, IAnimation>? animations
            = context.GetValue<Dictionary<string, IAnimation>>("Animations");

        foreach (IProperty property in _properties)
        {
            property.DeserializeValue(context);
            if (property.IsAnimatable && animations?.TryGetValue(property.Name, out IAnimation? value) == true)
            {
                property.Animation = value;
            }
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        Dictionary<string, IAnimation> animations = _properties.Where(p=> p is { IsAnimatable: true, Animation: not null })
            .ToDictionary(p => p.Name, p => p.Animation!);

        context.SetValue("Animations", animations);
        foreach (IProperty property in _properties)
        {
            property.SerializeValue(context);
        }
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
                    s.RaiseEdited();

                    if (e.OldValue is INotifyEdited oldAffectsRender)
                    {
                        oldAffectsRender.Edited -= s.OnPropertyEdited;
                    }

                    if (e.NewValue is INotifyEdited newAffectsRender)
                    {
                        newAffectsRender.Edited += s.OnPropertyEdited;
                    }
                }
            });
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
                property.SetPropertyInfo(propertyInfo);
                property.SetOwnerObject(this);
            }
        }
    }

    protected void RegisterProperty(IProperty property)
    {
        if (!_properties.Contains(property))
        {
            _properties.Add(property);
            property.Edited += OnPropertyEdited;
        }
    }

    private void OnPropertyEdited(object? sender, EventArgs e)
    {
        Edited?.Invoke(sender, e);
    }

    protected void RaiseEdited()
    {
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public virtual Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public class Resource : IDisposable
    {
        ~Resource()
        {
            Dispose(false);
        }

        private EngineObject _original = null!;

        public int Version { get; set; }

        public bool IsEnabled { get; set; }

        public bool IsDisposed { get; private set; }

        public EngineObject GetOriginal() => _original;

        public virtual void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _original = obj;
            if (IsEnabled != obj.IsEnabled)
            {
                IsEnabled = obj.IsEnabled;
                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
                }
            }
        }

        protected void CompareAndUpdate<TValue>(RenderContext context, IProperty<TValue> prop, ref TValue field,
            ref bool updateOnly)
        {
            TValue newValue = context.Get(prop);
            TValue oldValue = field;
            field = newValue;
            if (updateOnly)
            {
                return;
            }

            if (!EqualityComparer<TValue>.Default.Equals(newValue, oldValue))
            {
                Version++;
                updateOnly = true;
            }
        }

        protected void CompareAndUpdateList<TItem, TResource>(RenderContext context, IList<TItem> prop,
            ref List<TResource> field, ref bool updateOnly) where TItem : EngineObject where TResource : Resource
        {
            for (int i = 0; i < prop.Count; i++)
            {
                var child = prop[i];
                if (i < field.Count)
                {
                    var item = field[i];
                    if (item.GetOriginal() != child)
                    {
                        var oldItem = item;
                        item = (TResource)child.ToResource(context);
                        field[i] = item;
                        Version++;
                        updateOnly = true;
                        oldItem.Dispose();
                    }
                    else
                    {
                        var oldVersion = item.Version;
                        var _ = false;
                        item.Update(child, context, ref _);
                        if (!updateOnly && oldVersion != item.Version)
                        {
                            Version++;
                            updateOnly = true;
                        }
                    }
                }
                else
                {
                    var item = (TResource)child.ToResource(context);
                    field.Add(item);
                    if (!updateOnly)
                    {
                        Version++;
                        updateOnly = true;
                    }
                }
            }

            if (!updateOnly && field.Count != prop.Count)
            {
                Version++;
                updateOnly = true;
            }

            while (field.Count > prop.Count)
            {
                var oldItem = field[^1];
                field.RemoveAt(field.Count - 1);
                oldItem.Dispose();
            }
        }

        protected void CompareAndUpdateObject<TObject, TResource>(RenderContext context, IProperty<TObject> prop,
            ref TResource? field, ref bool updateOnly) where TObject : EngineObject where TResource : Resource
        {
            var value = context.Get(prop);
            if (value is null)
            {
                if (field is not null)
                {
                    field.Dispose();
                    field = null;
                    if (!updateOnly)
                    {
                        Version++;
                        updateOnly = true;
                    }
                }
            }
            else
            {
                if (field is null)
                {
                    field = (TResource)value.ToResource(context);
                    if (!updateOnly)
                    {
                        Version++;
                        updateOnly = true;
                    }
                }
                else
                {
                    if (field.GetOriginal() != value)
                    {
                        var oldField = field;
                        field = (TResource)value.ToResource(context);
                        Version++;
                        updateOnly = true;
                        oldField.Dispose();
                    }
                    else
                    {
                        var oldVersion = field.Version;
                        var _ = false;
                        field.Update(value, context, ref _);
                        if (!updateOnly && oldVersion != field.Version)
                        {
                            Version++;
                            updateOnly = true;
                        }
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            Dispose(true);
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private static class ReflectionCache<T>
    {
        public static readonly List<(PropertyInfo, Func<object, IProperty?>)> Properties = new();
    }
}
