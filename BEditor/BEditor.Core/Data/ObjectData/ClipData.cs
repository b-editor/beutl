using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Interfaces;

namespace BEditor.Core.Data.ObjectData {
    /// <summary>
    /// タイムラインに配置されるクリップのデータ
    /// </summary>
    [DataContract(Namespace = "", Name = "Data")]
    public class ClipData : ComponentObject, ICloneable {

        #region ClipDataフィールド

        private string name;
        private int start;
        private int end;
        private int layer;
        private string labeltext;
        private ObservableCollection<EffectElement> effect;

        #endregion

        /// <summary>
        /// <see cref="ClipData"/>クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="id">Sceneから取得できるId</param>
        /// <param name="effects">エフェクトのリスト</param>
        /// <param name="start">開始位置</param>
        /// <param name="end">終了位置</param>
        /// <param name="type">クリップの種類</param>
        /// <param name="layer">配置されるレイヤー</param>
        public ClipData(uint id, ObservableCollection<EffectElement> effects, int start, int end, Type type, int layer) {
            Id = id;
            this.start = start;
            this.end = end;
            Type = type;
            this.layer = layer;
            Effect = effects;
            LabelText = Name;
        }

        /// <summary>
        /// クリップのID
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Id", Order = 0)]
        public uint Id { get; set; }

        /// <summary>
        /// オブジェクトの名前
        /// </summary>
        /// <remarks>このプロパティはシリアル化されません</remarks>
        public string Name {
            get {
                if (name == null) {
                    name = $"{Type.Name}{Id}";
                }

                return name;
            }
            set => name = value;
        }


        /// <summary>
        /// オブジェクトの種類
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Type", Order = 1)]
        public string ClipType { get => Type.FullName; set => Type = Type.GetType(value); }

        public Type Type { get; set; }

        #region Start
        /// <summary>
        /// オブジェクトの開始フレーム
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Start", Order = 2)]
        public int Start { get => start; set => SetValue(value, ref start, nameof(Start)); }
        #endregion

        #region End
        /// <summary>
        /// オブジェクトの終了フレーム
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "End", Order = 3)]
        public int End { get => end; set => SetValue(value, ref end, nameof(End)); }
        #endregion

        #region Length
        /// <summary>
        /// オブジェクトの長さ
        /// </summary>
        /// <remarks>このプロパティはシリアル化されません</remarks>
        public int Length => End - Start;
        #endregion

        #region Layer
        /// <summary>
        /// オブジェクトの配置レイヤー
        /// 1から
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Layer", Order = 4)]
        public int Layer {
            get => layer;
            set {
                if (value == 0) {
                    return;
                }

                SetValue(value, ref layer, nameof(Layer));
            }
        }
        #endregion

        #region ClipのText

        /// <summary>
        /// クリップに表示されるテキスト
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Text", Order = 5)]
        public string LabelText { get => labeltext; set => SetValue(value, ref labeltext, nameof(LabelText)); }
        #endregion

        #region Sceneのインスタンス
        /// <summary>
        /// <see cref="ProjectData.Scene"/>のインスタンス
        /// </summary>
        /// <remarks>このプロパティはシリアル化されません</remarks>
        public Scene Scene { get; set; }
        #endregion

        
        /// <summary>
        /// クリップのエフェクト
        /// </summary>
        /// <remarks>このプロパティは <see cref="DataMemberAttribute"/> です</remarks>
        [DataMember(Name = "Effects", Order = 6)]
        public ObservableCollection<EffectElement> Effect {
            get => effect;
            set {
                effect = value;

                Parallel.For(0, Effect.Count, i => {
                    Effect[i].ClipData = this;
                    Effect[i].PropertyLoaded();
                });
            }
        }

        #region Load


        /// <summary>
        /// フレーム描画時に呼び出されます
        /// </summary>
        public void Load(ObjectLoadArgs args) {
            var loadargs = new EffectLoadArgs(args.Frame, Effect.Where(x => x.IsEnabled).ToList());
            if (Effect[0] is ObjectElement obj) {
                if (!obj.IsEnabled) {
                    return;
                }

                obj.Load(loadargs);
            }
        }

        /// <summary>
        /// フレーム描画前に呼び出されます
        /// </summary>
        public void PreviewLoad(ObjectLoadArgs args) {
            var loadargs = new EffectLoadArgs(args.Frame, Effect.Where(x => x.IsEnabled).ToList());

            foreach (var item in loadargs.Schedules) {
                item.PreviewLoad(loadargs);
            }
        }
        #endregion

        #region MoveTime
        internal void MoveFrame(int f) {
            Start += f;
            End += f;
        }

        internal void MoveTo(int start) {
            var length = Length;
            Start = start;
            End = length + start;
        }
        #endregion

        /// <inheritdoc/>
        public object Clone() => this.DeepClone();

        #region Commands

        #region Add
        /// <summary>
        /// クリップをシーンに追加するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class Add : IUndoRedoCommand {
            private readonly Scene Scene;
            private readonly int AddFrame;
            private readonly int AddLayer;
            private readonly Type Type;
            public ClipData data;

            /// <summary>
            /// <see cref="Add"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="scene">対象のシーン</param>
            /// <param name="addframe">配置するフレーム</param>
            /// <param name="layer">配置するレイヤー</param>
            /// <param name="_Type">クリップの種類</param>
            public Add(Scene scene, int addframe, int layer, Type _Type) {
                Scene = scene;
                AddFrame = addframe;
                AddLayer = layer;
                Type = _Type;
            }


            /// <inheritdoc/>
            public void Do() {
                //新しいidを取得
                uint idmax = Scene.NewId;

                //描画情報
                ObservableCollection<EffectElement> list = new();


                EffectElement index0;
                if (Type.IsSubclassOf(typeof(DefaultData.DefaultImageObject))) {
                    DefaultData.DefaultImageObject _Custom_info = (DefaultData.DefaultImageObject)Activator.CreateInstance(Type);

                    index0 = new ImageObject() { Custom = _Custom_info };
                }
                else if (Type.IsSubclassOf(typeof(ObjectElement))) {
                    index0 = (ObjectElement)Activator.CreateInstance(Type);
                }
                else {
                    throw new Exception();
                }

                list.Add(index0);

                //オブジェクトの情報
                data = new ClipData(idmax, list, AddFrame, AddFrame + 180, Type, AddLayer);

                index0.ClipData = data;

                Scene.Add(data);

                Scene.SetCurrentClip(data);
            }

            /// <inheritdoc/>
            public void Redo() {
                Scene.Add(data);

                Scene.SetCurrentClip(data);
            }

            /// <inheritdoc/>
            public void Undo() {
                Scene.Remove(data);

                //存在する場合
                if (Scene.SelectNames.Exists(x => x == data.Name)) {
                    Scene.SelectItems.Remove(data);

                    if (Scene.SelectName == data.Name) {
                        Scene.SelectItem = null;
                    }
                }
            }
        }

        #endregion


        #region Remove
        /// <summary>
        /// シーンからクリップを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class Remove : IUndoRedoCommand {
            private readonly ClipData data;

            /// <summary>
            /// <see cref="Remove"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="data">対象のクリップ</param>
            public Remove(ClipData data) => this.data = data;


            /// <inheritdoc/>
            public void Do() {
                if (!data.Scene.Remove(data)) {
                    //Message.Snackbar("削除できませんでした");
                }
                else {
                    //存在する場合
                    if (data.Scene.SelectNames.Exists(x => x == data.Name)) {
                        data.Scene.SelectItems.Remove(data);

                        if (data.Scene.SelectName == data.Name) {
                            if (data.Scene.SelectItems.Count == 0) {
                                data.Scene.SelectItem = null;
                            }
                            else {
                                data.Scene.SelectItem = data.Scene.SelectItems[0];
                            }
                        }
                    }
                }
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => data.Scene.Add(data);
        }
        #endregion


        #region Move
        /// <summary>
        /// クリップのフレームとレイヤーを移動するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class Move : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly int to;
            private readonly int from;
            private readonly int tolayer;
            private readonly int fromlayer;
            private Scene Scene => data.Scene;

            #region コンストラクタ
            /// <summary>
            /// <see cref="Move"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="data">対象のクリップ</param>
            /// <param name="to">新しい開始フレーム</param>
            /// <param name="tolayer">新しい配置レイヤー</param>
            public Move(ClipData data, int to, int tolayer) {
                this.data = data;
                this.to = to;
                from = data.Start;
                this.tolayer = tolayer;
                fromlayer = data.Layer;
            }

            /// <summary>
            /// <see cref="Move"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="data">対象のクリップ</param>
            /// <param name="to">新しい開始フレーム</param>
            /// <param name="from">古い開始フレーム</param>
            /// <param name="tolayer">新しい配置レイヤー</param>
            /// <param name="fromlayer">古い配置レイヤー</param>
            public Move(ClipData data, int to, int from, int tolayer, int fromlayer) {
                this.data = data;
                this.to = to;
                this.from = from;
                this.tolayer = tolayer;
                this.fromlayer = fromlayer;
            }
            #endregion


            /// <inheritdoc/>
            public void Do() {
                data.MoveTo(to);

                data.Layer = tolayer;


                if (data.End > Scene.TotalFrame) {
                    Scene.TotalFrame = data.End;
                }
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                data.MoveTo(from);

                data.Layer = fromlayer;
            }
        }

        #endregion


        #region LengthChange
        /// <summary>
        /// クリップの長さを変更するコマンド
        /// </summary>
        /// <remarks>このクラスは<see cref="UndoRedoManager.Do(IUndoRedoCommand)"/>と併用することでコマンドを記録できます</remarks>
        public class LengthChange : IUndoRedoCommand {
            private readonly ClipData data;
            private readonly int start;
            private readonly int end;
            private readonly int oldstart;
            private readonly int oldend;

            /// <summary>
            /// <see cref="LengthChange"/>クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="data">対象のクリップ</param>
            /// <param name="start">開始フレーム</param>
            /// <param name="end">終了フレーム</param>
            public LengthChange(ClipData data, int start, int end) {
                this.data = data;
                this.start = start;
                this.end = end;
                oldstart = data.Start;
                oldend = data.End;
            }


            /// <inheritdoc/>
            [Pure]
            public void Do() {
                data.Start = start;
                data.End = end;
            }

            /// <inheritdoc/>
            [Pure]
            public void Redo() => Do();

            /// <inheritdoc/>
            [Pure]
            public void Undo() {
                data.Start = oldstart;
                data.End = oldend;
            }
        }
        #endregion

        #endregion
    }

    /// <summary>
    /// 標準のクリップの種類
    /// </summary>
    public static class ClipType {
        public static readonly Type Video = typeof(DefaultData.Video);
        public static readonly Type Image = typeof(DefaultData.Image);
        public static readonly Type Text = typeof(DefaultData.Text);
        public static readonly Type Figure = typeof(DefaultData.Figure);
        public static readonly Type Camera = typeof(CameraObject);
        public static readonly Type GL3DObject = typeof(GL3DObject);
        public static readonly Type Scene = typeof(DefaultData.Scene);
    }

    public static class DataTools {

        /// <summary>
        /// ファイルの名前から適切なクリップの種類を返します
        /// </summary>
        /// <param name="file">ファイルの名前</param>
        /// <returns>System.Type</returns>
        public static Type FileTypeConvert(string file) {
            var ex = Path.GetExtension(file);
            if (ex is ".avi" or ".mp4") {
                return ClipType.Video;
            }
            else if (ex is ".jpg" or ".jpeg" or ".png" or ".bmp") {
                return ClipType.Image;
            }
            else if (ex is ".txt") {
                return ClipType.Text;
            }

            return ClipType.Figure;
        }
    }
}
