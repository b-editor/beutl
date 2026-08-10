using System;
using System.Collections.Generic;

using Beutl.Composition;

namespace Beutl.Engine;

public class EngineObject
{
    protected readonly struct ResourceDefaultValuesConstruction
    {
    }

    public EngineObject()
    {
    }

    protected EngineObject(ResourceDefaultValuesConstruction construction)
    {
    }

    public virtual IReadOnlyList<IProperty> Properties => throw null!;

    internal int Version { get; private set; }

    public bool IsEnabled { get; set; } = true;

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : EngineObject
    {
        throw null!;
    }

    public virtual Resource ToResource(CompositionContext context)
    {
        var resource = new EngineObject.Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public class Resource : IDisposable
    {
        public Resource()
        {
            IsEnabled = true;
        }

        protected Resource(EngineObject defaultValues)
        {
            IsEnabled = defaultValues.IsEnabled;
        }

        protected Resource(bool skipDefaultInitialization)
        {
            if (!skipDefaultInitialization)
            {
                throw new ArgumentException(
                    "Attached-resource construction must explicitly opt out of detached default initialization.",
                    nameof(skipDefaultInitialization));
            }
        }

        private EngineObject? _original;

        public int Version { get; protected set; }

        public bool IsEnabled { get; set; }

        public bool IsAttached => _original is not null;

        public EngineObject? GetOriginal() => _original;

        public EngineObject RequireOriginal()
        {
            return _original ?? throw new InvalidOperationException(
                $"{GetType()} was constructed directly rather than through {nameof(EngineObject)}.{nameof(ToResource)}, "
                + "so it has no backing engine object to dispatch to.");
        }

        public virtual void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
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

        protected void CompareAndUpdate<TValue>(CompositionContext context, IProperty<TValue> prop, ref TValue field, ref bool updateOnly)
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

        protected void CompareAndUpdateList<TItem, TResource>(CompositionContext context, IList<TItem> prop, ref List<TResource> field, ref bool updateOnly) where TItem : EngineObject where TResource : Resource
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
                        Version++;
                        updateOnly = true;
                    }
                    else
                    {
                        var oldVersion = item.Version;
                        item.Update(child, context, ref updateOnly);
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
            while (field.Count > prop.Count)
            {
                field.RemoveAt(field.Count - 1);
            }
        }
        protected void CompareAndUpdateObject<TObject, TResource>(CompositionContext context, IProperty<TObject> prop, ref TResource field, ref bool updateOnly) where TObject : EngineObject where TResource : Resource
        {
            var value = context.Get(prop);
            if (value is null)
            {
                if (field is not null)
                {
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
                        field = (TResource)value.ToResource(context);
                        Version++;
                        updateOnly = true;
                    }
                    else
                    {
                        var oldVersion = value.Version;
                        field.Update(value, context, ref updateOnly);
                        if (!updateOnly && oldVersion != field.Version)
                        {
                            Version++;
                            updateOnly = true;
                        }
                    }
                }
            }
        }
    }
}
