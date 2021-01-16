using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.Primitive.Objects;
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

        public static ObservableCollection<ObjectMetadata> LoadedObjects { get; } = new()
        {
            ClipType.VideoMetadata,
            ClipType.ImageMetadata,
            ClipType.FigureMetadata,
            ClipType.PolygonMetadata,
            ClipType.RoundRectMetadata,
            ClipType.TextMetadata,
            ClipType.CameraMetadata,
            ClipType.GL3DObjectMetadata,
            ClipType.SceneMetadata
        };
    }
}
