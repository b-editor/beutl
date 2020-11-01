using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public abstract class DefaultImageObject : Group {
            public abstract Media.Image Load(EffectLoadArgs args);
        }
    }
}
