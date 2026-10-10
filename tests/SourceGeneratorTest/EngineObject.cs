using System;
using System.Collections.Generic;

using Beutl.Composition;

namespace Beutl.Engine;

public class EngineObject
{
    public virtual IReadOnlyList<IProperty> Properties => throw null!;

    internal int Version { get; private set; }

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : EngineObject
    {
        throw null!;
    }

    public virtual Resource ToResource(CompositionContext context)
    {
        var resource = new EngineObject.Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public class Resource : IDisposable
    {
        private EngineObject? _original;

        public int Version { get; set; }

        public bool IsAttached => _original is not null;

        public EngineObject? GetOriginal() => _original;

        public EngineObject RequireOriginal()
        {
            return _original ?? throw new InvalidOperationException(
                $"{GetType()} was constructed directly rather than through {nameof(EngineObject)}.{nameof(ToResource)}, "
                + "so it has no backing engine object to dispatch to.");
        }

        public virtual void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            _original = obj;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}

public static class ResourceReconciler
{
    public static void ReconcileValue<TValue>(EngineObject.Resource owner, CompositionContext context, IProperty<TValue> prop, ref TValue field, ref bool versionBumped)
    {
        TValue newValue = context.Get(prop);
        TValue oldValue = field;
        field = newValue;
        if (versionBumped)
        {
            return;
        }
        if (!EqualityComparer<TValue>.Default.Equals(newValue, oldValue))
        {
            owner.Version++;
            versionBumped = true;
        }
    }

    public static void ReconcileChildren<TItem, TResource>(EngineObject.Resource owner, CompositionContext context, IList<TItem> prop, ref List<TResource> field, ref bool versionBumped) where TItem : EngineObject where TResource : EngineObject.Resource
    {
        for (int i = 0; i < prop.Count; i++)
        {
            var child = prop[i];
            if (i < field.Count)
            {
                var item = field[i];
                if (item.GetOriginal() != child)
                {
                    item = (TResource)child.ToResource(context);
                    field[i] = item;
                    owner.Version++;
                    versionBumped = true;
                }
                else
                {
                    var oldVersion = item.Version;
                    item.Reconcile(child, context, ref versionBumped);
                    if (!versionBumped && oldVersion != item.Version)
                    {
                        owner.Version++;
                        versionBumped = true;
                    }
                }
            }
            else
            {
                var item = (TResource)child.ToResource(context);
                field.Add(item);
                if (!versionBumped)
                {
                    owner.Version++;
                    versionBumped = true;
                }
            }
        }
        while (field.Count > prop.Count)
        {
            field.RemoveAt(field.Count - 1);
        }
    }
    public static void ReconcileChild<TObject, TResource>(EngineObject.Resource owner, CompositionContext context, IProperty<TObject> prop, ref TResource? field, ref bool versionBumped) where TObject : EngineObject? where TResource : EngineObject.Resource
    {
        var value = context.Get(prop);
        if (value is null)
        {
            if (field is not null)
            {
                field.Dispose();
                field = null;
                if (!versionBumped)
                {
                    owner.Version++;
                    versionBumped = true;
                }
            }
        }
        else
        {
            if (field is null)
            {
                field = (TResource)value.ToResource(context);
                if (!versionBumped)
                {
                    owner.Version++;
                    versionBumped = true;
                }
            }
            else
            {
                if (field.GetOriginal() != value)
                {
                    var oldField = field;
                    field = (TResource)value.ToResource(context);
                    owner.Version++;
                    versionBumped = true;
                    oldField.Dispose();
                }
                else
                {
                    var oldVersion = field.Version;
                    var _ = false;
                    field.Reconcile(value, context, ref _);
                    if (!versionBumped && oldVersion != field.Version)
                    {
                        owner.Version++;
                        versionBumped = true;
                    }
                }
            }
        }
    }
}
