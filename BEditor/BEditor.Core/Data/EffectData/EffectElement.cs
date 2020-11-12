using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

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
        /// エフェクトの名前を取得します
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// エフェクトが有効かを取得または設定します
        /// </summary>
        /// <remarks>エフェクトが有効な場合 <see langword="true"/> そうでない場合は <see langword="false"/> となります</remarks>
        [DataMember]
        public bool IsEnabled { get => isEnabled; set => SetValue(value, ref isEnabled, nameof(IsEnabled)); }

        /// <summary>
        /// エクスパンダーが開いているかを取得または設定します
        /// </summary>
        /// <remarks>エクスパンダーが開いている場合は <see langword="true"/>、そうでない場合は <see langword="false"/> となります</remarks>
        [DataMember]
        public bool IsExpanded { get => isExpanded; set => SetValue(value, ref isExpanded, nameof(IsExpanded)); }

        #region ClipData
        /// <summary>
        /// <see cref="ObjectData.ClipData"/> を取得します
        /// </summary>
        public virtual ClipData ClipData {
            get => clipData;
            internal set {
                clipData = value;

                PropertySettings.AsParallel().ForAll(property => property.Parent = this);
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
            var settings = PropertySettings;

            settings.AsParallel().ForAll(p => p.PropertyLoaded());
            

            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            properties.AsParallel().ForAll(property => {
                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement) {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
        }


        /// <summary>
        /// GUIに表示する<seealso cref="PropertyElement"/>を<see cref="IList{PropertyElement}"/>を取得します
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
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class CheckCommand : IUndoRedoCommand {
            private readonly EffectElement effect;
            private readonly bool value;

            /// <summary>
            /// <see cref="CheckCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象の <see cref="EffectElement"/></param>
            /// <param name="value">セットする値</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public CheckCommand(EffectElement effect, bool value) {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
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
        /// エフェクトの順番を上げるコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class UpCommand : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="UpCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象の <see cref="EffectElement"/></param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public UpCommand(EffectElement effect) {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                data = effect.ClipData;
            }


            /// <inheritdoc/>
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1) {
                    data.Effect.Move(index, index - 1);
                }
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1) {
                    data.Effect.Move(index, index + 1);
                }
            }
        }
        #endregion

        #region Down

        /// <summary>
        /// エフェクトの順番を下げるコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class DownCommand : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="DownCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象の <see cref="EffectElement"/></param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public DownCommand(EffectElement effect) {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                data = effect.ClipData;
            }


            /// <inheritdoc/>
            public void Do() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1) {
                    data.Effect.Move(index, index + 1);
                }
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1) {
                    data.Effect.Move(index, index - 1);
                }
            }
        }
        #endregion

        #region Remove
        /// <summary>
        /// エフェクトを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public class RemoveCommand : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;
            private readonly int index;

            /// <summary>
            /// <see cref="RemoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象の <see cref="EffectElement"/></param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public RemoveCommand(EffectElement effect) {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                this.data = effect.ClipData;
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
        /// エフェクトを追加するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public class AddCommand : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="AddCommand"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象のエフェクト</param>
            /// <exception cref="ArgumentException">effect.ClipDataがnullです</exception>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public AddCommand(EffectElement effect) {
                if (effect.ClipData is null) throw new ArgumentException("effect.ClipData is null", nameof(effect));
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));

                this.data = effect.ClipData;

                effect.PropertyLoaded();
            }
            /// <summary>
            /// <see cref="AddCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="effect">対象の <see cref="EffectElement"/></param>
            /// <param name="clip"></param>
            /// <exception cref="ArgumentException">effect.ClipDataが <see langword="null"/> です</exception>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> が <see langword="null"/> です</exception>
            public AddCommand(EffectElement effect, ClipData clip) {
                if (clip is null) throw new ArgumentNullException(nameof(clip));

                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                this.data = clip;
                effect.ClipData = clip;

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
