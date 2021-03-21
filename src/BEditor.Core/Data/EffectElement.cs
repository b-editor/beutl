using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Dynamic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Property;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a base class of the effect.
    /// </summary>
    [DataContract]
    public abstract class EffectElement : EditorObject, IChild<ClipElement>, IParent<PropertyElement>, ICloneable, IHasId, IElementObject, IJsonObject
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _isEnabledArgs = new(nameof(IsEnabled));
        private static readonly PropertyChangedEventArgs _isExpandedArgs = new(nameof(IsExpanded));
        private bool _isEnabled = true;
        private bool _isExpanded = true;
        private WeakReference<ClipElement?>? _parent;
        private IEnumerable<PropertyElement>? _cachedList;
        #endregion

        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => _cachedList ??= Properties.ToArray();
        /// <summary>
        /// Gets the name of the <see cref="EffectElement"/>.
        /// </summary>
        public abstract string Name { get; }
        /// <summary>
        /// Gets or sets if the <see cref="EffectElement"/> is enabled.
        /// </summary>
        /// <remarks><see langword="true"/> if the <see cref="EffectElement"/> is enabled or <see langword="false"/> otherwise.</remarks>
        [DataMember]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetValue(value, ref _isEnabled, _isEnabledArgs);
        }
        /// <summary>
        /// Gets or sets whether the expander is open.
        /// </summary>
        /// <remarks><see langword="true"/> if the expander is open, otherwise <see langword="false"/>.</remarks>
        [DataMember]
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetValue(value, ref _isExpanded, _isExpandedArgs);
        }
        /// <summary>
        /// Gets the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <inheritdoc/>
        public ClipElement Parent
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
        public int Id => Parent?.Effect?.IndexOf(this) ?? -1;


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

        /// <inheritdoc/>
        public virtual void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteBoolean(nameof(IsEnabled), IsEnabled);
            writer.WriteBoolean(nameof(IsExpanded), IsExpanded);
            foreach (var item in GetType().GetProperties()
                .Where(i => Attribute.IsDefined(i, typeof(DataMemberAttribute)))
                .Select(i => (Info: i, Attribute: (DataMemberAttribute)Attribute.GetCustomAttribute(i, typeof(DataMemberAttribute))!))
                .Select(i => (Object: i.Info.GetValue(this), i)))
            {
                if (item.Object is IJsonObject json)
                {
                    writer.WriteStartObject(item.i.Attribute.Name ?? item.i.Info.Name);
                    json.GetObjectData(writer);
                    writer.WriteEndObject();
                }
            }
        }

        /// <inheritdoc/>
        public virtual void SetObjectData(JsonElement element)
        {
            IsEnabled = element.GetProperty(nameof(IsEnabled)).GetBoolean();
            IsExpanded = element.GetProperty(nameof(IsExpanded)).GetBoolean();

            foreach (var item in GetType().GetProperties()
                .Where(i => Attribute.IsDefined(i, typeof(DataMemberAttribute)))
                .Select(i => (Info: i, Attribute: (DataMemberAttribute)Attribute.GetCustomAttribute(i, typeof(DataMemberAttribute))!))
                .Where(i => i.Info.PropertyType.IsAssignableTo(typeof(IJsonObject))))
            {
                var property = element.GetProperty(item.Attribute.Name ?? item.Info.Name);
                var obj = (IJsonObject)FormatterServices.GetUninitializedObject(item.Info.PropertyType);
                obj.SetObjectData(property);
            }
        }

        #endregion


        internal sealed class CheckCommand : IRecordCommand
        {
            private readonly WeakReference<EffectElement> _effect;
            private readonly bool _value;

            public CheckCommand(EffectElement effect, bool value)
            {
                _effect = new(effect);
                _value = value;
            }

            public string Name => CommandName.EnableDisableEffect;

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
        internal sealed class UpCommand : IRecordCommand
        {
            private readonly WeakReference<ClipElement> _clip;
            private readonly WeakReference<EffectElement> _effect;

            public UpCommand(EffectElement effect)
            {
                _effect = new(effect);
                _clip = new(effect.Parent);
            }

            public string Name => CommandName.UpEffect;

            public void Do()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更前のインデックス
                    int index = clip.Effect.IndexOf(effect);

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
                    int index = clip.Effect.IndexOf(effect);

                    if (index != clip.Effect.Count - 1)
                    {
                        clip.Effect.Move(index, index + 1);
                    }
                }
            }
        }
        internal sealed class DownCommand : IRecordCommand
        {
            private readonly WeakReference<ClipElement> _clip;
            private readonly WeakReference<EffectElement> _effect;

            public DownCommand(EffectElement effect)
            {
                _effect = new(effect);
                _clip = new(effect.Parent);
            }

            public string Name => CommandName.DownEffect;

            public void Do()
            {
                if (_clip.TryGetTarget(out var clip) && _effect.TryGetTarget(out var effect))
                {
                    // 変更前のインデックス
                    int index = clip.Effect.IndexOf(effect);

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
                    int index = clip.Effect.IndexOf(effect);

                    if (index != 1)
                    {
                        clip.Effect.Move(index, index - 1);
                    }
                }
            }
        }
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private readonly EffectElement _effect;
            private readonly int _index;

            public RemoveCommand(EffectElement effect, ClipElement clip)
            {
                _effect = effect;
                _clip = clip;
                _index = _clip.Effect.IndexOf(effect);
            }

            public string Name => CommandName.RemoveEffect;

            public void Do()
            {
                _clip.Effect.RemoveAt(_index);
                _effect.Unload();
            }
            public void Redo() => Do();
            public void Undo()
            {
                _effect.Load();
                _clip.Effect.Insert(_index, _effect);
            }
        }
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private readonly EffectElement? _effect;

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

            public string Name => CommandName.AddEffect;

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
