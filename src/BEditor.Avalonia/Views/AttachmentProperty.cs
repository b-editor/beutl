
using Avalonia;

namespace BEditor.Views
{
    public class AttachmentProperty : AvaloniaObject
    {
        public static readonly AttachedProperty<int> IntProperty = AvaloniaProperty.RegisterAttached<AttachmentProperty, AvaloniaObject, int>("Int", 0);

        public static int GetInt(AvaloniaObject obj)
        {
            return obj.GetValue(IntProperty);
        }
        public static void SetInt(AvaloniaObject obj, int value)
        {
            obj.SetValue(IntProperty, value);
        }
    }
}