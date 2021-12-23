namespace BEditorNext;

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

    public static PropertyDefine<T> Animatable<T>(this PropertyDefine<T> define, bool value = true)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.IsAnimatable, value);
    }

    //public static PropertyDefine<T> Easing<T>(this PropertyDefine<T> define, Easing easing);

    public static PropertyDefine<T> Header<T>(this PropertyDefine<T> define, ResourceReference<string> value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.Header, value);
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

    public static PropertyDefine<T> SuppressAutoRender<T>(this PropertyDefine<T> define, bool value)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.SuppressAutoRender, value);
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

    public static PropertyDefine<T> FilePicker<T>(this PropertyDefine<T> define, ResourceReference<string> name, params string[] extensions)
    {
        return define.SetKeyValue(PropertyMetaTableKeys.FilePickerName, name)
            .SetKeyValue(PropertyMetaTableKeys.FilePickerExtensions, extensions);
    }

    public static PropertyDefine<T> DirectoryPicker<T>(this PropertyDefine<T> define)
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

    public static T GetValueOrDefault<T>(this PropertyDefine define, string key, T defaltValue)
    {
        if (!define.MetaTable.ContainsKey(key))
        {
            return defaltValue;
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

    public static Func<PropertyDefine, object, T> GetGetter<T>(this PropertyDefine<T> define)
    {
        return define.GetValue<Func<PropertyDefine, object, T>>(PropertyMetaTableKeys.Getter);
    }

    public static Action<PropertyDefine, object, T> GetSetter<T>(this PropertyDefine<T> define)
    {
        return define.GetValue<Action<PropertyDefine, object, T>>(PropertyMetaTableKeys.Setter);
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

    public static bool IsAnimatable(this PropertyDefine define)
    {
        return define.GetValueOrDefault(PropertyMetaTableKeys.IsAnimatable, false);
    }
}
