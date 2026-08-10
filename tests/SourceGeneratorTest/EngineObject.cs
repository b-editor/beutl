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
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public class Resource : IDisposable
    {
        private readonly object _resourceOwnershipGate = new();
        private int _disposeState;
        private EngineObject _original = null!;

        public int Version { get; protected set; }

        public EngineObject GetOriginal() => _original;

        public virtual void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            _original = obj;
        }

        public void Dispose()
        {
            lock (_resourceOwnershipGate)
            {
                if (System.Threading.Volatile.Read(ref _disposeState) != 0)
                {
                    return;
                }

                Dispose(true);
                System.Threading.Volatile.Write(ref _disposeState, 1);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
        }

        protected TResource? ExchangeOwnedResource<TResource>(
            ref TResource? location,
            TResource? value)
            where TResource : Resource
        {
            lock (_resourceOwnershipGate)
            {
                if (System.Threading.Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                return System.Threading.Interlocked.Exchange(ref location, value);
            }
        }

        protected void SetOwnedResource<TResource>(
            ref TResource? location,
            TResource? value)
            where TResource : Resource
        {
            lock (_resourceOwnershipGate)
            {
                if (System.Threading.Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                TResource? current = location;
                if (ReferenceEquals(current, value))
                    return;

                current?.Dispose();
                System.Threading.Interlocked.Exchange(ref location, value);
            }
        }

        protected TResource ReplaceOwnedResource<TResource>(
            ref TResource? location,
            TResource replacement)
            where TResource : Resource
        {
            lock (_resourceOwnershipGate)
            {
                if (System.Threading.Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                ArgumentNullException.ThrowIfNull(replacement);
                TResource current = location ?? throw new InvalidOperationException();
                if (ReferenceEquals(current, replacement))
                {
                    throw new ArgumentException(null, nameof(replacement));
                }

                return System.Threading.Interlocked.Exchange(ref location, replacement)!;
            }
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
            lock (_resourceOwnershipGate)
            {
                if (System.Threading.Volatile.Read(ref _disposeState) != 0)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                var value = context.Get(prop);
                if (value is null)
                {
                    if (field is not null)
                    {
                        SetOwnedResource(ref field, default);
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
                        SetOwnedResource(ref field, (TResource)value.ToResource(context));
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
                            SetOwnedResource(ref field, (TResource)value.ToResource(context));
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
}
