using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Command;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    [DataContract]
    public class Scene : EditorObject, IParent<ClipElement>, IChild<Project>, IHasName, IHasId, IElementObject, IJsonObject
    {
        #region Fields

        private static readonly PropertyInfo _ClipDataID = typeof(ClipElement).GetProperty(nameof(ClipElement.Id))!;
        private static readonly PropertyChangedEventArgs _SelectItemArgs = new(nameof(SelectItem));
        private static readonly PropertyChangedEventArgs _PrevireFrameArgs = new(nameof(PreviewFrame));
        private static readonly PropertyChangedEventArgs _TotalFrameArgs = new(nameof(TotalFrame));
        private static readonly PropertyChangedEventArgs _ZoomArgs = new(nameof(TimeLineZoom));
        private static readonly PropertyChangedEventArgs _HoffsetArgs = new(nameof(TimeLineHorizonOffset));
        private static readonly PropertyChangedEventArgs _VoffsetArgs = new(nameof(TimeLineVerticalOffset));
        private static readonly PropertyChangedEventArgs _SceneNameArgs = new(nameof(SceneName));
        private ClipElement? _selectItem;
        private ObservableCollection<ClipElement>? _selectItems;
        private Frame _previewframe;
        private Frame _totalframe = 1000;
        private float _timeLineZoom = 150;
        private double _timeLineHorizonOffset;
        private double _timeLineVerticalOffset;
        private string _sceneName = string.Empty;
        private IPlayer? _player;
        private WeakReference<Project?>? _parent;

        #endregion

        #region Contructor

        /// <summary>
        /// Initializes a new instance of the <see cref="Scene"/> class.
        /// </summary>
        /// <param name="width">The width of the frame buffer.</param>
        /// <param name="height">The height of the frame buffer.</param>
        public Scene(int width, int height)
        {
            Width = width;
            Height = height;
            Datas = new ObservableCollection<ClipElement>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the width of the frame buffer.
        /// </summary>
        [DataMember(Order = 0)]
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height of the frame buffer.
        /// </summary>
        [DataMember(Order = 1)]
        public int Height { get; private set; }

        /// <summary>
        /// Gets or sets the name of this <see cref="Scene"/>.
        /// </summary>
        [DataMember(Order = 2)]
        public virtual string SceneName
        {
            get => _sceneName;
            set => SetValue(value, ref _sceneName, _SceneNameArgs);
        }

        /// <summary>
        /// Gets or sets the total frame.
        /// </summary>
        [DataMember(Order = 3)]
        public Frame TotalFrame
        {
            get => _totalframe;
            set => SetValue(value, ref _totalframe, _TotalFrameArgs);
        }

        /// <summary>
        /// Gets the number of the hidden layer.
        /// </summary>
        [DataMember(Order = 4)]
        public List<int> HideLayer { get; private set; } = new List<int>();

        /// <summary>
        /// Gets the <see cref="ClipElement"/> contained in this <see cref="Scene"/>.
        /// </summary>
        [DataMember(Order = 5)]
        public ObservableCollection<ClipElement> Datas { get; private set; }

        /// <summary>
        /// Gets or sets the selected <see cref="ClipElement"/>.
        /// </summary>
        public ClipElement? SelectItem
        {
            get => _selectItem ??= SelectItems.FirstOrDefault();
            set
            {
                _selectItem = value;
                RaisePropertyChanged(_SelectItemArgs);
            }
        }

        /// <summary>
        /// Gets the selected <see cref="ClipElement"/>.
        /// </summary>
        public ObservableCollection<ClipElement> SelectItems
        {
            get
            {
                if (_selectItems is null)
                {
                    _selectItems = new();

                    _selectItems.CollectionChanged += (s, e) =>
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                        {
                            if (SelectItems.Count == 0)
                            {
                                SelectItem = null;
                            }
                        }
                    };
                }

                return _selectItems;
            }
        }

        /// <summary>
        /// Gets graphic context.
        /// </summary>
        public GraphicsContext? GraphicsContext { get; private set; }

        /// <summary>
        /// Gets audio context.
        /// </summary>
        public AudioContext? AudioContext { get; private set; }

        /// <summary>
        /// Gets a player to play this <see cref="Scene"/>.
        /// </summary>
        public IPlayer Player
            => _player ??= new ScenePlayer(this);

        #region コントロールに関係

        /// <summary>
        /// Gets or sets the frame number during preview.
        /// </summary>
        public Frame PreviewFrame
        {
            get => _previewframe;
            set => SetValue(value, ref _previewframe, _PrevireFrameArgs);
        }

        /// <summary>
        /// Gets or sets the scale of the timeline.
        /// </summary>
        public float TimeLineZoom
        {
            get => _timeLineZoom;
            set => SetValue(value, ref _timeLineZoom, _ZoomArgs);
        }

        #region TimeLineScrollOffset

        /// <summary>
        /// Gets or sets the horizontal scrolling offset of the timeline.
        /// </summary>
        public double TimeLineHorizonOffset
        {
            get => _timeLineHorizonOffset;
            set => SetValue(value, ref _timeLineHorizonOffset, _HoffsetArgs);
        }

        /// <summary>
        /// Gets or sets the vertical scrolling offset of the timeline.
        /// </summary>
        public double TimeLineVerticalOffset
        {
            get => _timeLineVerticalOffset;
            set => SetValue(value, ref _timeLineVerticalOffset, _VoffsetArgs);
        }

        #endregion

        #endregion

        /// <inheritdoc/>
        public IEnumerable<ClipElement> Children => Datas;

        /// <inheritdoc/>
        public Project Parent

        {
            get
            {
                _parent ??= new(null!);

                if (_parent.TryGetTarget(out var p))
                {
                    return p;
                }

                return null!;
            }
            set
            {
                (_parent ??= new(null!)).SetTarget(value);

                foreach (var prop in Children)
                {
                    prop.Parent = this;
                }
            }
        }

        /// <inheritdoc/>
        public string Name => (SceneName ?? string.Empty).Replace('.', '_');

        /// <inheritdoc/>
        public int Id => Parent?.SceneList?.IndexOf(this) ?? -1;

        /// <summary>
        /// Gets or sets the settings for this scene.
        /// </summary>
        public SceneSettings Settings
        {
            get => new(Width, Height, Name);
            set
            {
                Width = value.Width;
                Height = value.Height;
                SceneName = value.Name;

                GraphicsContext?.Dispose();
                GraphicsContext = new(Width, Height);
            }
        }

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

        /// <summary>
        /// Gets the <see cref="ClipElement"/> from its <see cref="IHasName.Name"/>.
        /// </summary>
        /// <param name="name">Value of <see cref="IHasName.Name"/>.</param>
        public ClipElement? this[string? name]
        {
            [return: NotNullIfNotNull("name")]
            get
            {
                if (name is null)
                {
                    return null;
                }

                return this.Find(name);
            }
        }

        #region Methods

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Debug.Assert(Synchronize is not null);
            Synchronize.Send(_ =>
            {
                GraphicsContext = new GraphicsContext(Width, Height);
                AudioContext = new AudioContext();
            }, null);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Synchronize.Send(_ =>
            {
                GraphicsContext?.Dispose();
                AudioContext?.Dispose();
            }, null);
        }

        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public RenderingResult Render(Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded)
            {
                return new()
                {
                    Image = new(Width, Height),
                };
            }

            var layer = GetFrame(frame).ToList();

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            var buffer = new Image<BGRA32>(Width, Height);
            GraphicsContext.ReadImage(buffer);

            return new RenderingResult { Image = buffer };
        }

        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public RenderingResult Render(RenderType renderType = RenderType.Preview)
        {
            return Render(PreviewFrame, renderType);
        }

        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="image">The image to be drawn.</param>
        /// <param name="frame">The frame to render.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(Image<BGRA32> image, Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded) return;

            image.ThrowIfDisposed();
            if (image.Width != Width) throw new ArgumentException(null, nameof(image));
            if (image.Height != Height) throw new ArgumentException(null, nameof(image));

            var layer = GetFrame(frame).ToList();

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext!.ReadImage(image);
        }

        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        /// <param name="image">The image to be drawn.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(Image<BGRA32> image, RenderType renderType = RenderType.Preview)
        {
            Render(image, PreviewFrame, renderType);
        }

        /// <summary>
        /// Get and sort the clips on the specified frame.
        /// </summary>
        /// <param name="frame">Target frame number.</param>
        /// <returns>Returns a clips that contains the specified frame.</returns>
        public IEnumerable<ClipElement> GetFrame(Frame frame)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Start <= frame && frame < item.End)
                .Where(item => !HideLayer.Exists(x => x == item.Layer))
                .OrderBy(item => item.Layer);
        }

        /// <summary>
        /// Get and sort the clips on the specified layer.
        /// </summary>
        /// <param name="layer">Target layer number.</param>
        /// <returns>Returns a clips that contains the specified layer.</returns>
        public IEnumerable<ClipElement> GetLayer(int layer)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Layer == layer)
                .OrderBy(item => item.Start.Value);
        }

        /// <summary>
        /// Add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip">A <see cref="ClipElement"/> to add.</param>
        public void Add(ClipElement clip)
        {
            clip.Parent = this;

            Datas.Add(clip);
        }

        /// <summary>
        /// Remove certain a <see cref="ClipElement"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be removed.</param>
        /// <returns>
        /// <see langword="true"/> if item is successfully removed; otherwise, <see langword="false"/>. This method also returns
        /// <see langword="false"/> if item was not found in the original <see cref="Collection{T}"/>.
        /// </returns>
        public bool Remove(ClipElement clip)
        {
            return Datas.Remove(clip);
        }

        /// <summary>
        /// Set the selected <see cref="ClipElement"/> and add the name to <see cref="SelectItems"/> if it does not exist.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be set to current.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
        public void SetCurrentClip(ClipElement clip)
        {
            SelectItem = clip ?? throw new ArgumentNullException(nameof(clip));

            if (!SelectItems.Contains(clip))
            {
                SelectItems.Add(clip);
            }
        }

        /// <summary>
        /// Create a command to add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand AddClip(ClipElement clip)
        {
            // オブジェクトの情報
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

                    // 存在する場合
                    if (scene.SelectItems.Remove(clip))
                    {
                        if (scene.SelectItem == clip)
                        {
                            scene.SelectItem = null;
                        }
                    }
                },
                _ => CommandName.AddClip);
        }

        /// <summary>
        /// Create a command to add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">Frame to add a clip.</param>
        /// <param name="layer">Layer to add a clip.</param>
        /// <param name="metadata">Clip metadata to be added.</param>
        /// <param name="generatedClip">Generated <see cref="ClipElement"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand AddClip(Frame frame, int layer, ObjectMetadata metadata, out ClipElement generatedClip)
        {
            var command = new ClipElement.AddCommand(this, frame, layer, metadata);
            generatedClip = command.Clip;

            return command;
        }

#pragma warning disable CA1822
        /// <summary>
        /// Create a command to remove <see cref="ClipElement"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand RemoveClip(ClipElement clip)
        {
            return new ClipElement.RemoveCommand(clip);
        }
#pragma warning restore CA1822

        /// <summary>
        /// Create a command to remove the specified layer from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="layer">Layer number to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand RemoveLayer(int layer)
        {
            return new RemoveLayerCommand(this, layer);
        }

        /// <inheritdoc/>
        public void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteNumber(nameof(Width), Width);
            writer.WriteNumber(nameof(Height), Height);
            writer.WriteString(nameof(SceneName), SceneName);
            writer.WriteNumber(nameof(TotalFrame), TotalFrame);
            writer.WriteStartArray(nameof(HideLayer));
            {
                foreach (var layer in HideLayer)
                {
                    writer.WriteNumberValue(layer);
                }
            }
            writer.WriteEndArray();
            writer.WriteStartArray("Clips");
            {
                foreach (var clip in Datas)
                {
                    writer.WriteStartObject();
                    {
                        clip.GetObjectData(writer);
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public void SetObjectData(JsonElement element)
        {
            Width = element.GetProperty(nameof(Width)).GetInt32();
            Height = element.GetProperty(nameof(Height)).GetInt32();
            SceneName = element.GetProperty(nameof(SceneName)).GetString() ?? "";
            TotalFrame = element.GetProperty(nameof(TotalFrame)).GetInt32();
            HideLayer = element.GetProperty(nameof(HideLayer)).EnumerateArray().Select(i => i.GetInt32()).ToList();
            Datas = new(element.GetProperty("Clips").EnumerateArray().Select(i =>
              {
                  var clip = (ClipElement)FormatterServices.GetUninitializedObject(typeof(ClipElement));
                  clip.SetObjectData(i);

                  return clip;
              }));
        }
        #endregion

        internal sealed class RemoveLayerCommand : IRecordCommand
        {
            private readonly IEnumerable<IRecordCommand> _clips;

            public RemoveLayerCommand(Scene scene, int layer)
            {
                _clips = scene.GetLayer(layer).Select(clip => clip.Parent.RemoveClip(clip)).ToArray();
            }

            public string Name => CommandName.RemoveLayer;

            public void Do()
            {
                foreach (var clip in _clips)
                {
                    clip.Do();
                }
            }

            public void Redo()
            {
                foreach (var clip in _clips)
                {
                    clip.Redo();
                }
            }

            public void Undo()
            {
                foreach (var clip in _clips)
                {
                    clip.Undo();
                }
            }
        }
    }

    /// <summary>
    /// Represents a <see cref="Scene"/> setting.
    /// </summary>
    public record SceneSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSettings"/> class.
        /// </summary>
        /// <param name="width">The width of the frame buffer.</param>
        /// <param name="height">The height of the frame buffer.</param>
        /// <param name="name">The name of the <see cref="Scene"/>.</param>
        public SceneSettings(int width, int height, string name)
        {
            Width = width;
            Height = height;
            Name = name;
        }

        /// <summary>
        /// Gets the width.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name { get; init; }
    }
}