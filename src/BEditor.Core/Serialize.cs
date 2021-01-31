using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.PrimitiveGroup;
using BEditor.Core.Data;
using System.Collections.ObjectModel;
using System.Xml;
using System.Diagnostics;
using BEditor.Core.Data.Property.Easing;
using BEditor.Core.Data.Primitive;

namespace BEditor.Core
{
    /// <summary>
    /// Represents a class that uses the <see cref="DataContractJsonSerializer"/> to provide methods for serialization, cloning, etc.
    /// </summary>
    public static class Serialize
    {
        /// <summary>
        /// Reads and restores the contents of an object from a stream
        /// </summary>
        /// <param name="stream">Stream to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromStream<T>(Stream stream, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                T? obj;
                stream.Position = 0;

                if (mode == SerializeMode.Binary)
                {
                    using var reader = XmlDictionaryReader.CreateBinaryReader(stream, new XmlDictionaryReaderQuotas());
                    var serializer = new DataContractSerializer(typeof(T), SerializeKnownTypes);
                    obj = (T?)serializer.ReadObject(reader);
                }
                else
                {
                    var serializer = new DataContractJsonSerializer(typeof(T), SerializeKnownTypes);
                    obj = (T?)serializer.ReadObject(stream);
                }

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }

        /// <summary>
        /// Reads and restores the contents of an object from a file.
        /// </summary>
        /// <param name="path">The name of the file to load.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        /// <returns>Returns the restored object on success, <see langword="null"/> otherwise.</returns>
        public static T? LoadFromFile<T>(string path, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                T? obj;

                using (FileStream file = new FileStream(path, FileMode.Open))
                {
                    if (mode == SerializeMode.Binary)
                    {
                        using var reader = XmlDictionaryReader.CreateBinaryReader(file, new XmlDictionaryReaderQuotas());
                        var serializer = new DataContractSerializer(typeof(T), SerializeKnownTypes);
                        obj = (T?)serializer.ReadObject(reader);
                    }
                    else
                    {
                        var serializer = new DataContractJsonSerializer(typeof(T), SerializeKnownTypes);
                        obj = (T?)serializer.ReadObject(file);
                    }
                }

                return obj;
            }
            catch
            {
                Debug.Assert(false);
                return default;
            }
        }

        /// <summary>
        /// Save the contents of an object to a stream.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="stream">The stream to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static bool SaveToStream<T>(T obj, Stream stream, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                stream.Position = 0;
             
                if (mode == SerializeMode.Binary)
                {
                    using var writer = XmlDictionaryWriter.CreateBinaryWriter(stream);

                    var serializer = new DataContractSerializer(typeof(T), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }
                else
                {
                    using var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, true, true, "  ");

                    var serializer = new DataContractJsonSerializer(typeof(T), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }

        /// <summary>
        /// Saves the contents of an object to a file.
        /// </summary>
        /// <param name="obj">The object to save.</param>
        /// <param name="path">The name of the file to save to.</param>
        /// <param name="mode">This is the mode of serialization.</param>
        public static bool SaveToFile<T>(T obj, string path, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Create);
                if (mode == SerializeMode.Binary)
                {
                    using var writer = XmlDictionaryWriter.CreateBinaryWriter(file);

                    var serializer = new DataContractSerializer(typeof(T), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }
                else
                {
                    using var writer = JsonReaderWriterFactory.CreateJsonWriter(file, Encoding.UTF8, true, true, "  ");

                    var serializer = new DataContractJsonSerializer(typeof(T), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }

                return true;
            }
            catch
            {
                Debug.Assert(false);
                return false;
            }
        }

        /// <summary>
        /// DeepClone using <see cref="DataContractSerializer"/>.
        /// </summary>
        public static T? DeepClone<T>(this T obj)
        {
            var serializer = new DataContractJsonSerializer(obj!.GetType(), SerializeKnownTypes);
            var ms = new MemoryStream();
            serializer.WriteObject(ms, obj);
            var json = Encoding.UTF8.GetString(ms.ToArray());

            // テキストからデシリアライズする
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var ms2 = new MemoryStream();
            ms2.Write(bytes, 0, bytes.Length);

            // デシリアライズを実行する
            ms2.Position = 0;
            var result = (T?)serializer.ReadObject(ms2);

            ms.Dispose();
            ms2.Dispose();

            return result;
        }

        /// <summary>
        /// Get the KnownType used by the <see cref="DataContractJsonSerializer"/>.
        /// </summary>
        public static readonly List<Type> SerializeKnownTypes = new List<Type>()
        {
            typeof(BasePropertyChanged),
            typeof(ComponentObject),

            typeof(Project),
            typeof(RootScene),
            typeof(Scene),

            typeof(ClipData),
            typeof(ImageObject),
            typeof(AudioObject),
            typeof(CameraObject),
            typeof(GL3DObject),
            typeof(ObjectElement),

            typeof(Angle),
            typeof(Blend),
            typeof(Coordinate),
            typeof(Zoom),
            typeof(Material),

            typeof(Figure),
            typeof(ImageFile),
            typeof(Text),
            typeof(VideoFile),
            typeof(SceneObject),

            typeof(Blur),
            typeof(Border),
            typeof(ColorKey),
            typeof(Dilate),
            typeof(EffectElement),
            typeof(Erode),
            typeof(ImageEffect),
            typeof(Monoc),
            typeof(Shadow),
            typeof(Clipping),
            typeof(AreaExpansion),
            typeof(LinearGradient),
            typeof(CircularGradient),
            typeof(Mask),
            typeof(PointLightDiffuse),
            typeof(ChromaKey),
            typeof(ImageSplit),
            typeof(MultipleControls),

            typeof(DepthTest),
            typeof(DirectionalLightSource),
            typeof(PointLightSource),
            typeof(SpotLight),

            typeof(CheckProperty),
            typeof(ColorProperty),
            typeof(DocumentProperty),
            typeof(EaseProperty),
            typeof(FileProperty),
            typeof(FontProperty),
            typeof(Group),
            typeof(PropertyElement),
            typeof(SelectorProperty),
            typeof(ExpandGroup),

            typeof(PrimitiveEasing),
            typeof(EasingFunc)
        };
    }

    /// <summary>
    /// Represents the mode of serialization.
    /// </summary>
    public enum SerializeMode
    {
        /// <summary>
        /// The binary.
        /// </summary>
        Binary,
        /// <summary>
        /// The json.
        /// </summary>
        Json
    }
}
