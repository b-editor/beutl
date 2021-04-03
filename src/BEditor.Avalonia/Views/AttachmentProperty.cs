using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

using Avalonia;
using Avalonia.Controls;

namespace BEditor.Views
{
    public class AttachmentProperty : AvaloniaObject
    {
        public static readonly AttachedProperty<int> IntProperty = AvaloniaProperty.RegisterAttached<AttachmentProperty, AvaloniaObject, int>("Int", 0);
    }
}
