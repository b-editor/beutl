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
using BEditor.Core.Data.Property;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a base class of the effect.
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

        /// <inheritdoc cref="IElementObject.Load"/>
        protected virtual void OnLoad()
        {

        }
        /// <inheritdoc cref="IElementObject.Unload"/>
        protected virtual void OnUnload()
        {

        }

        /// <summary>
        /// Create a command to change whether the <see cref="EffectElement"/> is enabled.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeIsEnabled(bool value) => new CheckCommand(this, value);
        /// <summary>
        /// Create a command to bring the order of this <see cref="EffectElement"/> forward.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand BringForward() => new UpCommand(this);
        /// <summary>
        /// Create a command to send the order of this <see cref="EffectElement"/> backward.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand SendBackward() => new DownCommand(this);

        #endregion


        internal sealed class CheckCommand : IRecordCommand
        {
            private readonly EffectElement _Effect;
            private readonly bool _Value;

            public CheckCommand(EffectElement effect, bool value)
            {
                _Effect = effect;
                _Value = value;
            }

            public string Name => CommandName.EnableDisableEffect;

            public void Do() => _Effect.IsEnabled = _Value;
            public void Redo() => Do();
            public void Undo() => _Effect.IsEnabled = !_Value;
        }
        internal sealed class UpCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            public UpCommand(EffectElement effect)
            {
                _Effect = effect;
                _Clip = effect.Parent!;
            }

            public string Name => CommandName.UpEffect;

            public void Do()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != 1)
                {
                    _Clip.Effect.Move(index, index - 1);
                }
            }
            public void Redo() => Do();
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
        internal sealed class DownCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            public DownCommand(EffectElement effect)
            {
                _Effect = effect;
                _Clip = effect.Parent!;
            }

            public string Name => CommandName.DownEffect;

            public void Do()
            {
                //変更前のインデックス
                int index = _Clip.Effect.IndexOf(_Effect);

                if (index != _Clip.Effect.Count - 1)
                {
                    _Clip.Effect.Move(index, index + 1);
                }
            }
            public void Redo() => Do();
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
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;
            private readonly int _Indec;

            public RemoveCommand(EffectElement effect, ClipData clip)
            {
                _Effect = effect;
                _Clip = clip;
                _Indec = _Clip.Effect.IndexOf(effect);
            }

            public string Name => CommandName.RemoveEffect;

            public void Do()
            {
                _Clip.Effect.RemoveAt(_Indec);
                _Effect.Unload();
            }
            public void Redo() => Do();
            public void Undo()
            {
                _Effect.Load();
                _Clip.Effect.Insert(_Indec, _Effect);
            }
        }
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly ClipData _Clip;
            private readonly EffectElement _Effect;

            public AddCommand(EffectElement effect, ClipData clip)
            {
                _Effect = effect;
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
