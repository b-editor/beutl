using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Media;

namespace BEditor.Core.Data
{
    public static class ExtensionCommand
    {
        public static void Execute(this IRecordCommand command) => CommandManager.Do(command);

        public static void Load(this PropertyElement property, PropertyElementMetadata metadata)
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }
        public static void Load<T>(this PropertyElement<T> property, T metadata) where T : PropertyElementMetadata
        {
            property.Load();
            property.PropertyMetadata = metadata;
        }
    }
}
