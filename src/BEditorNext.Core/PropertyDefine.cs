namespace BEditorNext;

public class PropertyDefine
{
    private static readonly object s_lock = new();
    private static int s_nextId = 0;

    public PropertyDefine(IDictionary<string, object> metaTable)
    {
        MetaTable = metaTable;

        lock (s_lock)
        {
            this.SetKeyValue(PropertyMetaTableKeys.Id, s_nextId);

            s_nextId++;
        }
    }

    public string Name
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Name, out object? val) &&
                val is string result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public Type PropertyType
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.PropertyType, out object? val) &&
                val is Type result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public Type OwnerType
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.OwnerType, out object? val) &&
                val is Type result)
            {
                return result;
            }

            throw new KeyNotFoundException();
        }
    }

    public bool IsAttached
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.IsAttached, out object? val) &&
                val is bool result)
            {
                return result;
            }

            return false;
        }
    }

    public bool IsAutomatic
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.IsAutomatic, out object? val) &&
                val is bool result)
            {
                return result;
            }

            return false;
        }
    }

    public int Id
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Id, out object? val) &&
                val is int result)
            {
                return result;
            }

            return -1;
        }
    }

    public IDictionary<string, object> MetaTable { get; }
}

public class PropertyDefine<T> : PropertyDefine
{
    public PropertyDefine(IDictionary<string, object> metaTable)
        : base(metaTable)
    {
        this.SetKeyValue(PropertyMetaTableKeys.PropertyType, typeof(T));
    }
}
