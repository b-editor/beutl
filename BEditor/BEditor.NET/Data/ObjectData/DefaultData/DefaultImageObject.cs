using System.Runtime.Serialization;

using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public abstract class DefaultImageObject : Group {
            public abstract Media.Image Load(EffectLoadArgs args);
        }
    }
}
