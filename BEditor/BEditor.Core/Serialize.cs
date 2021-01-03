using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Data.Primitive.Effects.PrimitiveImages;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Primitive.Properties.PrimitiveEasing;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Data.Primitive.Objects.PrimitiveImages;
using BEditor.Core.Data;
using System.Collections.ObjectModel;
using System.Xml;
using System.Diagnostics;

namespace BEditor.Core
{
    /// <summary>
    /// <see cref="DataContractJsonSerializer"/> を利用してシリアル化やクローンなどの関数を提供するクラスを表します
    /// </summary>
    public static class Serialize
    {
        /// <summary>
        /// オブジェクトの内容をファイルから読み込み復元します
        /// </summary>
        /// <param name="stream">読み込むストリーム</param>
        /// <param name="mode"></param>
        /// <returns>成功した場合は復元されたオブジェクト、そうでない場合は <see langword="null"/> を返します</returns>
        public static T LoadFromStream<T>(Stream stream, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                T obj;
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
        /// オブジェクトの内容をファイルから読み込み復元します
        /// </summary>
        /// <param name="path">読み込むファイル名</param>
        /// <param name="mode"></param>
        /// <returns>成功した場合は復元されたオブジェクト、そうでない場合は <see langword="null"/> を返します</returns>
        public static T LoadFromFile<T>(string path, SerializeMode mode = SerializeMode.Binary)
        {
            try
            {
                T obj;

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
        /// オブジェクトの内容をファイルに保存します
        /// </summary>
        /// <param name="obj">保存するオブジェクト</param>
        /// <param name="stream">保存先のストリーム</param>
        /// <param name="mode"></param>
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
        /// オブジェクトの内容をファイルに保存します
        /// </summary>
        /// <param name="obj">保存するオブジェクト</param>
        /// <param name="path">保存先のファイル名</param>
        /// <param name="mode"></param>
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
        /// DataContractを使用してDeepCloneを行います
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static T DeepClone<T>(this T obj)
        {

            var serializer = new DataContractJsonSerializer(obj.GetType(), SerializeKnownTypes);
            var ms = new MemoryStream();
            serializer.WriteObject(ms, obj);
            var json = Encoding.UTF8.GetString(ms.ToArray());

            // テキストからデシリアライズする
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var ms2 = new MemoryStream();
            ms2.Write(bytes, 0, bytes.Length);

            // デシリアライズを実行する
            ms2.Position = 0;
            T result = (T)serializer.ReadObject(ms2);

            ms.Dispose();
            ms2.Dispose();

            return result;
        }

        /// <summary>
        /// <see cref="DataContractJsonSerializer"/> で使用するKnownTypeを取得します
        /// </summary>
        public static List<Type> SerializeKnownTypes = new List<Type>()
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
            typeof(Image),
            typeof(Text),
            typeof(Video),
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

            typeof(DefaultEasing),
            typeof(EasingFunc)
        };
    }

    public enum SerializeMode
    {
        Binary,
        Json
    }
}
