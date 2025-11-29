using Beutl.Graphics.Rendering;

namespace Beutl.Engine;

public class EngineObject : Hierarchical
{
    public virtual IReadOnlyList<IProperty> Properties => throw null!;

    internal int Version { get; private set; }

    protected void ScanProperties<T>() where T : EngineObject
    {
        throw null!;
    }


    public virtual Resource ToResource(RenderContext context)
    {
        var resource = new EngineObject.Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public class Resource
    {
        private EngineObject _original = null!;

        public int Version { get; protected set; }

        public EngineObject GetOriginal() => _original;

        public virtual void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            _original = obj;
        }

        protected void CompareAndUpdate<TValue>(RenderContext context, IProperty<TValue> prop, ref TValue field, ref bool updateOnly)
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

        protected void CompareAndUpdateList<TItem, TResource>(RenderContext context, IList<TItem> prop, ref List<TResource> field, ref bool updateOnly) where TItem : EngineObject where TResource : Resource
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
        protected void CompareAndUpdateObject<TObject, TResource>(RenderContext context, IProperty<TObject> prop, ref TResource field, ref bool updateOnly) where TObject : EngineObject where TResource : Resource
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
