using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Extensions.Object.Datas
{
    public class DataFactory
    {
        private static readonly List<Type> KnownType = new()
        {
            typeof(AudioFile),
            typeof(Figure),
            typeof(StandardDraw),
            typeof(StandardPlay),
            typeof(Text),
            typeof(VideoFile),
        };

        public static IExEffect RawToObject(Exobject exobject, RawExeffect raw)
        {
            var name = raw.Name;
            var type = KnownType.Find(i => Attribute.GetCustomAttribute(i, typeof(NamedAttribute)) is NamedAttribute attribute && attribute.Name == name);

            if (type is null) throw new Exception();

            var effect = (Activator.CreateInstance(type, exobject) as IExEffect)!;

            return effect;
        }
    }
}
