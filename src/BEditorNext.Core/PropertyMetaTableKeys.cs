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
