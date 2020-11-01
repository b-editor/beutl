using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.ObjectData {
    /// <summary>
    /// オブジェクトのベースクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class ObjectElement : EffectElement {

    }

    public class ObjectData {
        public string Name { get; set; }
        public Type Type { get; set; }
        public List<ObjectData> Children { get; set; }

        public static List<ObjectData> LoadedObjects { get; } = new List<ObjectData>() {
            new ObjectData() { Name = Resources.Video, Type = typeof(DefaultData.Video) },
            new ObjectData() { Name = Resources.Image, Type = typeof(DefaultData.Image) },
            new ObjectData() { Name = Resources.Figure, Type = typeof(DefaultData.Figure) },
            new ObjectData() { Name = Resources.Text, Type = typeof(DefaultData.Text) },
            new ObjectData() { Name = Resources.Camera, Type = typeof(CameraObject) },
            new ObjectData() { Name = Resources._3DObject, Type = typeof(GL3DObject) },
            new ObjectData() { Name = Resources.Scenes, Type = typeof(DefaultData.Scene) }
        };
    }
}
