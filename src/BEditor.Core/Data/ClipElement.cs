using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;

using BEditor.Command;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data of a clip to be placed in the timeline.
    /// </summary>
    public class ClipElement : EditorObject, ICloneable, IParent<EffectElement>, IChild<Scene>, IHasName, IHasId, IFormattable, IElementObject, IJsonObject
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _startArgs = new(nameof(Start));
        private static readonly PropertyChangedEventArgs _endArgs = new(nameof(End));
        private static readonly PropertyChangedEventArgs _layerArgs = new(nameof(Layer));
        private static readonly PropertyChangedEventArgs _textArgs = new(nameof(LabelText));
        private string? _name;
        private Frame _start;
        private Frame _end;
        private int _layer;
        private string _labelText = "";
        private WeakReference<Scene?>? _parent;
        #endregion

        #region Contructor

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipElement"/> class.
        /// </summary>
        public ClipElement(int id, ObservableCollection<EffectElement> effects, Frame start, Frame end, int layer, Scene scene)
        {
            Id = id;
            _start = start;
            _end = end;
            _layer = layer;
            Effect = effects;
            Parent = scene;
            LabelText = Name;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the ID for this <see cref="ClipElement"/>
        /// </summary>
        public int Id { get; private set; }

        /// <summary>
        /// Gets the name of this <see cref="ClipElement"/>.
        /// </summary>
        public string Name => _name ??= $"{Effect[0].GetType().Name}{Id}";

        /// <summary>
        /// Gets or sets the start frame for this <see cref="ClipElement"/>.
        /// </summary>
        public Frame Start
        {
            get => _start;
            set => SetValue(value, ref _start, _startArgs);
        }

        /// <summary>
        /// Gets or sets the end frame for this <see cref="ClipElement"/>.
        /// </summary>
        public Frame End
        {
            get => _end;
            set => SetValue(value, ref _end, _endArgs);
        }

        /// <summary>
        /// Gets the length of this <see cref="ClipElement"/>.
        /// </summary>
        public Frame Length => End - Start;

        /// <summary>
        /// Gets or sets the layer where this <see cref="ClipElement"/> will be placed.
        /// </summary>
        public int Layer
        {
            get => _layer;
            set
            {
                if (value == 0) return;
                SetValue(value, ref _layer, _layerArgs);
            }
        }

        /// <summary>
        /// Gets or sets the character displayed in this <see cref="ClipElement"/>.
        /// </summary>
        public string LabelText
        {
            get => _labelText;
            set => SetValue(value, ref _labelText, _textArgs);
        }

        /// <inheritdoc/>
        public Scene Parent
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

        /// <summary>
        /// Gets the effects included in this <see cref="ClipElement"/>.
        /// </summary>
        public ObservableCollection<EffectElement> Effect { get; private set; }

        /// <inheritdoc/>
        public IEnumerable<EffectElement> Children => Effect;

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
            catch (Exception e)
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
        object ICloneable.Clone()
        {
            return Clone();
        }

        /// <inheritdoc cref="ICloneable.Clone"/>
        public ClipElement Clone()
        {
            var clip = this.DeepClone()!;

            clip.Parent = Parent;
            clip.Load();

            return clip;
        }

        /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        public string ToString(string? format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

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

        /// <summary>
        /// Get the clip from its full name.
        /// </summary>
        public static ClipElement? FromFullName(string name, Project? project)
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


            return new EffectElement.AddCommand(effect, this);
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
        {
            return new MoveCommand(this, toFrame, toLayer);
        }

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
        {
            return new MoveCommand(this, to, from, tolayer, fromlayer);
        }

        /// <summary>
        /// Create a command to change the length of this clip.
        /// </summary>
        /// <param name="start">New start frame for this <see cref="ClipElement"/>.</param>
        /// <param name="end">New end frame for this <see cref="ClipElement"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand ChangeLength(Frame start, Frame end)
        {
            return new LengthChangeCommand(this, start, end);
        }

        /// <summary>
        /// Create a command to split this clip at the specified frame.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand Split(Frame frame)
        {
            return new SplitCommand(this, frame);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Id), Id);
            writer.WriteNumber(nameof(Start), Start);
            writer.WriteNumber(nameof(End), End);
            writer.WriteNumber(nameof(Layer), Layer);
            writer.WriteString("Text", LabelText);
            writer.WriteStartArray("Effects");
            {
                foreach (var effect in Effect)
                {
                    writer.WriteStartObject();
                    {
                        var type = effect.GetType();
                        writer.WriteString("_type", type.FullName + ", " + type.Assembly.GetName().Name);
                        effect.GetObjectData(writer);
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Id = element.GetProperty(nameof(Id)).GetInt32();
            Start = element.GetProperty(nameof(Start)).GetInt32();
            End = element.GetProperty(nameof(End)).GetInt32();
            Layer = element.GetProperty(nameof(Layer)).GetInt32();
            LabelText = element.GetProperty("Text").GetString() ?? "";
            var effects = element.GetProperty("Effects");
            Effect = new();
            foreach (var effect in effects.EnumerateArray())
            {
                var typeName = effect.GetProperty("_type").GetString() ?? "";
                if (Type.GetType(typeName) is var type && type is not null)
                {
                    var obj = (EffectElement)FormatterServices.GetUninitializedObject(type);
                    obj.SetObjectData(effect);

                    Effect.Add(obj);
                }
            }
        }

        #endregion

        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            public ClipElement Clip;

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
                Clip = new ClipElement(idmax, list, startFrame, startFrame + 180, layer, scene);
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
                if (Scene.SelectItems.Contains(Clip))
                {
                    Scene.SelectItems.Remove(Clip);

                    if (Scene.SelectItem == Clip)
                    {
                        Scene.SelectItem = null;
                    }
                }
            }
        }
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;

            public RemoveCommand(ClipElement clip)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
            }

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
                    if (_Clip.Parent.SelectItems.Contains(_Clip))
                    {
                        _Clip.Parent.SelectItems.Remove(_Clip);

                        if (_Clip.Parent.SelectItem == _Clip)
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
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                _Clip.Load();
                _Clip.Parent.Add(_Clip);
            }
        }
        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;
            private readonly Frame _ToFrame;
            private readonly Frame _FromFrame;
            private readonly int _ToLayer;
            private readonly int _FromLayer;
            private Scene Scene => _Clip.Parent;

            #region コンストラクタ
            public MoveCommand(ClipElement clip, Frame toFrame, int toLayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                _FromFrame = clip.Start;
                _ToLayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                _FromLayer = clip.Layer;
            }
            public MoveCommand(ClipElement clip, Frame to, Frame from, int tolayer, int fromlayer)
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
            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _Clip.MoveTo(_FromFrame);

                _Clip.Layer = _FromLayer;
            }
        }
        private sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;
            private readonly Frame _Start;
            private readonly Frame _End;
            private readonly Frame _OldStart;
            private readonly Frame _OldEnd;

            public LengthChangeCommand(ClipElement clip, Frame start, Frame end)
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
            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _Clip.Start = _OldStart;
                _Clip.End = _OldEnd;
            }
        }
        private sealed class SplitCommand : IRecordCommand
        {
            public readonly ClipElement Before;
            public readonly ClipElement After;
            private readonly ClipElement Source;
            private readonly Scene Scene;

            public SplitCommand(ClipElement clip, Frame frame)
            {
                Source = clip;
                Scene = clip.Parent;
                Before = clip.Clone();
                After = clip.Clone();

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
}
