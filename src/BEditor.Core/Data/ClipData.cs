using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Media;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a data of a clip to be placed in the timeline.
    /// </summary>
    [DataContract]
    public class ClipData : ComponentObject, ICloneable, IParent<EffectElement>, IChild<Scene>, IHasName, IHasId, IFormattable, IElementObject
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _StartArgs = new(nameof(Start));
        private static readonly PropertyChangedEventArgs _EndArgs = new(nameof(End));
        private static readonly PropertyChangedEventArgs _LayerArgs = new(nameof(Layer));
        private static readonly PropertyChangedEventArgs _TextArgs = new(nameof(LabelText));
        private string? _Name;
        private Frame _Start;
        private Frame _End;
        private int _Layer;
        private string _LabelText = "";
        #endregion


        #region Contructor

        /// <summary>
        /// <see cref="ClipData"/> Initialize a new instance of the class.
        /// </summary>
        public ClipData(int id, ObservableCollection<EffectElement> effects, Frame start, Frame end, Type type, int layer, Scene scene)
        {
            Id = id;
            _Start = start;
            _End = end;
            Type = type;
            _Layer = layer;
            Parent = scene;
            Effect = effects;
            LabelText = Name;
        }

        #endregion


        #region Properties

        /// <summary>
        /// Get the ID for this <see cref="ClipData"/>
        /// </summary>
        [DataMember(Order = 0)]
        public int Id { get; private set; }

        /// <summary>
        /// Get the name of this <see cref="ClipData"/>.
        /// </summary>
        public string Name => _Name ??= $"{Type.Name}{Id}";

        /// <summary>
        /// Get the type of this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Name = "Type", Order = 1)]
        public string ClipType
        {
            get => Type.FullName!;
            private set => Type = Type.GetType(value)!;
        }

        /// <summary>
        /// Get the type of this <see cref="ClipData"/>.
        /// </summary>
        public Type Type { get; private set; }

        /// <summary>
        /// Get or set the start frame for this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 2)]
        public Frame Start
        {
            get => _Start;
            set => SetValue(value, ref _Start, _StartArgs);
        }

        /// <summary>
        /// Get or set the end frame for this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 3)]
        public Frame End
        {
            get => _End;
            set => SetValue(value, ref _End, _EndArgs);
        }

        /// <summary>
        /// Get the length of this <see cref="ClipData"/>.
        /// </summary>
        public Frame Length => End - Start;

        /// <summary>
        /// Get or set the layer where this <see cref="ClipData"/> will be placed.
        /// </summary>
        [DataMember(Order = 4)]
        public int Layer
        {
            get => _Layer;
            set
            {
                if (value == 0) return;
                SetValue(value, ref _Layer, _LayerArgs);
            }
        }

        /// <summary>
        /// Gets or sets the character displayed in this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Name = "Text", Order = 5)]
        public string LabelText
        {
            get => _LabelText;
            set => SetValue(value, ref _LabelText, _TextArgs);
        }

        /// <inheritdoc/>
        public Scene Parent { get; internal set; }

        /// <summary>
        /// Get the effects included in this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Name = "Effects", Order = 6)]
        public ObservableCollection<EffectElement> Effect { get; private set; }

        /// <inheritdoc/>
        public IEnumerable<EffectElement> Children => Effect;

        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        #endregion


        #region Methods

        /// <summary>
        /// Render this clip.
        /// </summary>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(ClipRenderArgs args)
        {
            try
            {
                var loadargs = new EffectRenderArgs(args.Frame, args.Type);

                if (Effect[0] is ObjectElement obj)
                {
                    if (!obj.IsEnabled) return;

                    obj.Render(loadargs);
                }
            }
            catch (Exception e)
            {
                throw new RenderingException("Faileds to rendering.", e);
            }
        }
        /// <summary>
        /// Prepare this clip for rendering.
        /// </summary>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void PreviewRender(ClipRenderArgs args)
        {
            try
            {
                var enableEffects = Effect.Where(x => x.IsEnabled);
                var loadargs = new EffectRenderArgs(args.Frame, args.Type);

                foreach (var item in enableEffects)
                {
                    item.PreviewRender(loadargs);
                }
            }
            catch(Exception e)
            {
                throw new RenderingException("Faileds to rendering.", e);
            }
        }

        internal void MoveFrame(Frame f)
        {
            Start += f;
            End += f;
        }
        internal void MoveTo(Frame start)
        {
            var length = Length;
            Start = start;
            End = length + start;
        }

        /// <inheritdoc/>
        object ICloneable.Clone() => Clone();
        /// <inheritdoc cref="ICloneable.Clone"/>
        public ClipData Clone()
        {
            var clip = this.DeepClone()!;

            clip.Parent = Parent;
            clip.Load();

            return clip;
        }

        /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        public string ToString(string? format)
            => ToString(format, CultureInfo.CurrentCulture);
        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (string.IsNullOrEmpty(format)) format = "#";

            return format switch
            {
                "#" => $"{Parent.Name}.{Name}",
                _ => throw new FormatException(string.Format("The {0} format string is not supported.", format))
            };
        }

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            foreach (var effect in Effect)
            {
                effect.Parent = this;
                effect.Load();
            }

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            foreach (var effect in Effect)
            {
                effect.Unload();
            }

            IsLoaded = false;
        }
        /// <summary>
        /// Get the clip from its full name.
        /// </summary>
        public static ClipData? FromFullName(string name, Project? project)
        {
            if (project is null) return null;

            var reg = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\z");

            if (reg.IsMatch(name))
            {
                var match = reg.Match(name);

                var scene = project.Find(match.Groups[1].Value);
                var clip = scene?.Find(match.Groups[2].Value);

                return clip;
            }

            return null;
        }
        /// <summary>
        /// Create a command to add an effect to this clip
        /// </summary>
        /// <param name="effect"><see cref="EffectElement"/> to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
        [Pure]
        public IRecordCommand AddEffect(EffectElement effect)
        {
            if (effect is null) throw new ArgumentNullException(nameof(effect));


            return new EffectElement.RemoveCommand(effect, this);
        }
        /// <summary>
        /// Create a command to remove an effect to this clip
        /// </summary>
        /// <param name="effect"><see cref="EffectElement"/> to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
        [Pure]
        public IRecordCommand RemoveEffect(EffectElement effect)
        {
            if (effect is null) throw new ArgumentNullException(nameof(effect));

            return new EffectElement.RemoveCommand(effect, this);
        }
        /// <summary>
        /// Create a command to move this clip frames and layers.
        /// </summary>
        /// <param name="toFrame">Frame to be moved</param>
        /// <param name="toLayer">Layer to be moved.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="toFrame"/> or <paramref name="toLayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame toFrame, int toLayer)
            => new MoveCommand(this, toFrame, toLayer);
        /// <summary>
        /// Create a command to move this clip frames and layers.
        /// </summary>
        /// <param name="to">Frame to be moved.</param>
        /// <param name="from">Frame to be moved from.</param>
        /// <param name="tolayer">Layer to be moved.</param>
        /// <param name="fromlayer">Layer to be moved from.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame to, Frame from, int tolayer, int fromlayer)
            => new MoveCommand(this, to, from, tolayer, fromlayer);
        /// <summary>
        /// Create a command to change the length of this clip.
        /// </summary>
        /// <param name="start">New start frame for this <see cref="ClipData"/>.</param>
        /// <param name="end">New end frame for this <see cref="ClipData"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand ChangeLength(Frame start, Frame end)
            => new LengthChangeCommand(this, start, end);
        /// <summary>
        /// Create a command to split this clip at the specified frame.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand Split(Frame frame)
            => new SplitCommand(this, frame);

        #endregion

        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            public ClipData Clip;

            public AddCommand(Scene scene, Frame startFrame, int layer, ObjectMetadata metadata)
            {
                Scene = scene ?? throw new ArgumentNullException(nameof(scene));
                if (Frame.Zero > startFrame) throw new ArgumentOutOfRangeException(nameof(startFrame));
                if (0 > layer) throw new ArgumentOutOfRangeException(nameof(layer));
                if (metadata is null) throw new ArgumentNullException(nameof(metadata));

                //新しいidを取得
                int idmax = scene.NewId;

                //描画情報
                var list = new ObservableCollection<EffectElement>
                {
                    metadata.CreateFunc()
                };

                //オブジェクトの情報
                Clip = new ClipData(idmax, list, startFrame, startFrame + 180, metadata.Type, layer, scene);
            }

            public string Name => CommandName.AddClip;

            public void Do()
            {
                Clip.Load();
                Scene.Add(Clip);
                Scene.SetCurrentClip(Clip);
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                Scene.Remove(Clip);
                Clip.Unload();

                //存在する場合
                if (Scene.SelectNames.Exists(x => x == Clip.Name))
                {
                    Scene.SelectItems.Remove(Clip);

                    if (Scene.SelectName == Clip.Name)
                    {
                        Scene.SelectItem = null;
                    }
                }
            }
        }
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;

            public RemoveCommand(ClipData clip) => _Clip = clip ?? throw new ArgumentNullException(nameof(clip));

            public string Name => CommandName.RemoveClip;

            public void Do()
            {
                if (!_Clip.Parent.Remove(_Clip))
                {
                    //Message.Snackbar("削除できませんでした");
                }
                else
                {
                    _Clip.Unload();
                    //存在する場合
                    if (_Clip.Parent.SelectNames.Exists(x => x == _Clip.Name))
                    {
                        _Clip.Parent.SelectItems.Remove(_Clip);

                        if (_Clip.Parent.SelectName == _Clip.Name)
                        {
                            if (_Clip.Parent.SelectItems.Count == 0)
                            {
                                _Clip.Parent.SelectItem = null;
                            }
                            else
                            {
                                _Clip.Parent.SelectItem = _Clip.Parent.SelectItems[0];
                            }
                        }
                    }
                }
            }
            public void Redo() => Do();
            public void Undo()
            {
                _Clip.Load();
                _Clip.Parent.Add(_Clip);
            }
        }
        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly Frame _ToFrame;
            private readonly Frame _FromFrame;
            private readonly int _ToLayer;
            private readonly int _FromLayer;
            private Scene Scene => _Clip.Parent;

            #region コンストラクタ
            public MoveCommand(ClipData clip, Frame toFrame, int toLayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                _FromFrame = clip.Start;
                _ToLayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                _FromLayer = clip.Layer;
            }
            public MoveCommand(ClipData clip, Frame to, Frame from, int tolayer, int fromlayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
                _FromFrame = (Frame.Zero > from) ? throw new ArgumentOutOfRangeException(nameof(from)) : from;
                _ToLayer = (0 > tolayer) ? throw new ArgumentOutOfRangeException(nameof(tolayer)) : tolayer;
                _FromLayer = (0 > fromlayer) ? throw new ArgumentOutOfRangeException(nameof(fromlayer)) : fromlayer;
            }
            #endregion

            public string Name => CommandName.MoveClip;

            public void Do()
            {
                _Clip.MoveTo(_ToFrame);

                _Clip.Layer = _ToLayer;


                if (_Clip.End > Scene.TotalFrame)
                {
                    Scene.TotalFrame = _Clip.End;
                }
            }
            public void Redo() => Do();
            public void Undo()
            {
                _Clip.MoveTo(_FromFrame);

                _Clip.Layer = _FromLayer;
            }
        }
        private sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly Frame _Start;
            private readonly Frame _End;
            private readonly Frame _OldStart;
            private readonly Frame _OldEnd;

            public LengthChangeCommand(ClipData clip, Frame start, Frame end)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _Start = (Frame.Zero > start) ? throw new ArgumentOutOfRangeException(nameof(start)) : start;
                _End = (Frame.Zero > end) ? throw new ArgumentOutOfRangeException(nameof(end)) : end;
                _OldStart = clip.Start;
                _OldEnd = clip.End;
            }

            public string Name => CommandName.ChangeLength;

            public void Do()
            {
                _Clip.Start = _Start;
                _Clip.End = _End;
            }
            public void Redo() => Do();
            public void Undo()
            {
                _Clip.Start = _OldStart;
                _Clip.End = _OldEnd;
            }
        }
        private sealed class SplitCommand : IRecordCommand
        {
            public readonly ClipData Before;
            public readonly ClipData After;
            private readonly ClipData Source;
            private readonly Scene Scene;

            public SplitCommand(ClipData clip, Frame frame)
            {
                Source = clip;
                Scene = clip.Parent;
                Before = (ClipData)clip.Clone();
                After = (ClipData)clip.Clone();

                Before.End = frame;
                After.Start = frame;
            }

            public string Name => CommandName.SplitClip;

            public void Do()
            {
                After.Load();
                Before.Load();

                new RemoveCommand(Source).Do();
                After.Id = Scene.NewId;
                Scene.Add(After);
                Before.Id = Scene.NewId;
                Scene.Add(Before);
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                Before.Unload();
                After.Unload();
                Source.Load();

                Scene.Remove(Before);
                Scene.Remove(After);
                Scene.Add(Source);
            }
        }
    }

    /// <summary>
    /// Standard clip types.
    /// </summary>
    public static class ClipType
    {
        /// <summary>
        /// <see cref="Type"/> of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly Type Video = typeof(VideoFile);
        /// <summary>
        /// <see cref="Type"/> of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly Type Audio = typeof(AudioObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly Type Image = typeof(ImageFile);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Text"/> class.
        /// </summary>
        public static readonly Type Text = typeof(Text);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Figure"/> class.
        /// </summary>
        public static readonly Type Figure = typeof(Figure);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Polygon"/> class.
        /// </summary>
        public static readonly Type Polygon = typeof(Polygon);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.RoundRect"/> class.
        /// </summary>
        public static readonly Type RoundRect = typeof(RoundRect);
        /// <summary>
        /// <see cref="Type"/> of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly Type Camera = typeof(CameraObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly Type GL3DObject = typeof(GL3DObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly Type Scene = typeof(SceneObject);
        /// <summary>
        /// Metadata of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata VideoMetadata = new(Resources.Video, () => new VideoFile());
        /// <summary>
        /// Metadata of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata AudioMetadata = new(Resources.Audio, () => new AudioObject());
        /// <summary>
        /// Metadata of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata ImageMetadata = new(Resources.Image, () => new ImageFile());
        /// <summary>
        /// Metadata of <see cref="Primitive.Objects.Text"/> class.
        /// </summary>
        public static readonly ObjectMetadata TextMetadata = new(Resources.Text, () => new Text());
        /// <summary>
        /// Metadata of <see cref="Primitive.Objects.Figure"/> class.
        /// </summary>
        public static readonly ObjectMetadata FigureMetadata = new(Resources.Figure, () => new Figure());
        /// <summary>
        /// Metadata of <see cref="Primitive.Objects.Polygon"/> class.
        /// </summary>
        public static readonly ObjectMetadata PolygonMetadata = new("Polygon", () => new Polygon());
        /// <summary>
        /// Metadata of <see cref="Primitive.Objects.RoundRect"/> class.
        /// </summary>
        public static readonly ObjectMetadata RoundRectMetadata = new("RoundRect", () => new RoundRect());
        /// <summary>
        /// Metadata of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata CameraMetadata = new(Resources.Camera, () => new CameraObject());
        /// <summary>
        /// Metadata of <see cref="Primitive.Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata GL3DObjectMetadata = new(Resources._3DObject, () => new GL3DObject());
        /// <summary>
        /// Metadata of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata SceneMetadata = new(Resources.Scene, () => new SceneObject());
    }
}
