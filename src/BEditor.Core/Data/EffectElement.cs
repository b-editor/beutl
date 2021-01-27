using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the base class of the effect.
    /// </summary>
    [DataContract]
    public abstract class EffectElement : ComponentObject, IChild<ClipData>, IParent<PropertyElement>, ICloneable, IHasId, IElementObject
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _IsEnabledArgs = new(nameof(IsEnabled));
        private static readonly PropertyChangedEventArgs _IsExpandedArgs = new(nameof(IsExpanded));
        private bool _IsEnabled = true;
        private bool _IsExpanded = true;
        private ClipData? _Parent;
        private IEnumerable<PropertyElement>? _CachedList;
        #endregion

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _CachedList ??= Properties.ToArray();
        /// <summary>
        /// Get the name of the <see cref="EffectElement"/>.
        /// </summary>
        public abstract string Name { get; }
        /// <summary>
        /// Get or set if the <see cref="EffectElement"/> is enabled.
        /// </summary>
        /// <remarks><see langword="true"/> if the <see cref="EffectElement"/> is enabled or <see langword="false"/> otherwise.</remarks>
        [DataMember]
        public bool IsEnabled
        {
            get => _IsEnabled;
            set => SetValue(value, ref _IsEnabled, _IsEnabledArgs);
        }
        /// <summary>
        /// Get or set whether the expander is open.
        /// </summary>
        /// <remarks><see langword="true"/> if the expander is open, otherwise <see langword="false"/>.</remarks>
        [DataMember]
        public bool IsExpanded
        {
            get => _IsExpanded;
            set => SetValue(value, ref _IsExpanded, _IsExpandedArgs);
        }
        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <inheritdoc/>
        public ClipData? Parent
        {
            get => _Parent;
            internal set
            {
                _Parent = value;

                Parallel.ForEach(Children, property => property.Parent = this);
            }
        }
        /// <inheritdoc/>
        public int Id => Parent?.Effect?.IndexOf(this) ?? -1;
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }


        #region Methods

        /// <inheritdoc/>
        public object Clone()
        {
            return this.DeepClone()!;
        }

        /// <summary>
        /// It is called at rendering time
        /// </summary>
        public abstract void Render(EffectRenderArgs args);
        /// <summary>
        /// It will be called before rendering.
        /// </summary>
        public virtual void PreviewRender(EffectRenderArgs args) { }

        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            OnLoad();

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            OnUnload();

            IsLoaded = false;
        }

        protected virtual void OnLoad()
        {

        }
        protected virtual void OnUnload()
        {

        }

        /// <summary>
        /// Create a command to change whether the effect is enabled.
        /// </summary>
        [Pure]
        public IRecordCommand ChangeIsEnabled(bool value) => new CheckCommand(this, value);
        /// <summary>
        /// Create a command to bring the order of the effects forward.
        /// </summary>
        [Pure]
        public IRecordCommand BringForward() => new UpCommand(this);
        /// <summary>
        /// Create a command to send the order of the effects backward.
        /// </summary>
        [Pure]
        public IRecordCommand SendBackward() => new DownCommand(this);

        #endregion


        /// <summary>
        /// Represents a command that changes the boolean for which an effect is enabled.
        /// </summary>
        internal sealed class CheckCommand : IRecordCommand
        {
            private readonly EffectElement _Effect;
            private readonly bool _Value;

            /// <summary>
            /// <see cref="CheckCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <param name="value">New value</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public CheckCommand(EffectElement effect, bool value)
            {
                _Effect = effect ?? throw new ArgumentNullException(nameof(effect));
                _Value = value;
            }

            public string Name => CommandName.EnableDisableEffect;

            /// <inheritdoc/>
            public void Do() => _Effect.IsEnabled = _Value;
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => _Effect.IsEnabled = !_Value;
        }
        /// <summary>
        /// Represents a command that changes the order of the effects.
        /// </summary>
        internal sealed class UpCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            /// <summary>
            /// <see cref="UpCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public UpCommand(EffectElement effect)
            {
                this._Effect = effect ?? throw new ArgumentNullException(nameof(effect));
                _Clip = effect.Parent!;
            }

            public string Name => CommandName.UpEffect;

            /// <inheritdoc/>
            public void Do()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != 1)
                {
                    _Clip.Effect.Move(index, index - 1);
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != _Clip.Effect.Count - 1)
                {
                    _Clip.Effect.Move(index, index + 1);
                }
            }
        }
        /// <summary>
        /// Represents a command that changes the order of the effects.
        /// </summary>
        internal sealed class DownCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            /// <summary>
            /// <see cref="DownCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public DownCommand(EffectElement effect)
            {
                _Effect = effect ?? throw new ArgumentNullException(nameof(effect));
                _Clip = effect.Parent!;
            }

            public string Name => CommandName.DownEffect;

            /// <inheritdoc/>
            public void Do()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != _Clip.Effect.Count - 1)
                {
                    _Clip.Effect.Move(index, index + 1);
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != 1)
                {
                    _Clip.Effect.Move(index, index - 1);
                }
            }
        }
        /// <summary>
        /// Represents a command that removes the effect from the parent element.
        /// </summary>
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;
            private readonly int _Indec;

            /// <summary>
            /// <see cref="RemoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <param name="clip"></param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
            public RemoveCommand(EffectElement effect, ClipData clip)
            {
                _Effect = effect ?? throw new ArgumentNullException(nameof(effect));
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _Indec = _Clip.Effect.IndexOf(effect);
            }

            public string Name => CommandName.RemoveEffect;

            /// <inheritdoc/>
            public void Do()
            {
                _Clip.Effect.RemoveAt(_Indec);
                _Effect.Unload();
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                _Effect.Load();
                _Clip.Effect.Insert(_Indec, _Effect);
            }
        }
        /// <summary>
        /// Represents a command to add an effect.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            /// <summary>
            /// <see cref="AddCommand"/>Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentException">effect.ClipData is null.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public AddCommand(EffectElement effect)
            {
                if (effect.Parent is null) throw new ArgumentException("effect.ClipData is null", nameof(effect));
                _Effect = effect ?? throw new ArgumentNullException(nameof(effect));

                _Clip = effect.Parent;
                if (!((ObjectElement)_Clip.Effect[0]).EffectFilter(effect)) throw new NotSupportedException();
            }
            /// <summary>
            /// <see cref="AddCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <param name="clip"></param>
            /// <exception cref="ArgumentException">effect.ClipData is <see langword="null"/>.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public AddCommand(EffectElement effect, ClipData clip)
            {
                if (clip is null) throw new ArgumentNullException(nameof(clip));

                _Effect = effect ?? throw new ArgumentNullException(nameof(effect));
                _Clip = clip;
                effect.Parent = clip;
                if (!((ObjectElement)_Clip.Effect[0]).EffectFilter(effect)) throw new NotSupportedException();
            }

            public string Name => CommandName.AddEffect;

            /// <inheritdoc/>
            public void Do()
            {
                _Effect.Load();
                _Clip.Effect.Add(_Effect);
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                _Clip.Effect.Remove(_Effect);
                _Effect.Unload();
            }
        }
        [DataContract]
        internal class EmptyClass : ObjectElement
        {
            public override string Name => "Empty";
            public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

            public override void Render(EffectRenderArgs args)
            {

            }
        }
    }
}
