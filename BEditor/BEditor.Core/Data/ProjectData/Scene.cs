using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.ObjectData;
using BEditor.Core.Media;
using BEditor.Core.Renderer;

namespace BEditor.Core.Data.ProjectData {
    /// <summary>
    /// シーンクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public class Scene : ComponentObject {
        private ObservableCollection<ClipData> datas;
        private ClipData selectItem;
        private ObservableCollection<ClipData> selectItems;
        private int previewframe;
        private int totalframe = 1000;
        private float timeLineZoom = 150;
        private double timeLineHorizonOffset;
        private double timeLineVerticalOffset;

        /// <summary>
        /// フレームバッファの横幅を取得または設定します
        /// </summary>
        [DataMember(Order = 0)]
        public virtual int Width { get; protected set; }
        /// <summary>
        /// フレームバッファの高さを取得または設定します
        /// </summary>
        [DataMember(Order = 1)]
        public virtual int Height { get; protected set; }

        /// <summary>
        /// 名前を取得または設定します
        /// </summary>
        [DataMember(Order = 2)]
        public virtual string SceneName { get; set; }

        /// <summary>
        /// 選択されているクリップの <see cref="ClipData.Name"/> を取得します
        /// </summary>
        [DataMember(Order = 3)]
        public List<string> SelectNames { get; private set; } = new List<string>();

        /// <summary>
        /// 選択中のクリップの <see cref="ClipData.Name"/> を取得します
        /// </summary>
        [DataMember(Order = 4)]
        public string SelectName { get; private set; }

        /// <summary>
        /// タイムライン上の <see cref="ClipData"/> を取得します
        /// </summary>
        [DataMember(Order = 10)]
        public ObservableCollection<ClipData> Datas {
            get => datas;
            private set {
                datas = value;
                datas.AsParallel().ForAll(data => data.Scene = this);
            }
        }

        /// <summary>
        /// 隠されているレイヤーの番号を取得します
        /// </summary>
        [DataMember(Order = 11)]
        public List<int> HideLayer { get; private set; } = new List<int>();

        /// <summary>
        /// 選択中の <see cref="ClipData"/> を取得または設定します
        /// </summary>
        public ClipData SelectItem {
            get => selectItem ??= Get(SelectName);
            set {
                SelectName = selectItem?.Name;
                selectItem = value;
                RaisePropertyChanged(nameof(SelectItem));
            }
        }

        /// <summary>
        /// 選択されている <see cref="ClipData"/> を取得します
        /// </summary>
        public ObservableCollection<ClipData> SelectItems {
            get {
                if (selectItems == null) {
                    selectItems = new ObservableCollection<ClipData>();

                    SelectNames.AsParallel().ForAll(name => selectItems.Add(Get(name)));

                    selectItems.CollectionChanged += SelectItems_CollectionChanged;
                }

                return selectItems;
            }
        }

        private void SelectItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) {
                SelectNames.Insert(e.NewStartingIndex, selectItems[e.NewStartingIndex].Name);
            }
            else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) {
                if (SelectName == SelectNames[e.OldStartingIndex] || SelectItems.Count == 0) {
                    SelectItem = null;
                }

                SelectNames.RemoveAt(e.OldStartingIndex);
            }
        }

        /// <summary>
        /// レンダリングコンテキストを取得します
        /// </summary>
        public BaseRenderingContext RenderingContext { get; internal set; }

        #region コントロールに関係

        /// <summary>
        /// プレビュー中のフレームを取得または設定します
        /// </summary>
        [DataMember(Order = 5)]
        public int PreviewFrame { get => previewframe; set => SetValue(value, ref previewframe, nameof(PreviewFrame)); }

        /// <summary>
        /// 最大フレームを取得または設定します
        /// </summary>
        [DataMember(Order = 6)]
        public int TotalFrame { get => totalframe; set => SetValue(value, ref totalframe, nameof(TotalFrame)); }

        /// <summary>
        /// タイムラインの拡大率を取得または設定します
        /// </summary>
        [DataMember(Order = 7)]
        public float TimeLineZoom { get => timeLineZoom; set => SetValue(value, ref timeLineZoom, nameof(TimeLineZoom)); }

        #region TimeLineScrollOffset

        /// <summary>
        /// タイムラインの水平方向のスクロールのオフセットを取得または設定します
        /// </summary>
        [DataMember(Order = 8)]
        public double TimeLineHorizonOffset { get => timeLineHorizonOffset; set => SetValue(value, ref timeLineHorizonOffset, nameof(TimeLineHorizonOffset)); }


        /// <summary>
        /// タイムラインの垂直方向のスクロールのオフセットを取得または設定します
        /// </summary>
        [DataMember(Order = 9)]
        public double TimeLineVerticalOffset { get => timeLineVerticalOffset; set => SetValue(value, ref timeLineVerticalOffset, nameof(TimeLineVerticalOffset)); }

        #endregion

        #endregion


        public Image FrameBuffer;

        internal uint NewId {
            get {
                int count = Datas.Count;
                uint max;

                if (count > 0) {
                    List<uint> tmp = new List<uint>();

                    Parallel.For(0, count, i => tmp.Add(Datas[i].Id));

                    max = tmp.Max() + 1;
                }
                else {
                    max = 0;
                }

                return max;
            }
        }

        #region コンストラクタ

        /// <summary>
        /// <see cref="Scene"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">フレームバッファの横幅</param>
        /// <param name="height">フレームバッファの高さ</param>
        public Scene(int width, int height) {
            Width = width;
            Height = height;
            Datas = new ObservableCollection<ClipData>();
            RenderingContext = Component.Funcs.CreateRenderingContext(width, height);
        }

        #endregion

        #region Rendering
        /// <summary>
        /// シーンをレンダリングします
        /// </summary>
        /// <param name="frame">タイムライン基準のレンダリングするフレーム</param>
        /// <returns>レンダリングされた <seealso cref="Image"/></returns>
        public Image Rendering(int frame) {
            FrameBuffer?.Dispose();
            FrameBuffer = new Image(Width, Height);
            var layer = GetLayer(frame).ToList();

            RenderingContext.Clear();
            RenderingContext.MakeCurrent();

            var args = new ObjectLoadArgs(frame, layer);

            //Preview
            layer.ForEach(clip => clip.PreviewLoad(args));
            
            layer.ForEach(clip => clip.Load(args));
            
            RenderingContext.SwapBuffers();
            RenderingContext.MakeCurrent();

            Graphics.GetPixels(FrameBuffer);

            RenderingContext.UnMakeCurrent();

            if (frame % Component.Current.Project.Framerate * 5 == 1)
                Task.Run(GC.Collect);

            return FrameBuffer;
        }

        /// <summary>
        /// <seealso cref="PreviewFrame"/> のフレームをレンダリングします
        /// </summary>
        /// <returns>レンダリングされた <seealso cref="Image"/></returns>
        public Image Rendering() {
            return Rendering(PreviewFrame);
        }
        #endregion


        #region GetLayer

        /// <summary>
        /// フレーム上にあるクリップを取得しソートします
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns>クリップのリスト</returns>
        public IEnumerable<ClipData> GetLayer(int frame) {
            return Datas
                .Where(item => item.Start <= (frame) && (frame) < item.End)
                .Where(item => HideLayer.Exists(x => x == item.Layer))
                .OrderBy(item => item.Layer);
        }

        #endregion


        #region Listの操作
        /// <summary>
        /// クリップを追加し、<seealso cref="ClipData.Scene"/> にこのシーンを設定します
        /// </summary>
        /// <param name="data">追加するクリップ</param>
        public void Add(ClipData data) {
            data.Scene = this;

            Datas.Add(data);
        }
        /// <summary>
        /// シーンからクリップを削除します
        /// </summary>
        /// <param name="data">削除するクリップ</param>
        /// <returns>アイテムが正常に削除された場合は <see langword="true"/>、そうでない場合は <see langword="false"/> となります。このメソッドは、元の <see cref="Collection{T}"/> でアイテムが見つからなかった場合も <see langword="false"/> を返します</returns>
        public bool Remove(ClipData data) {
            return Datas.Remove(data);
        }
        /// <summary>
        /// クリップの <seealso cref="ClipData.Name"/> から <seealso cref="ClipData"/> を取得します
        /// </summary>
        /// <param name="name">取得するクリップの名前</param>
        /// <returns>存在する場合 <see cref="ClipData"/> のインスタンス、そうでない場合は <see langword="null"/> となります。 <paramref name="name"/> が <see langword="null"/> の場合も <see langword="null"/> を返します</returns>
        public ClipData Get(string name) {
            if (name != null) {
                foreach (var a in Datas) {
                    if (a.Name == name) return a;
                }
            }
            return null;
        }

        #endregion

        /// <summary>
        /// 選択中の <see cref="ClipData"/> を設定し、<see cref="SelectNames"/> に名前が存在しない場合追加します
        /// </summary>
        /// <param name="data">対象の <see cref="ClipData"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> が <see langword="null"/> です</exception>
        public void SetCurrentClip(ClipData data) {
            SelectItem = data ?? throw new ArgumentNullException(nameof(data));

            if (!SelectNames.Exists(x => x == data.Name)) {
                SelectItems.Add(data);
            }
        }
    }


    [DataContract(Namespace = "")]
    public sealed class RootScene : Scene {
        /// <inheritdoc/>
        public override string SceneName { get => "root"; set { } }

        /// <summary>
        /// <see cref="RootScene"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">フレームバッファの横幅</param>
        /// <param name="height">フレームバッファの高さ</param>
        public RootScene(int width, int height) : base(width, height) { }
    }
}