using System;
using System.Diagnostics;
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
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;

using OpenTK.Graphics.OpenGL;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    [DataContract]
    public class Scene : ComponentObject, IParent<ClipData>, IChild<Project>, IHasName, IHasId, IElementObject
    {
        #region Fields

        private static readonly PropertyInfo _ClipDataID = typeof(ClipData).GetProperty(nameof(ClipData.Id))!;
        private static readonly PropertyChangedEventArgs _SelectItemArgs = new(nameof(SelectItem));
        private static readonly PropertyChangedEventArgs _PrevireFrameArgs = new(nameof(PreviewFrame));
        private static readonly PropertyChangedEventArgs _TotalFrameArgs = new(nameof(TotalFrame));
        private static readonly PropertyChangedEventArgs _ZoomArgs = new(nameof(TimeLineZoom));
        private static readonly PropertyChangedEventArgs _HoffsetArgs = new(nameof(TimeLineHorizonOffset));
        private static readonly PropertyChangedEventArgs _VoffsetArgs = new(nameof(TimeLineVerticalOffset));
        private static readonly PropertyChangedEventArgs _SceneNameArgs = new(nameof(SceneName));
        private static readonly PropertyChangedEventArgs _BackgroundColorArgs = new(nameof(BackgroundColor));
        private ClipData? _SelectItem;
        private ObservableCollection<ClipData?>? _SelectItems;
        private Frame _Previewframe;
        private Frame _Totalframe = 1000;
        private float _TimeLineZoom = 150;
        private double _TimeLineHorizonOffset;
        private double _TimeLineVerticalOffset;
        private string _SceneName = "";
        private IPlayer? _Player;
        private Color _BackgroundColor;

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
            get => _SceneName;
            set => SetValue(value, ref _SceneName, _SceneNameArgs);
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
        public string? SelectName { get; private set; }

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
        public ClipData? SelectItem
        {
            get => _SelectItem ??= this[SelectName ?? null];
            set
            {
                SelectName = _SelectItem?.Name;
                _SelectItem = value;
                RaisePropertyChanged(_SelectItemArgs);
            }
        }
        /// <summary>
        /// Get or set the selected <see cref="ClipData"/>.
        /// </summary>
        public ObservableCollection<ClipData?> SelectItems
        {
            get
            {
                if (_SelectItems == null)
                {
                    _SelectItems = new ObservableCollection<ClipData?>(SelectNames.Select(name => this.Find(name)));

                    _SelectItems.CollectionChanged += (s, e) =>
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                        {
                            if (_SelectItems[e.NewStartingIndex] is var item && item is not null)
                            {
                                SelectNames.Insert(e.NewStartingIndex, item.Name);
                            }
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

                return _SelectItems;
            }
        }
        /// <summary>
        /// Gets or sets the background color of the GraphicsContext
        /// </summary>
        [DataMember]
        public Color BackgroundColor
        {
            get => _BackgroundColor;
            set
            {
                if (GraphicsContext is not null)
                {
                    GraphicsContext.ClearColor = value;
                }

                SetValue(value, ref _BackgroundColor, _BackgroundColorArgs);
            }
        }
        /// <summary>
        /// Get graphic context.
        /// </summary>
        public GraphicsContext? GraphicsContext { get; private set; }
        /// <summary>
        /// Get audio context.
        /// </summary>
        public AudioContext? AudioContext { get; private set; }
        /// <summary>
        /// Get a player to play this <see cref="Scene"/>.
        /// </summary>
        public IPlayer Player
            => _Player ??= new ScenePlayer(this);


        #region コントロールに関係

        /// <summary>
        /// Gets or sets the frame number during preview.
        /// </summary>
        [DataMember(Order = 5)]
        public Frame PreviewFrame
        {
            get => _Previewframe;
            set => SetValue(value, ref _Previewframe, _PrevireFrameArgs);
        }

        /// <summary>
        /// Get or set the total frame.
        /// </summary>
        [DataMember(Order = 6)]
        public Frame TotalFrame
        {
            get => _Totalframe;
            set => SetValue(value, ref _Totalframe, _TotalFrameArgs);
        }

        /// <summary>
        /// Get or set the scale of the timeline.
        /// </summary>
        [DataMember(Order = 7)]
        public float TimeLineZoom
        {
            get => _TimeLineZoom;
            set => SetValue(value, ref _TimeLineZoom, _ZoomArgs);
        }

        #region TimeLineScrollOffset

        /// <summary>
        /// Get or set the horizontal scrolling offset of the timeline.
        /// </summary>
        [DataMember(Order = 8)]
        public double TimeLineHorizonOffset
        {
            get => _TimeLineHorizonOffset;
            set => SetValue(value, ref _TimeLineHorizonOffset, _HoffsetArgs);
        }


        /// <summary>
        /// Get or set the vertical scrolling offset of the timeline.
        /// </summary>
        [DataMember(Order = 9)]
        public double TimeLineVerticalOffset
        {
            get => _TimeLineVerticalOffset;
            set => SetValue(value, ref _TimeLineVerticalOffset, _VoffsetArgs);
        }

        #endregion

        #endregion

        /// <inheritdoc/>
        public IEnumerable<ClipData> Children => Datas;
        /// <inheritdoc/>
        public Project? Parent { get; set; }
        /// <inheritdoc/>
        public string Name => (SceneName ?? "").Replace('.', '_');
        /// <inheritdoc/>
        public int Id => Parent?.SceneList?.IndexOf(this) ?? -1;

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
        /// <summary>
        /// Gets or sets the settings for this scene.
        /// </summary>
        public SceneSettings Settings
        {
            get => new(Width, Height, Name, BackgroundColor);
            set
            {
                Width = value.Width;
                Height = value.Height;
                SceneName = value.Name;

                GraphicsContext?.Dispose();
                GraphicsContext = new(Width, Height);

                BackgroundColor = value.BackgroundColor;
            }
        }

        #endregion

        /// <summary>
        /// Get the <see cref="ClipData"/> from its <see cref="IHasName.Name"/>.
        /// </summary>
        /// <param name="name">Value of <see cref="IHasName.Name"/>.</param>
        public ClipData? this[string? name]
        {
            [return: NotNullIfNotNull("name")]
            get
            {
                if (name is null) return null;

                return this.Find(name);
            }
        }

        #region Methods

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            GraphicsContext = new GraphicsContext(Width, Height)
            {
                ClearColor = BackgroundColor
            };
            AudioContext = new AudioContext();
            foreach (var clip in Datas)
            {
                clip.Parent = this;
                clip.Load();
            }

            IsLoaded = true;
        }

        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            GraphicsContext?.Dispose();
            AudioContext?.Dispose();
            foreach (var clip in Datas)
            {
                clip.Unload();
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

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.MakeCurrent();
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var args = new ClipRenderArgs(frame, renderType);

            //Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext!.SwapBuffers();

            var buffer = new Image<BGRA32>(Width, Height);
            GraphicsContext!.ReadImage(buffer);

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

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.MakeCurrent();
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            //Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext!.SwapBuffers();

            GraphicsContext!.ReadImage(image);
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
        /// <param name="clip">Clip to be set to current.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
        public void SetCurrentClip(ClipData clip)
        {
            SelectItem = clip ?? throw new ArgumentNullException(nameof(clip));

            if (!SelectNames.Exists(x => x == clip.Name))
            {
                SelectItems.Add(clip);
            }
        }
        /// <summary>
        /// Create a command to add a <see cref="ClipData"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip">Clip to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand AddClip(ClipData clip)
        {
            //オブジェクトの情報
            clip.Parent = this;
            _ClipDataID.SetValue(clip, NewId);

            return RecordCommand.Create(
                clip,
                clip =>
                {
                    var scene = clip.Parent;
                    clip.Load();
                    scene.Add(clip);
                    scene.SetCurrentClip(clip);
                },
                clip =>
                {
                    var scene = clip.Parent;
                    scene.Remove(clip);
                    clip.Unload();

                    //存在する場合
                    if (scene.SelectNames.Exists(x => x == clip.Name))
                    {
                        scene.SelectItems.Remove(clip);

                        if (scene.SelectName == clip.Name)
                        {
                            scene.SelectItem = null;
                        }
                    }
                },
                _ => CommandName.AddClip);
        }
        /// <summary>
        /// Create a command to add a <see cref="ClipData"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">Frame to add a clip.</param>
        /// <param name="layer">Layer to add a clip.</param>
        /// <param name="metadata">Clip metadata to be added.</param>
        /// <param name="generatedClip">Generated clip.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand AddClip(Frame frame, int layer, ObjectMetadata metadata, out ClipData generatedClip)
        {
            var command = new ClipData.AddCommand(this, frame, layer, metadata);
            generatedClip = command.Clip;

            return command;
        }
        /// <summary>
        /// Create a command to remove <see cref="ClipData"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipData"/> to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [SuppressMessage("Performance", "CA1822:メンバーを static に設定します")]
        public IRecordCommand RemoveClip(ClipData clip)
            => new ClipData.RemoveCommand(clip);
        /// <summary>
        /// Create a command to remove the specified layer from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="layer">Layer number to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        public IRecordCommand RemoveLayer(int layer)
            => new RemoveLayerCommand(this, layer);
        #endregion

        internal sealed class RemoveLayerCommand : IRecordCommand
        {
            private readonly IEnumerable<IRecordCommand> _Clips;

            public RemoveLayerCommand(Scene scene, int layer)
            {
                _Clips = scene.GetLayer(layer).Select(clip => clip.Parent.RemoveClip(clip)).ToArray();
            }

            public string Name => CommandName.RemoveLayer;

            public void Do()
            {
                foreach (var clip in _Clips)
                {
                    clip.Do();
                }
            }
            public void Redo()
            {
                foreach (var clip in _Clips)
                {
                    clip.Redo();
                }
            }
            public void Undo()
            {
                foreach (var clip in _Clips)
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

    public record SceneSettings(int Width, int Height, string Name, Color BackgroundColor);
}