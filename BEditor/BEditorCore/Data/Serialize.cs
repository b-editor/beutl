using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

using BEditorCore.Data;
using BEditorCore.Data.EffectData;
using BEditorCore.Data.EffectData.DefaultCommon;
using BEditorCore.Data.ObjectData;
using BEditorCore.Data.ProjectData;
using BEditorCore.Data.PropertyData;
using BEditorCore.Data.PropertyData.Default;
using BEditorCore.Data.PropertyData.EasingSetting;
using BEditorCore.Plugin;

namespace BEditorCore.Data {

    public static class Serialize {

        /// <summary>
        /// オブジェクトの内容をファイルから読み込み復元する
        /// </summary>
        /// <param name="path">読み込むファイル名</param>
        /// <param name="type"></param>
        /// <returns>復元されたオブジェクト</returns>
        public static object LoadFromFile(string path, Type type) {
            try {
                object obj;

                using (FileStream file = new FileStream(path, FileMode.Open)) {
                    var serializer = new DataContractJsonSerializer(type, SerializeKnownTypes);
                    obj = serializer.ReadObject(file);
                }

                return obj;
            }
            catch (Exception e) {
                return null;
            }
        }

        /// <summary>
        /// オブジェクトの内容をファイルに保存する
        /// </summary>
        /// <param name="obj">保存するオブジェクト</param>
        /// <param name="path">保存先のファイル名</param>
        public static bool SaveToFile(object obj, string path) {
            try {
                using (FileStream file = new FileStream(path, FileMode.Create))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(file, Encoding.UTF8, true, true, "  ")) {
                    var serializer = new DataContractJsonSerializer(obj.GetType(), SerializeKnownTypes);
                    serializer.WriteObject(writer, obj);
                }

                return true;
            }
            catch (Exception e) {
                return false;
            }
        }

        /// <summary>
        /// DataContractを使用したDeepClone
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static T DeepClone<T>(this T obj) {

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

        public static List<Type> SerializeKnownTypes {
            get {
                if (serializeKnownTypes == null) {
                    serializeKnownTypes = new List<Type>()
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

                    foreach (var type in Component.Current.LoadedPlugins) {
                        if (type is IEffects effectsPlugin) {
                            foreach (var (_, effecttype) in effectsPlugin.Effects) {
                                serializeKnownTypes.Add(effecttype);
                            }
                        }

                        if (type is IObjects objectsPlugin) {
                            foreach (var (_, objecttype) in objectsPlugin.Objects) {
                                serializeKnownTypes.Add(objecttype);
                            }
                        }

                        if (type is IEasingFunctions funcsPlugin) {
                            foreach (var (_, functype) in funcsPlugin.EasingFunc) {
                                serializeKnownTypes.Add(functype);
                            }
                        }
                    }
                }

                return serializeKnownTypes;
            }
        }

        private static List<Type> serializeKnownTypes;
    }
}
