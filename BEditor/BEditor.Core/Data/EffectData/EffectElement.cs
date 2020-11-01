using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData.DefaultCommon;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.EffectData {

    /// <summary>
    /// エフェクトのベースクラス
    ///     <list type="bullet">
    ///         <item>
    ///             <term>Name</term>
    ///             <description>エフェクトの名前</description>
    ///         </item>
    ///         <item>
    ///             <term>IsEnabled</term>
    ///             <description>エフェクトが有効かのブーリアン</description>
    ///         </item>
    ///         <item>
    ///             <term>IsExpanded</term>
    ///             <description>GUIのExpanderが開いているかのブーリアン</description>
    ///         </item>
    ///         <item>
    ///             <term>GetControl</term>
    ///             <description>プロパティコントロールを取得</description>
    ///         </item>
    ///         <item>
    ///             <term>GetKeyframe</term>
    ///             <description>キーフレームコントロールを取得</description>
    ///         </item>
    ///         <item>
    ///             <term>ClipData</term>
    ///             <description>属するクリップのインスタンスの参照</description>
    ///         </item>
    ///         <item>
    ///             <term>Scene</term>
    ///             <description>属するシーン</description>
    ///         </item>
    ///         <item>
    ///             <term>PropertySettings</term>
    ///             <description>表示するプロパティ</description>
    ///         </item>
    ///     </list>
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class EffectElement : ComponentObject {
        private bool isEnabled = true;
        private bool isExpanded = true;
        private ClipData clipData;


        /// <summary>
        /// エフェクトの名前
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// エフェクトが有効か
        /// </summary>
        [DataMember]
        public bool IsEnabled { get => isEnabled; set => SetValue(value, ref isEnabled, nameof(IsEnabled)); }

        /// <summary>
        /// エクスパンダーが開いているか
        /// </summary>
        [DataMember]
        public bool IsExpanded { get => isExpanded; set => SetValue(value, ref isExpanded, nameof(IsExpanded)); }

        #region ClipData
        /// <summary>
        /// クリップのデータ
        /// </summary>
        public virtual ClipData ClipData {
            get => clipData;
            set {
                clipData = value;

                Parallel.For(0, PropertySettings.Count, i => {
                    PropertySettings[i].Parent = this;
                });
            }
        }
        #endregion

        /// <summary>
        /// 
        /// </summary>
        public virtual void PropertyLoaded() {
            Parallel.For(0, PropertySettings.Count, i => {
                PropertySettings[i].PropertyLoaded();
            });

            //フィールドがpublicのときだけなので注意
            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            void For1(int i) {
                var property = properties[i];

                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            }
            Parallel.For(0, properties.Length, For1);
        }


        /// <summary>
        /// プロパティのデータをList PropertySetting 型にして渡す
        /// </summary>
        public abstract IList<PropertyElement> PropertySettings { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        public abstract void Load(EffectLoadArgs args);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        public virtual void PreviewLoad(EffectLoadArgs args) { }


        #region Check
        /// <summary>
        /// 
        /// </summary>
        public class CheckEffect : IUndoRedoCommand {
            private readonly EffectElement effect;
            private readonly bool value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="effect"></param>
            /// <param name="value"></param>
            public CheckEffect(EffectElement effect, bool value) {
                this.effect = effect;
                this.value = value;
            }

            /// <inheritdoc/>
            public void Do() => effect.IsEnabled = value;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => effect.IsEnabled = !value;
        }
        #endregion

        #region Up

        /// <summary>
        /// 
        /// </summary>
        public class UpEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="data"></param>
            /// <param name="effect"></param>
            public UpEffect(ClipData data, EffectElement effect) {
                this.data = data;
                this.effect = effect;
            }


            #region Do
            /// <inheritdoc/>
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1) {
                    data.Effect.Move(index, index - 1);
                }
            }
            #endregion

            /// <inheritdoc/>
            public void Redo() => Do();

            #region Undo
            /// <inheritdoc/>
            public void Undo() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1) {
                    data.Effect.Move(index, index + 1);
                }
            }
            #endregion
        }
        #endregion

        #region Down

        /// <summary>
        /// 
        /// </summary>
        public class DownEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="data"></param>
            /// <param name="effect"></param>
            public DownEffect(ClipData data, EffectElement effect) {
                this.data = data;
                this.effect = effect;
            }

            #region Do
            /// <inheritdoc/>
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1) {
                    data.Effect.Move(index, index + 1);
                }
            }
            #endregion

            /// <inheritdoc/>
            public void Redo() => Do();

            #region Undo
            /// <inheritdoc/>
            public void Undo() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1) {
                    data.Effect.Move(index, index - 1);
                }
            }
            #endregion
        }
        #endregion

        #region Delete
        /// <summary>
        /// 
        /// </summary>
        public class DeleteEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;
            private readonly int index;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="data"></param>
            /// <param name="effect"></param>
            public DeleteEffect(ClipData data, EffectElement effect) {
                this.data = data;
                this.effect = effect;
                index = data.Effect.IndexOf(effect);
            }

            /// <inheritdoc/>
            public void Do() => data.Effect.RemoveAt(index);

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => data.Effect.Insert(index, effect);
        }
        #endregion

        #region Add

        /// <summary>
        /// 
        /// </summary>
        public class AddEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="data"></param>
            /// <param name="effect"></param>
            public AddEffect(ClipData data, EffectElement effect) {
                this.data = data;
                this.effect = effect;

                effect.ClipData = data;
                effect.PropertyLoaded();
            }


            /// <inheritdoc/>
            public void Do() => data.Effect.Add(effect);

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => data.Effect.Remove(effect);
        }

        #endregion
    }

    public class EffectData {
        public string Name { get; set; }
        public Type Type { get; set; }
        public List<EffectData> Children { get; set; }

        public static ObservableCollection<EffectData> LoadedEffects { get; } = new ObservableCollection<EffectData> {
            new() {
                Name = Resources.Effects,
                Children = new() {
                    new() { Name = Resources.Border, Type = typeof(Border) },
                    new() { Name = Resources.ColorKey, Type = typeof(ColorKey) },
                    new() { Name = Resources.DropShadow, Type = typeof(Shadow) },
                    new() { Name = Resources.Blur, Type = typeof(Blur) },
                    new() { Name = Resources.Monoc, Type = typeof(Monoc) },
                    new() { Name = Resources.Dilate, Type = typeof(Dilate) },
                    new() { Name = Resources.Erode, Type = typeof(Erode) },
                    new() { Name = Resources.Clipping, Type = typeof(Clipping) },
                    new() { Name = Resources.AreaExpansion, Type = typeof(AreaExpansion) }
                }
            },
            new() {
                Name = Resources.Camera,
                Children = new() {
                    new() { Name = Resources.DepthTest, Type = typeof(DepthTest) },
                    new() { Name = Resources.DirectionalLightSource, Type = typeof(DirectionalLightSource) },
                    new() { Name = Resources.PointLightSource, Type = typeof(PointLightSource) },
                    new() { Name = Resources.SpotLight, Type = typeof(SpotLight) }
                }
            }
        };
    }
}
