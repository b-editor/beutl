using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Contracts;
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
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class EffectElement : ComponentObject {
        private bool isEnabled = true;
        private bool isExpanded = true;
        private ClipData clipData;


        /// <summary>
        /// エフェクトの名前
        /// </summary>
        /// <remarks>このプロパティはシリアル化されません</remarks>
        public abstract string Name { get; }

        /// <summary>
        /// エフェクトが有効かのブーリアン
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember]
        public bool IsEnabled { get => isEnabled; set => SetValue(value, ref isEnabled, nameof(IsEnabled)); }

        /// <summary>
        /// エクスパンダーが開いているかのブーリアン
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember]
        public bool IsExpanded { get => isExpanded; set => SetValue(value, ref isExpanded, nameof(IsExpanded)); }

        #region ClipData
        /// <summary>
        /// クリップのデータ
        /// </summary>
        /// <remarks>このプロパティはシリアル化されません</remarks>
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
        /// コンストラクタの後やデシリアル化の後に呼び出されます
        /// </summary>
        /// <remarks>
        /// <para>通常は </para>
        /// <list type="bullet">
        /// <item>
        ///    <seealso cref="PropertySettings"/>内のアイテムのPropertyLoaded
        /// </item>
        /// <item>
        ///    リフレクションで<see cref="PropertyMetadataAttribute"/>がついているプロパティに<seealso cref="PropertyElementMetadata"/>をセットします
        /// </item>
        /// </list>
        /// </remarks>
        public virtual void PropertyLoaded() {
            Parallel.For(0, PropertySettings.Count, i => {
                PropertySettings[i].PropertyLoaded();
            });

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
        /// GUIに表示する<seealso cref="PropertyElement"/>を<see cref="IList{PropertyElement}"/>で渡してください
        /// </summary>
        public abstract IList<PropertyElement> PropertySettings { get; }

        /// <summary>
        /// フレーム描画時に呼び出されます
        /// </summary>
        /// <param name="args">呼び出しの順番などのデータ</param>
        public abstract void Load(EffectLoadArgs args);
        /// <summary>
        /// フレーム描画前に呼び出されます
        /// </summary>
        /// <param name="args">呼び出しの順番などのデータ</param>
        /// <remarks>ここでエフェクトの順番などを変更できます</remarks>
        public virtual void PreviewLoad(EffectLoadArgs args) { }


        #region Check
        /// <summary>
        /// エフェクトが有効かのブーリアンを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class CheckEffect : IUndoRedoCommand {
            private readonly EffectElement effect;
            private readonly bool value;

            /// <summary>
            /// <see cref="CheckEffect"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            /// <param name="value">セットする値</param>
            public CheckEffect(EffectElement effect, bool value) {
                this.effect = effect;
                this.value = value;
            }

            /// <inheritdoc/>
            [Pure]
            public void Do() => effect.IsEnabled = value;

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            /// <inheritdoc/>
            [Pure]
            public void Undo() => effect.IsEnabled = !value;
        }
        #endregion

        #region Up

        /// <summary>
        /// エフェクトの順番を上げるコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class UpEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="UpEffect"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            public UpEffect(EffectElement effect) {
                data = effect.ClipData;
                this.effect = effect;
            }


            #region Do
            /// <inheritdoc/>
            [Pure]
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1) {
                    data.Effect.Move(index, index - 1);
                }
            }
            #endregion

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            #region Undo
            /// <inheritdoc/>
            [Pure]
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
        /// エフェクトの順番を下げるコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class DownEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="DownEffect"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            public DownEffect(EffectElement effect) {
                data = effect.ClipData;
                this.effect = effect;
            }

            #region Do
            /// <inheritdoc/>
            [Pure]
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1) {
                    data.Effect.Move(index, index + 1);
                }
            }
            #endregion

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            #region Undo
            /// <inheritdoc/>
            [Pure]
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
        /// エフェクトを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class DeleteEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;
            private readonly int index;

            /// <summary>
            /// <see cref="DeleteEffect"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            public DeleteEffect(EffectElement effect) {
                this.data = effect.ClipData;
                this.effect = effect;
                index = data.Effect.IndexOf(effect);
            }

            /// <inheritdoc/>
            [Pure]
            public void Do() => data.Effect.RemoveAt(index);

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            /// <inheritdoc/>
            [Pure]
            public void Undo() => data.Effect.Insert(index, effect);
        }
        #endregion

        #region Add

        /// <summary>
        /// エフェクトを追加するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class AddEffect : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="AddEffect"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            /// <exception cref="ArgumentException">effect.ClipDataがnullです</exception>
            public AddEffect(EffectElement effect) {
                if (effect.ClipData is null) throw new ArgumentException("effect.ClipData is null", nameof(effect));

                this.data = effect.ClipData;
                this.effect = effect;

                effect.PropertyLoaded();
            }


            /// <inheritdoc/>
            [Pure]
            public void Do() => data.Effect.Add(effect);

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            /// <inheritdoc/>
            [Pure]
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
