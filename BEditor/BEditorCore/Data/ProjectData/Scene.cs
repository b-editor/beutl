using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditorCore.Data.ObjectData;
using BEditorCore.Interfaces;
using BEditorCore.Media;
using BEditorCore.Renderer;

namespace BEditorCore.Data.ProjectData {

    [DataContract(Namespace = "")]
    public class Scene : ComponentObject {
        private ObservableCollection<ClipData> datas;
        private ClipData selectItem;
        private ObservableCollection<ClipData> selectItems;
        private int previewframe;
        private int totalframe = 1000;
        private float timeLineZoom = 50;
        private double timeLineHorizonOffset;
        private double timeLineVerticalOffset;

        [DataMember(Order = 0)]
        public virtual int Width { get; set; }
        [DataMember(Order = 1)]
        public virtual int Height { get; set; }

        [DataMember(Order = 2)]
        public virtual string SceneName { get; set; }

        [DataMember(Order = 3)]
        public List<string> SelectNames { get; set; } = new List<string>();

        [DataMember(Order = 4)]
        public string SelectName { get; private set; }

        [DataMember(Order = 10)]
        public ObservableCollection<ClipData> Datas {
            get => datas;
            set {
                datas = value;

                for (int i = 0; i < datas.Count; i++) {
                    datas[i].Scene = this;
                }
            }
        }

        [DataMember(Order = 11)]
        public List<int> HideLayer { get; set; } = new List<int>();


        public ClipData SelectItem {
            get => selectItem ??= Get(SelectName);
            set {
                SelectName = selectItem?.Name;
                selectItem = value;
                RaisePropertyChanged(nameof(SelectItem));
            }
        }

        public ObservableCollection<ClipData> SelectItems {
            get {
                if (selectItems == null) {
                    selectItems = new ObservableCollection<ClipData>();


                    foreach (var name in SelectNames) {
                        selectItems.Add(Get(name));
                    }

                    selectItems.CollectionChanged += (s, e) => {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) {
                            SelectNames.Insert(e.NewStartingIndex, selectItems[e.NewStartingIndex].Name);
                        }
                        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove) {
                            if (SelectName == SelectNames[e.OldStartingIndex] || SelectItems.Count == 0) {
                                SelectItem = null;
                            }

                            SelectNames.RemoveAt(e.OldStartingIndex);
                        }
                    };
                }

                return selectItems;
            }
        }


        public BaseRenderingContext RenderingContext { get; set; }

        #region コントロールに関係

        [DataMember(Order = 5)]
        public int PreviewFrame { get => previewframe; set => SetValue(value, ref previewframe, nameof(PreviewFrame)); }

        [DataMember(Order = 6)]
        public int TotalFrame { get => totalframe; set => SetValue(value, ref totalframe, nameof(TotalFrame)); }

        [DataMember(Order = 7)]
        public float TimeLineZoom { get => timeLineZoom; set => SetValue(value, ref timeLineZoom, nameof(TimeLineZoom)); }

        #region TimeLineScrollOffset

        [DataMember(Order = 8)]
        public double TimeLineHorizonOffset { get => timeLineHorizonOffset; set => SetValue(value, ref timeLineHorizonOffset, nameof(TimeLineHorizonOffset)); }


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
        public Scene() { }

        public Scene(int width, int height) : this(width, height, new ObservableCollection<ClipData>()) {

        }

        public Scene(int width, int height, ObservableCollection<ClipData> datas) {
            Width = width;
            Height = height;
            Datas = datas;
            RenderingContext = Component.Funcs.CreateRenderingContext(width, height);
        }
        #endregion

        #region Rendering
        /// <summary>
        /// シーンをレンダリング
        /// </summary>
        /// <param name="frame">フレーム</param>
        /// <returns></returns>
        public Image Rendering(int frame) {
            FrameBuffer?.Dispose();
            FrameBuffer = new Image(Width, Height);
            var layer = GetLayer(frame);

            RenderingContext.Clear(Width, Height);
            RenderingContext.MakeCurrent();

            var args = new ObjectLoadArgs(frame, layer);

            //Preview
            foreach (var obj in layer) {
                if (HideLayer.Exists(x => x == obj.Layer)) {
                    continue;
                }

                obj.PreviewLoad(args);
            }

            foreach (var obj in layer) {
                if (HideLayer.Exists(x => x == obj.Layer)) {
                    continue;
                }

                obj.Load(args);
            }

            RenderingContext.MakeCurrent();
            RenderingContext.SwapBuffers();

            Graphics.GetPixels(FrameBuffer);

            return FrameBuffer;
        }

        public Image Rendering() {
            return Rendering(PreviewFrame);
        }
        #endregion


        #region GetLayer
        /// <summary>
        /// フレーム上にあるオブジェクトを取得しソートします
        /// </summary>
        /// <param name="frame">フレーム番号</param>
        /// <returns>オブジェクトのリスト</returns>
        public List<ClipData> GetLayer(int frame) {
            var List = (
                from item in Datas
                where item.Start <= (frame) && (frame) < item.End
                select item
                ).ToList();
            List.Sort((a, b) => a.Layer - b.Layer);

            return List;
        }

        #endregion


        #region Listの操作
        public void Add(ClipData data) {
            data.Scene = this;

            Datas.Add(data);
        }

        public bool Remove(ClipData data) {
            return Datas.Remove(data);
        }
        public ClipData Get(string name) {
            if (name != null) {
                foreach (var a in Datas) {
                    if (a.Name == name) return a;
                }
            }
            return null;
        }

        public int Count => Datas.Count;

        #endregion

        public void SetCurrentClip(ClipData data) {
            SelectItem = data;

            if (!SelectNames.Exists(x => x == data.Name)) {
                SelectItems.Add(data);
            }
        }
    }


    [DataContract(Namespace = "")]
    public class RootScene : Scene {
        public override string SceneName { get => "root"; set { } }

        public RootScene() { }

        public RootScene(int width, int height) : base(width, height) { }
    }
}
