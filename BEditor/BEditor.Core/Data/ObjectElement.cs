using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Objects.PrimitiveImages;
using BEditor.Core.Properties;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the base class of the object.
    /// </summary>
    [DataContract]
    public abstract class ObjectElement : EffectElement
    {
        public virtual bool EffectFilter(EffectElement effect) => true;
    }

    public class ObjectMetadata
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public Func<EffectElement> CreateFunc { get; set; }
        public List<ObjectMetadata> Children { get; set; }

        public static List<ObjectMetadata> LoadedObjects { get; } = new() {
            new()
            {
                Name = Resources.Video,
                Type = typeof(Video),
                CreateFunc = () => new Video()
            },
            new()
            {
                Name = Resources.Image,
                Type = typeof(Image),
                CreateFunc = () => new Image()
            },
            new()
            {
                Name = Resources.Figure,
                Type = typeof(Figure),
                CreateFunc = () => new Figure()
            },
            new()
            {
                Name = Resources.Text,
                Type = typeof(Text),
                CreateFunc = () => new Text()
            },
            new()
            {
                Name = Resources.Camera,
                Type = typeof(CameraObject),
                CreateFunc = () => new CameraObject()
            },
            new()
            {
                Name = Resources._3DObject,
                Type = typeof(GL3DObject),
                CreateFunc = () => new GL3DObject()
            },
            new()
            {
                Name = Resources.Scenes,
                Type = typeof(SceneObject),
                CreateFunc = () => new SceneObject()
            }
        };
    }
}
