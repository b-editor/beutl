using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data
{
    public static class DataExtension
    {
        public static CustomClipUIAttribute? GetAttribute<T>(this T self) where T : ObjectElement
        {
            var type = self.GetType();
            var attribute = Attribute.GetCustomAttribute(type, typeof(CustomClipUIAttribute));

            if (attribute is CustomClipUIAttribute uIAttribute) return uIAttribute;
            else return new();
        }
    }
}
