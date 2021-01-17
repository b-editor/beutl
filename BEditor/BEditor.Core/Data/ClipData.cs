using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
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

        private static readonly PropertyChangedEventArgs startArgs = new(nameof(Start));
        private static readonly PropertyChangedEventArgs endArgs = new(nameof(End));
        private static readonly PropertyChangedEventArgs layerArgs = new(nameof(Layer));
        private static readonly PropertyChangedEventArgs textArgs = new(nameof(LabelText));
        private string name;
        private Frame start;
        private Frame end;
        private int layer;
        private string labeltext;

        #endregion


        #region Contructor

        /// <summary>
        /// <see cref="ClipData"/> Initialize a new instance of the class.
        /// </summary>
        public ClipData(int id, ObservableCollection<EffectElement> effects, int start, int end, Type type, int layer, Scene scene)
        {
            Id = id;
            this.start = start;
            this.end = end;
            Type = type;
            this.layer = layer;
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
        public string Name => name ??= $"{Type.Name}{Id}";

        /// <summary>
        /// Get the type of this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Name = "Type", Order = 1)]
        public string ClipType
        {
            get => Type.FullName;
            private set => Type = Type.GetType(value);
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
            get => start;
            set => SetValue(value, ref start, startArgs);
        }

        /// <summary>
        /// Get or set the end frame for this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 3)]
        public Frame End
        {
            get => end;
            set => SetValue(value, ref end, endArgs);
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
            get => layer;
            set
            {
                if (value == 0) return;
                SetValue(value, ref layer, layerArgs);
            }
        }

        /// <summary>
        /// Gets or sets the character displayed in this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Name = "Text", Order = 5)]
        public string LabelText
        {
            get => labeltext;
            set => SetValue(value, ref labeltext, textArgs);
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
            var clip = this.DeepClone();

            clip.Parent = Parent;
            clip.Loaded();

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
                "#" => $"[{Parent.Id}].{Name}",
                _ => throw new FormatException(string.Format("The {0} format string is not supported.", format))
            };
        }

        /// <inheritdoc/>
        public void Loaded()
        {
            if (IsLoaded) return;

            foreach (var effect in Effect)
            {
                effect.Parent = this;
                effect.Loaded();
            }

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unloaded()
        {
            if (!IsLoaded) return;

            foreach (var effect in Effect)
            {
                effect.Unloaded();
            }

            IsLoaded = false;
        }

        #endregion


        /// <summary>
        /// Represents a command that adds <see cref="ClipData"/> to a <see cref="Data.Scene"/>.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            public ClipData data;

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
                    (EffectElement)(metadata.CreateFunc?.Invoke() ?? Activator.CreateInstance(metadata.Type))
                };

                //オブジェクトの情報
                data = new ClipData(idmax, list, startFrame, startFrame + 180, metadata.Type, layer, scene);
            }

            public string Name => CommandName.AddClip;

            /// <inheritdoc/>
            public void Do()
            {
                data.Loaded();
                Scene.Add(data);
                Scene.SetCurrentClip(data);
            }
            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }
            /// <inheritdoc/>
            public void Undo()
            {
                Scene.Remove(data);
                data.Unloaded();

                //存在する場合
                if (Scene.SelectNames.Exists(x => x == data.Name))
                {
                    Scene.SelectItems.Remove(data);

                    if (Scene.SelectName == data.Name)
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
            private readonly ClipData data;

            /// <summary>
            /// <see cref="RemoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="clip">The target <see cref="ClipData"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            public RemoveCommand(ClipData clip) => this.data = clip ?? throw new ArgumentNullException(nameof(clip));

            public string Name => CommandName.RemoveClip;

            /// <inheritdoc/>
            public void Do()
            {
                if (!data.Parent.Remove(data))
                {
                    //Message.Snackbar("削除できませんでした");
                }
                else
                {
                    data.Unloaded();
                    //存在する場合
                    if (data.Parent.SelectNames.Exists(x => x == data.Name))
                    {
                        data.Parent.SelectItems.Remove(data);

                        if (data.Parent.SelectName == data.Name)
                        {
                            if (data.Parent.SelectItems.Count == 0)
                            {
                                data.Parent.SelectItem = null;
                            }
                            else
                            {
                                data.Parent.SelectItem = data.Parent.SelectItems[0];
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
                data.Loaded();
                data.Parent.Add(data);
            }
        }
        /// <summary>
        /// Represents a command to move <see cref="ClipData"/> frames and layers.
        /// </summary>
        internal sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipData data;
            private readonly Frame to;
            private readonly Frame from;
            private readonly int tolayer;
            private readonly int fromlayer;
            private Scene Scene => data.Parent;

            #region コンストラクタ
            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="toFrame"/> or <paramref name="toLayer"/> is less than 0.</exception>
            public MoveCommand(ClipData clip, Frame toFrame, int toLayer)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.to = (Frame.Zero > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                from = clip.Start;
                this.tolayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                fromlayer = clip.Layer;
            }

            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> is less than 0.</exception>
            public MoveCommand(ClipData clip, Frame to, Frame from, int tolayer, int fromlayer)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.to = (Frame.Zero > to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
                this.from = (Frame.Zero > from) ? throw new ArgumentOutOfRangeException(nameof(from)) : from;
                this.tolayer = (0 > tolayer) ? throw new ArgumentOutOfRangeException(nameof(tolayer)) : tolayer;
                this.fromlayer = (0 > fromlayer) ? throw new ArgumentOutOfRangeException(nameof(fromlayer)) : fromlayer;
            }
            #endregion

            public string Name => CommandName.MoveClip;

            /// <inheritdoc/>
            public void Do()
            {
                data.MoveTo(to);

                data.Layer = tolayer;


                if (data.End > Scene.TotalFrame)
                {
                    Scene.TotalFrame = data.End;
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                data.MoveTo(from);

                data.Layer = fromlayer;
            }
        }
        /// <summary>
        /// Represents a command to change the length of <see cref="ClipData"/>.
        /// </summary>
        internal sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipData data;
            private readonly Frame start;
            private readonly Frame end;
            private readonly Frame oldstart;
            private readonly Frame oldend;

            /// <summary>
            /// <see cref="LengthChangeCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
            public LengthChangeCommand(ClipData clip, Frame start, Frame end)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.start = (Frame.Zero > start) ? throw new ArgumentOutOfRangeException(nameof(start)) : start;
                this.end = (Frame.Zero > end) ? throw new ArgumentOutOfRangeException(nameof(end)) : end;
                oldstart = clip.Start;
                oldend = clip.End;
            }

            public string Name => CommandName.ChangeLength;

            /// <inheritdoc/>
            public void Do()
            {
                data.Start = start;
                data.End = end;
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                data.Start = oldstart;
                data.End = oldend;
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
                Before = (ClipData?)clip.Clone();
                After = (ClipData?)clip.Clone();

                Before.End = frame;
                After.Start = frame;
            }

            public string Name => CommandName.SparateClip;

            public void Do()
            {
                After.Loaded();
                Before.Loaded();

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
                Before.Unloaded();
                After.Unloaded();
                Source.Loaded();

                Scene.Remove(Before);
                Scene.Remove(After);
                Scene.Add(Source);
            }
        }
    }

    public static class ClipType
    {
        public static readonly Type Video = typeof(Video);
        public static readonly Type Audio = typeof(AudioObject);
        public static readonly Type Image = typeof(Image);
        public static readonly Type Text = typeof(Text);
        public static readonly Type Figure = typeof(Figure);
        public static readonly Type Polygon = typeof(Polygon);
        public static readonly Type RoundRect = typeof(RoundRect);
        public static readonly Type Camera = typeof(CameraObject);
        public static readonly Type GL3DObject = typeof(GL3DObject);
        public static readonly Type Scene = typeof(SceneObject);
        public static readonly ObjectMetadata VideoMetadata = new()
        {
            Name = Resources.Video,
            Type = Video,
            CreateFunc = () => new Primitive.Objects.Video()
        };
        public static readonly ObjectMetadata AudioMetadata = new()
        {
            Name = Resources.Audio,
            Type = Audio,
            CreateFunc = () => new Primitive.Objects.AudioObject()
        };
        public static readonly ObjectMetadata ImageMetadata = new()
        {
            Name = Resources.Image,
            Type = Image,
            CreateFunc = () => new Primitive.Objects.Image()
        };
        public static readonly ObjectMetadata TextMetadata = new()
        {
            Name = Resources.Text,
            Type = Text,
            CreateFunc = () => new Primitive.Objects.Text()
        };
        public static readonly ObjectMetadata FigureMetadata = new()
        {
            Name = Resources.Figure,
            Type = Figure,
            CreateFunc = () => new Primitive.Objects.Figure()
        };
        public static readonly ObjectMetadata PolygonMetadata = new()
        {
            Name = "Polygon",
            Type = Polygon,
            CreateFunc = () => new Primitive.Objects.Polygon()
        };
        public static readonly ObjectMetadata RoundRectMetadata = new()
        {
            Name = "RoundRect",
            Type = RoundRect,
            CreateFunc = () => new Primitive.Objects.RoundRect()
        };
        public static readonly ObjectMetadata CameraMetadata = new()
        {
            Name = Resources.Camera,
            Type = Camera,
            CreateFunc = () => new CameraObject()
        };
        public static readonly ObjectMetadata GL3DObjectMetadata = new()
        {
            Name = Resources._3DObject,
            Type = GL3DObject,
            CreateFunc = () => new GL3DObject()
        };
        public static readonly ObjectMetadata SceneMetadata = new()
        {
            Name = Resources.Scene,
            Type = Scene,
            CreateFunc = () => new Primitive.Objects.SceneObject()
        };
    }
}
