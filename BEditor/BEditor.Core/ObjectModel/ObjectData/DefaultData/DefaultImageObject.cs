using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;

namespace BEditor.ObjectModel.ObjectData
{
    public static partial class DefaultData
    {
        [DataContract(Namespace = "")]
        public abstract class DefaultImageObject : Group
        {
            public abstract Media.Image Render(EffectRenderArgs args);
        }
    }
}
