using System;
using System.Reflection;

namespace BEditor.Core.Data.PropertyData {
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class PropertyMetadataAttribute : Attribute {
        public PropertyElementMetadata PropertyMetadata { get; }

        public PropertyMetadataAttribute(string Fieldpath, Type Type) {
            var info = Type.GetField(Fieldpath, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (info != null && info.IsStatic) {
                PropertyMetadata = info.GetValue(null) as PropertyElementMetadata;
            }
        }
    }
}
