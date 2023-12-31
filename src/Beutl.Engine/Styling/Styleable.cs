using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Styling;

public abstract class Styleable : Animatable, IStyleable, IModifiableHierarchical
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();

        HierarchicalParentProperty = ConfigureProperty<IHierarchical?, Styleable>(nameof(HierarchicalParent))
            .Accessor(o => o.HierarchicalParent, (o, v) => o.HierarchicalParent = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = [];
        _styles.Attached += item =>
        {
            item.Invalidated += Style_Invalidated;
        };
        _styles.Detached += item =>
        {
            item.Invalidated -= Style_Invalidated;
        };
        _styles.CollectionChanged += Style_Invalidated;

        _root = this as IHierarchicalRoot;
        _hierarchicalChildren = new CoreList<IHierarchical>()
        {
            ResetBehavior = ResetBehavior.Remove
        };
        _hierarchicalChildren.CollectionChanged += HierarchicalChildrenCollectionChanged;
    }

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        _styleInstance = null;
    }

    [NotAutoSerialized]
    public Styles Styles
    {
        get => _styles;
        set
        {
            if (_styles != value)
            {
                _styles.Replace(value);
            }
        }
    }

    public void InvalidateStyles()
    {
        if (_styleInstance != null)
        {
            _styleInstance.Dispose();
            _styleInstance = null;
        }
    }

    public virtual void ApplyStyling(IClock clock)
    {
        _styleInstance ??= Styles.Instance(this);

        if (_styleInstance != null)
        {
            _styleInstance.Begin();
            _styleInstance.Apply(clock);
            _styleInstance.End();
        }
    }

    IStyleInstance? IStyleable.GetStyleInstance(IStyle style)
    {
        IStyleInstance? styleInstance = _styleInstance;
        while (styleInstance != null)
        {
            if (styleInstance.Source == style)
            {
                return styleInstance;
            }
            else
            {
                styleInstance = styleInstance.BaseStyle;
            }
        }

        return null;
    }

    void IStyleable.StyleApplied(IStyleInstance instance)
    {
        _styleInstance = instance;
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue("styles", out JsonNode? stylesNode)
            && stylesNode is JsonArray stylesArray)
        {
            Styles.Clear();
            Styles.EnsureCapacity(stylesArray.Count);

            foreach (JsonNode? styleNode in stylesArray)
            {
                if (styleNode is JsonObject styleObject
                    && styleObject.ToStyle() is Style style)
                {
                    Styles.Add(style);
                }
            }
        }
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        if (Styles.Count > 0)
        {
            var styles = new JsonArray();

            foreach (IStyle style in Styles.GetMarshal().Value)
            {
                styles.Add(style.ToJson());
            }

            json["styles"] = styles;
        }
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Styles), Styles);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if(context.GetValue<Styles>(nameof(Styles)) is { } styles)
        {
            Styles = styles;
        }
    }

    #region IHierarchical

    public static readonly CoreProperty<IHierarchical?> HierarchicalParentProperty;
    private readonly CoreList<IHierarchical> _hierarchicalChildren;
    private IHierarchical? _parent;
    private IHierarchicalRoot? _root;

    [NotAutoSerialized]
    public IHierarchical? HierarchicalParent
    {
        get => _parent;
        private set => SetAndRaise(HierarchicalParentProperty, ref _parent, value);
    }

    protected ICoreList<IHierarchical> HierarchicalChildren => _hierarchicalChildren;

    IHierarchical? IHierarchical.HierarchicalParent => _parent;

    IHierarchicalRoot? IHierarchical.HierarchicalRoot => _root;

    ICoreReadOnlyList<IHierarchical> IHierarchical.HierarchicalChildren => HierarchicalChildren;

    public event EventHandler<HierarchyAttachmentEventArgs>? AttachedToHierarchy;

    public event EventHandler<HierarchyAttachmentEventArgs>? DetachedFromHierarchy;

    protected virtual void HierarchicalChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void SetParent(IList children)
        {
            int count = children.Count;

            for (int i = 0; i < count; i++)
            {
                var logical = (IHierarchical)children[i]!;

                if (logical.HierarchicalParent is null)
                {
                    (logical as IModifiableHierarchical)?.SetParent(this);
                }
            }
        }

        void ClearParent(IList children)
        {
            int count = children.Count;

            for (int i = 0; i < count; i++)
            {
                var logical = (IHierarchical)children[i]!;

                if (logical.HierarchicalParent == this)
                {
                    (logical as IModifiableHierarchical)?.SetParent(null);
                }
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                SetParent(e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                ClearParent(e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Replace:
                ClearParent(e.OldItems!);
                SetParent(e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                break;
        }
    }

    protected virtual void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
    }

    private void OnAttachedToHierarchyCore(in HierarchyAttachmentEventArgs e)
    {
        if (_parent == null && this is not IHierarchicalRoot)
        {
            throw new InvalidOperationException(
                $"OnAttachedToHierarchyCore called for '{GetType().Name}' but element has no logical parent.");
        }

        if (_root == null)
        {
            _root = e.Root;
            OnAttachedToHierarchy(e);
            AttachedToHierarchy?.Invoke(this, e);
        }

        foreach (IHierarchical item in _hierarchicalChildren.GetMarshal().Value)
        {
            (item as IModifiableHierarchical)?.NotifyAttachedToHierarchy(new(e.Root, this));
        }
    }

    private void OnDetachedFromHierarchyCore(in HierarchyAttachmentEventArgs e)
    {
        if (_root != null)
        {
            _root = null;
            OnDetachedFromHierarchy(e);
            DetachedFromHierarchy?.Invoke(this, e);

            foreach (IHierarchical item in _hierarchicalChildren.GetMarshal().Value)
            {
                (item as IModifiableHierarchical)?.NotifyDetachedFromHierarchy(new(e.Root, this));
            }
        }
    }

    void IModifiableHierarchical.NotifyAttachedToHierarchy(in HierarchyAttachmentEventArgs e)
    {
        OnAttachedToHierarchyCore(e);
    }

    void IModifiableHierarchical.NotifyDetachedFromHierarchy(in HierarchyAttachmentEventArgs e)
    {
        OnDetachedFromHierarchyCore(e);
    }

    void IModifiableHierarchical.SetParent(IHierarchical? parent)
    {
        IHierarchical? old = _parent;

        if (parent != old)
        {
            if (old != null && parent != null)
            {
                throw new InvalidOperationException("This logical element already has a parent.");
            }

            IHierarchicalRoot? newRoot = parent?.FindHierarchicalRoot() ?? (this as IHierarchicalRoot);
            HierarchicalParent = parent;

            if (_root != null)
            {
                var e = new HierarchyAttachmentEventArgs(_root, old);
                OnDetachedFromHierarchyCore(e);
            }

            if (newRoot != null)
            {
                var e = new HierarchyAttachmentEventArgs(newRoot, parent);
                OnAttachedToHierarchyCore(e);
            }
        }
    }

    void IModifiableHierarchical.AddChild(IHierarchical child)
    {
        HierarchicalChildren.Add(child);
    }

    void IModifiableHierarchical.RemoveChild(IHierarchical child)
    {
        HierarchicalChildren.Remove(child);
    }
    #endregion
}
