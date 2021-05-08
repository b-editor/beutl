using System.Windows;

namespace BEditor.Views
{
    public static class AttachmentProperty
    {
        public static readonly DependencyProperty IntProperty = DependencyProperty.RegisterAttached("Int", typeof(int), typeof(AttachmentProperty), new PropertyMetadata(0));

        public static int GetInt(DependencyObject obj)
        {
            return (int)obj.GetValue(IntProperty);
        }
        public static void SetInt(DependencyObject obj, int value)
        {
            obj.SetValue(IntProperty, value);
        }
    }
}