// EffectElement.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a base class of the effect.
    /// </summary>
    public abstract class EffectElement : EditingObject, IChild<ClipElement>, IParent<PropertyElement>, ICloneable
    {
        private static readonly PropertyChangedEventArgs _isEnabledArgs = new(nameof(IsEnabled));
        private static readonly PropertyChangedEventArgs _isExpandedArgs = new(nameof(IsExpanded));
        private bool _isEnabled = true;
        private bool _isExpanded = true;
        private ClipElement? _parent;
        private IEnumerable<PropertyElement>? _cachedList;

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _cachedList ??= GetProperties().ToArray();

        /// <summary>
        /// Gets the name of this <see cref="EffectElement"/>.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Gets or sets if this <see cref="EffectElement"/> is enabled.
        /// </summary>
        /// <remarks><see langword="true"/> if the <see cref="EffectElement"/> is enabled or <see langword="false"/> otherwise.</remarks>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetAndRaise(value, ref _isEnabled, _isEnabledArgs);
        }

        /// <summary>
        /// Gets or sets whether the expander is open.
        /// </summary>
        /// <remarks><see langword="true"/> if the expander is open, otherwise <see langword="false"/>.</remarks>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetAndRaise(value, ref _isExpanded, _isExpandedArgs);
        }

        /// <inheritdoc/>
        public ClipElement Parent
        {
            get => _parent!;
            set
            {
                _parent = value;

                foreach (var prop in Children)
                {
                    prop.Parent = this;
                }
            }
        }

        /// <summary>
        /// Gets the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        /// <returns>Returns the <see cref="PropertyElement"/> to display on the GUI.</returns>
        public abstract IEnumerable<PropertyElement> GetProperties();

        /// <inheritdoc/>
        public object Clone()
        {
            return this.DeepClone()!;
        }

        /// <summary>
        /// Apply the effect.
        /// </summary>
        /// <param name="args">The data used to apply the effect.</param>
        public abstract void Apply(EffectApplyArgs args);

        /// <summary>
        /// It will be called before applying.
        /// </summary>
        /// <param name="args">The data used to apply the effect.</param>
        public virtual void PreviewApply(EffectApplyArgs args)
        {
        }

        /// <summary>
        /// Create a command to change whether the <see cref="EffectElement"/> is enabled.
        /// </summary>
        /// <param name="value">New value for <see cref="IsEnabled"/>.</param>
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

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteBoolean(nameof(IsEnabled), IsEnabled);
            writer.WriteBoolean(nameof(IsExpanded), IsExpanded);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            IsEnabled = element.GetProperty(nameof(IsEnabled)).GetBoolean();
            IsExpanded = element.GetProperty(nameof(IsExpanded)).GetBoolean();
        }

        /// <summary>
        /// クリップからエフェクトを削除するコマンドを表します.
        /// </summary>
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private readonly EffectElement _effect;
            private readonly int _index;

            /// <summary>
            /// Initializes a new instance of the <see cref="RemoveCommand"/> class.
            /// </summary>
            /// <param name="effect">削除するエフェクトです.</param>
            /// <param name="clip">削除するエフェクトを含むクリップです.</param>
            public RemoveCommand(EffectElement effect, ClipElement clip)
            {
                _effect = effect;
                _clip = clip;
                _index = _clip.Effect.IndexOf(effect);
            }

            /// <inheritdoc/>
            public string Name => Strings.RemoveEffect;

            /// <inheritdoc/>
            public void Do()
            {
                _clip.Effect.RemoveAt(_index);
                _effect.Unload();
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                _effect.Load();
                _clip.Effect.Insert(_index, _effect);
            }
        }

        /// <summary>
        /// クリップにエフェクトを追加するコマンドを表します.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private readonly EffectElement? _effect;

            /// <summary>
            /// Initializes a new instance of the <see cref="AddCommand"/> class.
            /// </summary>
            /// <param name="effect">追加するエフェクトです.</param>
            /// <param name="clip">エフェクトを追加するクリップです.</param>
            public AddCommand(EffectElement effect, ClipElement clip)
            {
                _effect = effect;
                _clip = clip;
                effect.Parent = clip;
                if (!((ObjectElement)_clip.Effect[0]).EffectFilter(effect))
                {
                    _effect = null;
                }
            }

            /// <inheritdoc/>
            public string Name => Strings.AddEffect;

            /// <inheritdoc/>
            public void Do()
            {
                if (_effect is not null)
                {
                    _effect.Load();
                    _clip.Effect.Add(_effect);
                }
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                if (_effect is not null)
                {
                    _clip.Effect.Remove(_effect);
                    _effect.Unload();
                }
            }
        }

        /// <summary>
        /// 空のエフェクトを表します.
        /// </summary>
        internal class EmptyClass : ObjectElement
        {
            /// <inheritdoc/>
            public override string Name => "Empty";

            /// <inheritdoc/>
            public override IEnumerable<PropertyElement> GetProperties()
            {
                return Enumerable.Empty<PropertyElement>();
            }

            /// <inheritdoc/>
            public override void Apply(EffectApplyArgs args)
            {
            }
        }

        private sealed class CheckCommand : IRecordCommand
        {
            private readonly WeakReference<EffectElement> _effect;
            private readonly bool _value;

            public CheckCommand(EffectElement effect, bool value)
            {
                _effect = new(effect);
                _value = value;
            }

            public string Name => Strings.EnableDisableEffect;

            public void Do()
            {
                if (_effect.TryGetTarget(out var target))
                {
                    target.IsEnabled = _value;
                }
            }

            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                if (_effect.TryGetTarget(out var target))
                {
                    target.IsEnabled = !_value;
                }
            }
        }

        private sealed class UpCommand : IRecordCommand
        {
            private readonly WeakReference<ClipElement> _clip;
            private readonly WeakReference<EffectElement> _effect;

            public UpCommand(EffectElement effect)
            {
                if (effect.Parent is null) throw new ArgumentException("effect.Parent is null");

                _effect = new(effect);
                _clip = new(effect.Parent);
            }

            public string Name => Strings.UpEffect;

            public void Do()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更前のインデックス
                    var index = clip.Effect.IndexOf(effect);

                    if (index != 1)
                    {
                        clip.Effect.Move(index, index - 1);
                    }
                }
            }

            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更後のインデックス
                    var index = clip.Effect.IndexOf(effect);

                    if (index != clip.Effect.Count - 1)
                    {
                        clip.Effect.Move(index, index + 1);
                    }
                }
            }
        }

        private sealed class DownCommand : IRecordCommand
        {
            private readonly WeakReference<ClipElement> _clip;
            private readonly WeakReference<EffectElement> _effect;

            public DownCommand(EffectElement effect)
            {
                if (effect.Parent is null) throw new ArgumentException("effect.Parent is null");

                _effect = new(effect);
                _clip = new(effect.Parent);
            }

            public string Name => Strings.DownEffect;

            public void Do()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更前のインデックス
                    var index = clip.Effect.IndexOf(effect);

                    if (index != clip.Effect.Count - 1)
                    {
                        clip.Effect.Move(index, index + 1);
                    }
                }
            }

            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更後のインデックス
                    var index = clip.Effect.IndexOf(effect);

                    if (index != 1)
                    {
                        clip.Effect.Move(index, index - 1);
                    }
                }
            }
        }
    }
}