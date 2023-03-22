using System.Collections;
using System.Collections.Specialized;

using Beutl.Collections;

namespace Beutl;

public abstract class Hierarchical : CoreObject, IHierarchical, IModifiableHierarchical
{
    public static readonly CoreProperty<IHierarchical?> HierarchicalParentProperty;
    private readonly CoreList<IHierarchical> _hierarchicalChildren;
    private IHierarchical? _parent;
    private IHierarchicalRoot? _root;

    static Hierarchical()
    {
        HierarchicalParentProperty = ConfigureProperty<IHierarchical?, Hierarchical>(nameof(HierarchicalParent))
            .Accessor(o => o.HierarchicalParent, (o, v) => o.HierarchicalParent = v)
            .Register();
    }

    public Hierarchical()
    {
        _root = this as IHierarchicalRoot;
        _hierarchicalChildren = new CoreList<IHierarchical>()
        {
            ResetBehavior = ResetBehavior.Remove
        };
        _hierarchicalChildren.CollectionChanged += HierarchicalChildrenCollectionChanged;
    }

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
}
