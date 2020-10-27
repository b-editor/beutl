using System.Runtime.Serialization;

using BEditorCore.Data.ProjectData;
using BEditorCore.Data.PropertyData;
using BEditorCore.Media;

namespace BEditorCore.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public abstract class DefaultImageObject : Group {
            public abstract Media.Image Load(EffectLoadArgs args);
        }
    }
}
