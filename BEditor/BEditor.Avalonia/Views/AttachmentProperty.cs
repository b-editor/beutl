using System;
using System.Collections.Generic;
using System.Text;

using Avalonia;
using Avalonia.Controls;

namespace BEditor.Views
{
    public class AttachmentProperty
    {
        public static readonly AttachedProperty<int> IntProperty = AvaloniaProperty.RegisterAttached<AttachmentProperty, Control, int>("Int");

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
