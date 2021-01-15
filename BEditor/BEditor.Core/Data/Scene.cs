using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Audio;
using BEditor.Core.Command;
using BEditor.Core.Extensions;
using BEditor.Core.Graphics;
using BEditor.Core.Renderings;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;

using OpenTK.Graphics.OpenGL;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    [DataContract]
    public class Scene : ComponentObject, IParent<ClipData>, IChild<Project>, IHasName, IHasId, IElementObject
    {
        #region Fields

        private static readonly PropertyChangedEventArgs selectItemArgs = new(nameof(SelectItem));
        private static readonly PropertyChangedEventArgs previreFrameArgs = new(nameof(PreviewFrame));
        private static readonly PropertyChangedEventArgs totalFrameArgs = new(nameof(TotalFrame));
        private static readonly PropertyChangedEventArgs zoomArgs = new(nameof(TimeLineZoom));
        private static readonly PropertyChangedEventArgs hoffsetArgs = new(nameof(TimeLineHorizonOffset));
        private static readonly PropertyChangedEventArgs voffsetArgs = new(nameof(TimeLineVerticalOffset));
        private static readonly PropertyChangedEventArgs sceneNameArgs = new(nameof(SceneName));
        private ClipData selectItem;
        private ObservableCollection<ClipData> selectItems;
        private Frame previewframe;
        private Frame totalframe = 1000;
        private float timeLineZoom = 150;
        private double timeLineHorizonOffset;
        private double timeLineVerticalOffset;
        private string sceneName;
        private IPlayer player;

        #endregion


        #region Contructor

        /// <summary>
        /// <see cref="Scene"/> Initialize a new instance of the class.
        /// </summary>
        /// <param name="width">The width of the frame buffer.</param>
        /// <param name="height">The height of the frame buffer</param>
        public Scene(int width, int height)
        {
            Width = width;
            Height = height;
            Datas = new ObservableCollection<ClipData>();
        }

        #endregion


        #region Properties

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// Get or set the width of the frame buffer.
        /// </summary>
        [DataMember(Order = 0)]
        public int Width { get; private set; }
        /// <summary>
        /// Get or set the height of the frame buffer
        /// </summary>
        [DataMember(Order = 1)]
        public int Height { get; private set; }

        /// <summary>
        /// Get or set the name of this <see cref="Scene"/>.
        /// </summary>
        [DataMember(Order = 2)]
        public virtual string SceneName
        {
            get => sceneName;
            set => SetValue(value, ref sceneName, sceneNameArgs);
        }

        /// <summary>
        /// Get the names of the selected <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 3)]
        public List<string> SelectNames { get; private set; } = new List<string>();
        /// <summary>
        /// Get the name of the selected <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 4)]
        public string SelectName { get; private set; }

        /// <summary>
        /// Get the <see cref="ClipData"/> contained in this <see cref="Scene"/>.
        /// </summary>
        [DataMember(Order = 10)]
        public ObservableCollection<ClipData> Datas { get; private set; }

        /// <summary>
        /// Get the number of the hidden layer.
        /// </summary>
        [DataMember(Order = 11)]
        public List<int> HideLayer { get; private set; } = new List<int>();

        /// <summary>
        /// Get or set the selected <see cref="ClipData"/>.
        /// </summary>
        public ClipData SelectItem
        {
            get => selectItem ??= this[SelectName];
            set
            {
                SelectName = selectItem?.Name;
                selectItem = value;
                RaisePropertyChanged(selectItemArgs);
            }
        }
        /// <summary>
        /// Get or set the selected <see cref="ClipData"/>.
        /// </summary>
        public ObservableCollection<ClipData> SelectItems
        {
            get
            {
                if (selectItems == null)
                {
                    selectItems = new ObservableCollection<ClipData>(SelectNames.Select(name => this.Find(name)));

                    selectItems.CollectionChanged += (s, e) =>
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                        {
                            SelectNames.Insert(e.NewStartingIndex, selectItems[e.NewStartingIndex].Name);
                        }
                        else if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                        {
                            if (SelectName == SelectNames[e.OldStartingIndex] || SelectItems.Count == 0)
                            {
                                SelectItem = null;
                            }

                            SelectNames.RemoveAt(e.OldStartingIndex);
                        }
                    };
                }

                return selectItems;
            }
        }


        /// <summary>
        /// Get graphic context.
        /// </summary>
        public GraphicsContext GraphicsContext { get; internal set; }
        /// <summary>
        /// Get audio context.
        /// </summary>
        public AudioContext AudioContext { get; internal set; }
        /// <summary>
        /// Get a player to play this <see cref="Scene"/>.
        /// </summary>
        public IPlayer Player
            => player ??= new ScenePlayer(this);


        #region コントロールに関係

        /// <summary>
        /// Gets or sets the frame number during preview.
        /// </summary>
        [DataMember(Order = 5)]
        public Frame PreviewFrame
        {
            get => previewframe;
            set => SetValue(value, ref previewframe, previreFrameArgs);
        }

        /// <summary>
        /// Get or set the total frame.
        /// </summary>
        [DataMember(Order = 6)]
        public Frame TotalFrame
        {
            get => totalframe;
            set => SetValue(value, ref totalframe, totalFrameArgs);
        }

        /// <summary>
        /// Get or set the scale of the timeline.
        /// </summary>
        [DataMember(Order = 7)]
        public float TimeLineZoom
        {
            get => timeLineZoom;
            set => SetValue(value, ref timeLineZoom, zoomArgs);
        }

        #region TimeLineScrollOffset

        /// <summary>
        /// Get or set the horizontal scrolling offset of the timeline.
        /// </summary>
        [DataMember(Order = 8)]
        public double TimeLineHorizonOffset
        {
            get => timeLineHorizonOffset;
            set => SetValue(value, ref timeLineHorizonOffset, hoffsetArgs);
        }


        /// <summary>
        /// Get or set the vertical scrolling offset of the timeline.
        /// </summary>
        [DataMember(Order = 9)]
        public double TimeLineVerticalOffset
        {
            get => timeLineVerticalOffset;
            set => SetValue(value, ref timeLineVerticalOffset, voffsetArgs);
        }

        #endregion

        #endregion

        /// <inheritdoc/>
        public IEnumerable<ClipData> Children => Datas;
        /// <inheritdoc/>
        public Project Parent { get; set; }
        /// <inheritdoc/>
        public string Name => SceneName;
        /// <inheritdoc/>
        public int Id => Parent.SceneList.IndexOf(this);

        internal int NewId
        {
            get
            {
                int count = Datas.Count;
                int max;

                if (count > 0)
                {
                    var tmp = new List<int>();

                    Parallel.For(0, count, i => tmp.Add(Datas[i].Id));

                    max = tmp.Max() + 1;
                }
                else
                {
                    max = 0;
                }

                return max;
            }
        }

        #endregion

        public ClipData this[string name] => this.Find(name);

        #region Methods

        /// <inheritdoc/>
        public void Loaded()
        {
            if (IsLoaded) return;

            GraphicsContext = new GraphicsContext(Width, Height);
            AudioContext = new AudioContext();
            foreach (var clip in Datas)
            {
                clip.Parent = this;
                clip.Loaded();
            }

            IsLoaded = true;
        }

        /// <inheritdoc/>
        public void Unloaded()
        {
            if (!IsLoaded) return;

            GraphicsContext.Dispose();
            AudioContext.Dispose();
            foreach (var clip in Datas)
            {
                clip.Unloaded();
            }

            IsLoaded = false;
        }


        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">The frame to render</param>
        /// <param name="renderType"></param>
        public RenderingResult Render(Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded) return new()
            {
                Image = new(Width, Height)
            };

            var layer = GetFrame(frame).ToList();

            GraphicsContext.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext.MakeCurrent();
            AudioContext.MakeCurrent();
            //GraphicsContext.Clear();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var args = new ClipRenderArgs(frame, renderType);

            //Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext.SwapBuffers();

            var buffer = new Image<BGRA32>(Width, Height);
            GraphicsContext.ReadImage(buffer);

            return new RenderingResult { Image = buffer };
        }
        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        public RenderingResult Render(RenderType renderType = RenderType.Preview)
        {
            return Render(PreviewFrame, renderType);
        }
        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        public void Render(Image<BGRA32> image, Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded) return;

            image.ThrowIfDisposed();
            if (image.Width != Width) throw new ArgumentException(null, nameof(image));
            if (image.Height != Height) throw new ArgumentException(null, nameof(image));

            var layer = GetFrame(frame).ToList();

            GraphicsContext.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext.MakeCurrent();
            AudioContext.MakeCurrent();
            GraphicsContext.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            //Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext.SwapBuffers();

            GraphicsContext.ReadImage(image);
        }
        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        public void Render(Image<BGRA32> image, RenderType renderType = RenderType.Preview)
        {
            Render(image, PreviewFrame, renderType);
        }


        /// <summary>
        /// Get and sort the clips on the specified frame.
        /// </summary>
        /// <param name="frame">Target frame number.</param>
        public IEnumerable<ClipData> GetFrame(Frame frame)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Start <= (frame) && (frame) < item.End)
                .Where(item => !HideLayer.Exists(x => x == item.Layer))
                .OrderBy(item => item.Layer);
        }
        /// <summary>
        /// Get and sort the clips on the specified layer.
        /// </summary>
        /// <param name="layer">Target layer number.</param>
        public IEnumerable<ClipData> GetLayer(int layer)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Layer == layer)
                .OrderBy(item => item.Start.Value);
        }

        /// <summary>
        /// Add a <see cref="ClipData"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip">A <see cref="ClipData"/> to add</param>
        public void Add(ClipData clip)
        {
            clip.Parent = this;

            Datas.Add(clip);
        }
        /// <summary>
        /// Remove certain a <see cref="ClipData"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipData"/> to be removed.</param>
        /// <returns>
        /// <see langword="true"/> if item is successfully removed; otherwise, <see langword="false"/>. This method also returns
        /// <see langword="false"/> if item was not found in the original <see cref="Collection{T}"/>.
        /// </returns>
        public bool Remove(ClipData clip)
        {
            return Datas.Remove(clip);
        }

        /// <summary>
        /// Set the selected <see cref="ClipData"/> and add the name to <see cref="SelectNames"/> if it does not exist.
        /// </summary>
        /// <param name="data">Target <see cref="ClipData"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
        public void SetCurrentClip(ClipData data)
        {
            SelectItem = data ?? throw new ArgumentNullException(nameof(data));

            if (!SelectNames.Exists(x => x == data.Name))
            {
                SelectItems.Add(data);
            }
        }

        #endregion

        internal sealed class RemoveLayer : IRecordCommand
        {
            private readonly IEnumerable<IRecordCommand> clips;

            public RemoveLayer(Scene scene, int layer)
            {
                clips = scene.GetLayer(layer).Select(clip => clip.Parent.CreateRemoveCommand(clip)).ToArray();
            }

            public void Do()
            {
                foreach (var clip in clips)
                {
                    clip.Do();
                }
            }
            public void Redo()
            {
                foreach (var clip in clips)
                {
                    clip.Redo();
                }
            }
            public void Undo()
            {
                foreach (var clip in clips)
                {
                    clip.Undo();
                }
            }
        }
    }

    [DataContract]
    public class RootScene : Scene
    {
        public RootScene(int width, int height) : base(width, height) { }

        public override string SceneName { get => "root"; set { } }
    }
}