namespace BeUtl;

public static class PropertyInitializationHelperExtensions
{
    public static T Animatable<T>(this T self, bool value = true)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.IsAnimatable, value);
        return self;
    }

    public static T Header<T>(this T self, ResourceReference<string> value)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.Header, value);
        return self;
    }

    public static T EnableEditor<T>(this T self)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.Editor, true);
        return self;
    }

    public static T DisableEditor<T>(this T self)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.Editor, false);
        return self;
    }

    public static T SuppressAutoRender<T>(this T self, bool value)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.SuppressAutoRender, value);
        return self;
    }

    public static T JsonName<T>(this T self, string value)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.JsonName, value);
        return self;
    }

    public static T FilePicker<T>(this T self, ResourceReference<string> name, params string[] extensions)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.FilePickerName, name);
        self.SetValue(PropertyMetaTableKeys.FilePickerExtensions, extensions);
        return self;
    }

    public static T AbsolutePath<T>(this T self)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.PathType, "abs");
        return self;
    }

    public static T RelativePath<T>(this T self)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.PathType, "rel");
        return self;
    }

    public static T Maximum<T>(this T self, object value)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.Maximum, value);
        return self;
    }

    public static T Minimum<T>(this T self, object value)
        where T : IPropertyInitializationHelper
    {
        self.SetValue(PropertyMetaTableKeys.Minimum, value);
        return self;
    }
}
