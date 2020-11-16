using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

using BEditor.ObjectModel.EffectData;
using BEditor.ObjectModel.EffectData.DefaultCommon;
using BEditor.ObjectModel.ObjectData;
using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.ObjectModel.PropertyData.Default;
using BEditor.ObjectModel.PropertyData.EasingSetting;
using BEditor.Core.Plugin;

namespace BEditor.ObjectModel
{
    /// <summary>
    /// <see cref="DataContractJsonSerializer"/> を利用してシリアル化やクローンなどの関数を提供するクラスを表します
    /// </summary>
    internal static class Serialize
    {
        internal static T LoadFromFile<T>(string path)
        {
            try
            {
                object obj;

                using (FileStream file = new FileStream(path, FileMode.Open))
                {
                    var serializer = new DataContractJsonSerializer(typeof(T), SerializeKnownTypes);
                    obj = serializer.ReadObject(file);
                }

                return (T)obj;
            }
            catch
            {
                return default;
            }
        }
        internal static object LoadFromFile(string path, Type type)
        {
            try
            {
                object obj;

                using (FileStream file = new FileStream(path, FileMode.Open))
                {
                    var serializer = new DataContractJsonSerializer(type, SerializeKnownTypes);
                    obj = serializer.ReadObject(file);
                }

                return obj;
            }
            catch
            {
                return null;
            }
        }
        internal static bool SaveToFile(object obj, string path)
        {
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Create))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(file, Encoding.UTF8, true, true, "  "))
                {
                    var serializer = new DataContractJsonSerializer(obj.GetType(), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        internal static T DeepClone<T>(this T obj)
        {

            var serializer = new DataContractJsonSerializer(obj.GetType(), SerializeKnownTypes);
            var ms = new MemoryStream();
            serializer.WriteObject(ms, obj);
            var json = Encoding.UTF8.GetString(ms.ToArray());

            // テキストからデシリアライズする
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            var ms2 = new MemoryStream();
            ms2.Write(bytes, 0, bytes.Length);

            //デシリアライズを実行する
            ms2.Position = 0;
            T result = (T)serializer.ReadObject(ms2);

            ms.Dispose();
            ms2.Dispose();

            return result;
        }

        internal static List<Type> SerializeKnownTypes = new List<Type>()
                    {
                        typeof(Project),
                        typeof(Scene),
                        typeof(RootScene),

                        typeof(ClipData),
                        typeof(ImageObject),
                        typeof(CameraObject),
                        typeof(GL3DObject),
                        typeof(ObjectElement),

                        typeof(Angle),
                        typeof(Blend),
                        typeof(Coordinate),
                        typeof(Zoom),
                        typeof(Material),

                        typeof(DefaultData.DefaultImageObject),
                        typeof(DefaultData.Figure),
                        typeof(DefaultData.Image),
                        typeof(DefaultData.Text),
                        typeof(DefaultData.Video),
                        typeof(DefaultData.Scene),

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
}
