using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.Diagnostics.CodeAnalysis;
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
    /// Represents the data of a clip to be placed in the timeline.
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
        public ClipData(int id, ObservableCollection<EffectElement> effects, int start, int end, Type type, int layer, Scene scene)
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
        /// It is called at rendering time
        /// </summary>
        public void Render(ClipRenderArgs args)
        {
            var loadargs = new EffectRenderArgs(args.Frame, args.Type);

            if (Effect[0] is ObjectElement obj)
            {
                if (!obj.IsEnabled) return;

                obj.Render(loadargs);
            }
        }
        /// <summary>
        /// It will be called before rendering.
        /// </summary>
        public void PreviewRender(ClipRenderArgs args)
        {
            var enableEffects = Effect.Where(x => x.IsEnabled);
            var loadargs = new EffectRenderArgs(args.Frame, args.Type);

            foreach (var item in enableEffects)
            {
                item.PreviewRender(loadargs);
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
        public object Clone()
        {
            var clip = this.DeepClone()!;

            clip.Parent = Parent;
            clip.Load();

            return clip;
        }

        /// <inheritdoc/>
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

        #endregion


        /// <summary>
        /// Represents a command that adds <see cref="ClipData"/> to a <see cref="Data.Scene"/>.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            public ClipData Clip;

            /// <summary>
            /// <see cref="AddCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="startFrame"/> is less than 0.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is less than 0</exception>
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

            /// <inheritdoc/>
            public void Do()
            {
                Clip.Load();
                Scene.Add(Clip);
                Scene.SetCurrentClip(Clip);
            }
            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }
            /// <inheritdoc/>
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
        /// <summary>
        /// Represents a command to remove <see cref="ClipData"/> from a <see cref="Data.Scene"/>
        /// </summary>
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;

            /// <summary>
            /// <see cref="RemoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="clip">The target <see cref="ClipData"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            public RemoveCommand(ClipData clip) => _Clip = clip ?? throw new ArgumentNullException(nameof(clip));

            public string Name => CommandName.RemoveClip;

            /// <inheritdoc/>
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
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                _Clip.Load();
                _Clip.Parent.Add(_Clip);
            }
        }
        /// <summary>
        /// Represents a command to move <see cref="ClipData"/> frames and layers.
        /// </summary>
        internal sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly Frame _ToFrame;
            private readonly Frame _FromFrame;
            private readonly int _ToLayer;
            private readonly int _FromLayer;
            private Scene Scene => _Clip.Parent;

            #region コンストラクタ
            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="toFrame"/> or <paramref name="toLayer"/> is less than 0.</exception>
            public MoveCommand(ClipData clip, Frame toFrame, int toLayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                _FromFrame = clip.Start;
                _ToLayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                _FromLayer = clip.Layer;
            }

            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> is less than 0.</exception>
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

            /// <inheritdoc/>
            public void Do()
            {
                _Clip.MoveTo(_ToFrame);

                _Clip.Layer = _ToLayer;


                if (_Clip.End > Scene.TotalFrame)
                {
                    Scene.TotalFrame = _Clip.End;
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                _Clip.MoveTo(_FromFrame);

                _Clip.Layer = _FromLayer;
            }
        }
        /// <summary>
        /// Represents a command to change the length of <see cref="ClipData"/>.
        /// </summary>
        internal sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly Frame _Start;
            private readonly Frame _End;
            private readonly Frame _OldStart;
            private readonly Frame _OldEnd;

            /// <summary>
            /// <see cref="LengthChangeCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
            public LengthChangeCommand(ClipData clip, Frame start, Frame end)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _Start = (Frame.Zero > start) ? throw new ArgumentOutOfRangeException(nameof(start)) : start;
                _End = (Frame.Zero > end) ? throw new ArgumentOutOfRangeException(nameof(end)) : end;
                _OldStart = clip.Start;
                _OldEnd = clip.End;
            }

            public string Name => CommandName.ChangeLength;

            /// <inheritdoc/>
            public void Do()
            {
                _Clip.Start = _Start;
                _Clip.End = _End;
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                _Clip.Start = _OldStart;
                _Clip.End = _OldEnd;
            }
        }
        internal sealed class SparateCommand : IRecordCommand
        {
            public readonly ClipData Before;
            public readonly ClipData After;
            private readonly ClipData Source;
            private readonly Scene Scene;

            public SparateCommand(ClipData clip, Frame frame)
            {
                Source = clip;
                Scene = clip.Parent;
                Before = (ClipData)clip.Clone();
                After = (ClipData)clip.Clone();

                Before.End = frame;
                After.Start = frame;
            }

            public string Name => CommandName.SparateClip;

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

    public static class ClipType
    {
        public static readonly Type Video = typeof(VideoFile);
        public static readonly Type Audio = typeof(AudioObject);
        public static readonly Type Image = typeof(ImageFile);
        public static readonly Type Text = typeof(Text);
        public static readonly Type Figure = typeof(Figure);
        public static readonly Type Polygon = typeof(Polygon);
        public static readonly Type RoundRect = typeof(RoundRect);
        public static readonly Type Camera = typeof(CameraObject);
        public static readonly Type GL3DObject = typeof(GL3DObject);
        public static readonly Type Scene = typeof(SceneObject);
        public static readonly ObjectMetadata VideoMetadata = new(Resources.Video, () => new VideoFile());
        public static readonly ObjectMetadata AudioMetadata = new(Resources.Audio, () => new AudioObject());
        public static readonly ObjectMetadata ImageMetadata = new(Resources.Image, () => new ImageFile());
        public static readonly ObjectMetadata TextMetadata = new(Resources.Text, () => new Text());
        public static readonly ObjectMetadata FigureMetadata = new(Resources.Figure, () => new Figure());
        public static readonly ObjectMetadata PolygonMetadata = new("Polygon", () => new Polygon());
        public static readonly ObjectMetadata RoundRectMetadata = new("RoundRect", () => new RoundRect());
        public static readonly ObjectMetadata CameraMetadata = new(Resources.Camera, () => new CameraObject());
        public static readonly ObjectMetadata GL3DObjectMetadata = new(Resources._3DObject, () => new GL3DObject());
        public static readonly ObjectMetadata SceneMetadata = new(Resources.Scene, () => new SceneObject());
    }
}
