using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Service;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the data of a clip to be placed in the timeline.
    /// </summary>
    [DataContract(Namespace = "", Name = "Data")]
    public class ClipData : ComponentObject, ICloneable, IParent<EffectElement>, IChild<Scene>, IHadName, IHadId
    {
        #region Fields

        private static readonly PropertyChangedEventArgs startArgs = new(nameof(Start));
        private static readonly PropertyChangedEventArgs endArgs = new(nameof(End));
        private static readonly PropertyChangedEventArgs layerArgs = new(nameof(Layer));
        private static readonly PropertyChangedEventArgs textArgs = new(nameof(LabelText));
        private string name;
        private int start;
        private int end;
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
        public int Start
        {
            get => start;
            set => SetValue(value, ref start, startArgs);
        }

        /// <summary>
        /// Get or set the end frame for this <see cref="ClipData"/>.
        /// </summary>
        [DataMember(Order = 3)]
        public int End
        {
            get => end;
            set => SetValue(value, ref end, endArgs);
        }

        /// <summary>
        /// Get the length of this <see cref="ClipData"/>.
        /// </summary>
        public int Length => End - Start;

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
        IEnumerable<EffectElement> IParent<EffectElement>.Children => Effect;

        #endregion


        #region Methods

        /// <summary>
        /// It is called at rendering time
        /// </summary>
        public void Render(ClipRenderArgs args)
        {
            var loadargs = new EffectRenderArgs(args.Frame, Effect.Where(x => x.IsEnabled).ToList());

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
            var loadargs = new EffectRenderArgs(args.Frame, enableEffects.ToList());

            foreach (var item in enableEffects)
            {
                item.PreviewRender(loadargs);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public void PropertyLoaded()
        {
            Parallel.ForEach(Effect, effect =>
            {
                effect.Parent = this;
                effect.PropertyLoaded();
            });
        }

        internal void MoveFrame(int f)
        {
            Start += f;
            End += f;
        }
        internal void MoveTo(int start)
        {
            var length = Length;
            Start = start;
            End = length + start;
        }

        /// <inheritdoc/>
        public override string ToString() => $"(Name:{Name} Id:{Id} Start:{Start} End:{End})";
        /// <inheritdoc/>
        public object Clone() => this.DeepClone();

        #endregion


        /// <summary>
        /// Represents a command that adds <see cref="ClipData"/> to a <see cref="ProjectData.Scene"/>.
        /// </summary>
        public sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            private readonly int AddFrame;
            private readonly int AddLayer;
            private readonly Type Type;
            public ClipData data;

            /// <summary>
            /// <see cref="AddCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="type"/> が <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="startFrame"/> is less than 0.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="layer"/> is less than 0</exception>
            public AddCommand(Scene scene, int startFrame, int layer, Type type)
            {
                Scene = scene ?? throw new ArgumentNullException(nameof(scene));
                AddFrame = (0 > startFrame) ? throw new ArgumentOutOfRangeException(nameof(startFrame)) : startFrame;
                AddLayer = (0 > layer) ? throw new ArgumentOutOfRangeException(nameof(layer)) : layer;
                Type = type ?? throw new ArgumentNullException(nameof(type));
            }

            /// <inheritdoc/>
            public void Do()
            {
                //新しいidを取得
                int idmax = Scene.NewId;

                //描画情報
                ObservableCollection<EffectElement> list = new();


                EffectElement index0;
                if (Type.IsSubclassOf(typeof(ObjectElement)))
                {
                    index0 = (ObjectElement)Activator.CreateInstance(Type);
                }
                else
                {
                    throw new Exception();
                }

                list.Add(index0);

                //オブジェクトの情報
                data = new ClipData(idmax, list, AddFrame, AddFrame + 180, Type, AddLayer, Scene);

                Scene.Add(data);
                data.PropertyLoaded();

                Scene.SetCurrentClip(data);
            }
            /// <inheritdoc/>
            public void Redo()
            {
                Scene.Add(data);

                Scene.SetCurrentClip(data);
            }
            /// <inheritdoc/>
            public void Undo()
            {
                Scene.Remove(data);

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
        /// Represents a command to remove <see cref="ClipData"/> from a <see cref="ProjectData.Scene"/>
        /// </summary>
        public sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipData data;

            /// <summary>
            /// <see cref="RemoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="clip">The target <see cref="ClipData"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            public RemoveCommand(ClipData clip) => this.data = clip ?? throw new ArgumentNullException(nameof(clip));

            /// <inheritdoc/>
            public void Do()
            {
                if (!data.Parent.Remove(data))
                {
                    //Message.Snackbar("削除できませんでした");
                }
                else
                {
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
            public void Undo() => data.Parent.Add(data);
        }
        /// <summary>
        /// Represents a command to move <see cref="ClipData"/> frames and layers.
        /// </summary>
        public sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipData data;
            private readonly int to;
            private readonly int from;
            private readonly int tolayer;
            private readonly int fromlayer;
            private Scene Scene => data.Parent;

            #region コンストラクタ
            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="toFrame"/> or <paramref name="toLayer"/> is less than 0.</exception>
            public MoveCommand(ClipData clip, int toFrame, int toLayer)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.to = (0 > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                from = clip.Start;
                this.tolayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                fromlayer = clip.Layer;
            }

            /// <summary>
            /// <see cref="MoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> is less than 0.</exception>
            public MoveCommand(ClipData clip, int to, int from, int tolayer, int fromlayer)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.to = (0 > to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
                this.from = (0 > from) ? throw new ArgumentOutOfRangeException(nameof(from)) : from;
                this.tolayer = (0 > tolayer) ? throw new ArgumentOutOfRangeException(nameof(tolayer)) : tolayer;
                this.fromlayer = (0 > fromlayer) ? throw new ArgumentOutOfRangeException(nameof(fromlayer)) : fromlayer;
            }
            #endregion


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
        public sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipData data;
            private readonly int start;
            private readonly int end;
            private readonly int oldstart;
            private readonly int oldend;

            /// <summary>
            /// <see cref="LengthChangeCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
            public LengthChangeCommand(ClipData clip, int start, int end)
            {
                this.data = clip ?? throw new ArgumentNullException(nameof(clip));
                this.start = (0 > start) ? throw new ArgumentOutOfRangeException(nameof(start)) : start;
                this.end = (0 > end) ? throw new ArgumentOutOfRangeException(nameof(end)) : end;
                oldstart = clip.Start;
                oldend = clip.End;
            }

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
    }

    public static class ClipType
    {
        public static readonly Type Video = typeof(Primitive.Objects.PrimitiveImages.Video);
        public static readonly Type Image = typeof(Primitive.Objects.PrimitiveImages.Image);
        public static readonly Type Text = typeof(Primitive.Objects.PrimitiveImages.Text);
        public static readonly Type Figure = typeof(Primitive.Objects.PrimitiveImages.Figure);
        public static readonly Type Camera = typeof(CameraObject);
        public static readonly Type GL3DObject = typeof(GL3DObject);
        public static readonly Type Scene = typeof(Primitive.Objects.PrimitiveImages.Scene);
    }
}
