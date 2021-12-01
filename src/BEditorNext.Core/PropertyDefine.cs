using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace BEditorNext;

public static class PropertyMetaTableKeys
{
    public const string Name = "name";
    public const string Id = "id";
    // Func<PropertyDefine, object, object>
    public const string Getter = "getter";
    // Action<PropertyDefine, object, object>
    public const string Setter = "setter";
    public const string GenericsGetter = "genericsGetter";
    public const string GenericsSetter = "genericsSetter";
    public const string PropertyType = "propertyType";
    public const string OwnerType = "ownerType";
    public const string IsAutomatic = "isAutomatic";
    public const string IsAttached = "isAttached";
    public const string AnimationIsEnabled = "animationIsEnabled";
    public const string Easing = "easing";
    public const string Label = "label";
    public const string Editor = "editor";
    public const string DefaultValue = "defaultValue";
    public const string JsonName = "jsonName";
    public const string XmlName = "xmlName";
    public const string NotifyPropertyChanged = "notifyPropertyChanged";
    public const string NotifyPropertyChanging = "notifyPropertyChanging";
    public const string FilePickerName = "filePicker.name";
    public const string FilePickerExtensions = "filePicker.extensions";
    public const string DirectoryPicker = "directoryPicker";
    public const string PathType = "pathType";
    public const string UseDocumentEditor = "useDocumentEditor";
    public const string Maximum = "maximum";
    public const string Minimum = "minimum";
}

public class PropertyDefine
{
    private static readonly object _lock = new();
    private static int _nextId = 0;

    public PropertyDefine(IDictionary<string, object> metaTable)
    {
        MetaTable = metaTable;

        lock (_lock)
        {
            this.SetKeyValue(PropertyMetaTableKeys.Id, _nextId);

            _nextId++;
        }
    }

    public string Name
    {
        get
        {
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Name, out var val) && val is string result)
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
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.PropertyType, out var val) && val is Type result)
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
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.OwnerType, out var val) && val is Type result)
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
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.IsAttached, out var val) && val is bool result)
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
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.IsAutomatic, out var val) && val is bool result)
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
            if (MetaTable.TryGetValue(PropertyMetaTableKeys.Id, out var val) && val is int result)
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

public static class PropertyDefineExtensions
{
    public static PropertyDefine<T> SetKeyValue<T>(this PropertyDefine<T> define, string key, object value)
    {
        if (define.MetaTable.ContainsKey(PropertyMetaTableKeys.Id))
        {
            define.MetaTable[key] = value;
        }
        else
        {
            define.MetaTable.Add(key, value);
        }

        return define;
    }

    public static PropertyDefine SetKeyValue(this PropertyDefine define, string key, object value)
    {
        if (define.MetaTable.ContainsKey(PropertyMetaTableKeys.Id))
        {
            define.MetaTable[key] = value;
        }
        else
        {
            define.MetaTable.Add(key, value);
        }

        return define;
    }

    public static PropertyDefine<T> EnableAnimation<T>(this PropertyDefine<T> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.AnimationIsEnabled, true);
    }

    public static PropertyDefine<T> DisableAnimation<T>(this PropertyDefine<T> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.AnimationIsEnabled, false);
    }

    //public static PropertyDefine<T> Easing<T>(this PropertyDefine<T> define, Easing easing);

    public static PropertyDefine<T> Label<T>(this PropertyDefine<T> define, string value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Label, value);
    }

    public static PropertyDefine<T> DefaultValue<T>(this PropertyDefine<T> define, T value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.DefaultValue, value!);
    }

    public static PropertyDefine<T> EnableEditor<T>(this PropertyDefine<T> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Editor, true);
    }

    public static PropertyDefine<T> DisableEditor<T>(this PropertyDefine<T> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Editor, false);
    }

    public static PropertyDefine DefaultValue(this PropertyDefine define, object value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.DefaultValue, value);
    }

    public static PropertyDefine<T> JsonName<T>(this PropertyDefine<T> define, string value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.JsonName, value);
    }

    public static PropertyDefine JsonName(this PropertyDefine define, string value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.JsonName, value);
    }

    public static PropertyDefine<T> NotifyPropertyChanged<T>(this PropertyDefine<T> define, bool value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.NotifyPropertyChanged, value);
    }

    public static PropertyDefine NotifyPropertyChanged(this PropertyDefine define, bool value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.NotifyPropertyChanged, value);
    }

    public static PropertyDefine<T> NotifyPropertyChanging<T>(this PropertyDefine<T> define, bool value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.NotifyPropertyChanging, value);
    }

    public static PropertyDefine NotifyPropertyChanging(this PropertyDefine define, bool value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.NotifyPropertyChanging, value);
    }

    public static PropertyDefine<T> Getter<T, TOwner>(this PropertyDefine<T> define, Func<TOwner, T> value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.GenericsGetter, value);
    }

    public static PropertyDefine<T> Setter<T, TOwner>(this PropertyDefine<T> define, Action<TOwner, T> value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.GenericsSetter, value);
    }

    public static PropertyDefine<string> FilePicker(this PropertyDefine<string> define, string name, params string[] extensions)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.FilePickerName, name)
            .SetKeyValue(PropertyMetaTableKeys.FilePickerName, extensions);
    }

    public static PropertyDefine<string> DirectoryPicker(this PropertyDefine<string> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.DirectoryPicker, new object());
    }

    public static PropertyDefine<string> AbsolutePath(this PropertyDefine<string> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.PathType, "abs");
    }

    public static PropertyDefine<string> RelativePath(this PropertyDefine<string> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.PathType, "rel");
    }

    public static PropertyDefine<string> Document(this PropertyDefine<string> define)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.UseDocumentEditor, new object());
    }

    public static PropertyDefine<T> Maximum<T>(this PropertyDefine<T> define, T value)
        where T : notnull
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Maximum, value);
    }

    public static PropertyDefine<T> Minimum<T>(this PropertyDefine<T> define, T value)
        where T : notnull
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Minimum, value);
    }

    public static T GetValue<T>(this PropertyDefine define, string key)
    {
        if (!define.MetaTable.ContainsKey(key))
        {
            throw new KeyNotFoundException();
        }

        return (T)define.MetaTable[key];
    }

    public static T? GetValueOrDefault<T>(this PropertyDefine define, string key)
    {
        if (!define.MetaTable.ContainsKey(key))
        {
            return default;
        }

        return (T)define.MetaTable[key];
    }

    public static Func<TOwner, T> GetGetter<T, TOwner>(this PropertyDefine<T> define)
    {
        return define.GetValue<Func<TOwner, T>>(PropertyMetaTableKeys.GenericsGetter);
    }

    public static Action<TOwner, T> GetSetter<T, TOwner>(this PropertyDefine<T> define)
    {
        return define.GetValue<Action<TOwner, T>>(PropertyMetaTableKeys.GenericsSetter);
    }

    public static Func<PropertyDefine, object, object?> GetGetter(this PropertyDefine define)
    {
        return define.GetValue<Func<PropertyDefine, object, object?>>(PropertyMetaTableKeys.Getter);
    }

    public static Action<PropertyDefine, object, object> GetSetter(this PropertyDefine define)
    {
        return define.GetValue<Action<PropertyDefine, object, object>>(PropertyMetaTableKeys.Setter);
    }

    public static bool GetNotifyPropertyChanged(this PropertyDefine define)
    {
        return define.GetValueOrDefault<bool>(PropertyMetaTableKeys.NotifyPropertyChanged);
    }

    public static bool GetNotifyPropertyChanging(this PropertyDefine define)
    {
        return define.GetValueOrDefault<bool>(PropertyMetaTableKeys.NotifyPropertyChanging);
    }

    public static T? GetDefaultValue<T>(this PropertyDefine<T> define)
    {
        return define.GetValueOrDefault<T>(PropertyMetaTableKeys.DefaultValue);
    }

    public static object? GetDefaultValue(this PropertyDefine define)
    {
        return define.GetValueOrDefault<object>(PropertyMetaTableKeys.DefaultValue);
    }

    public static string? GetJsonName(this PropertyDefine define)
    {
        return define.GetValueOrDefault<string>(PropertyMetaTableKeys.JsonName);
    }
}