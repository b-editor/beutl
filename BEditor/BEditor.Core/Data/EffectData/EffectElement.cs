using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Data.EffectData.DefaultCommon;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.EffectData
{
    /// <summary>
    /// Represents the base class of the effect.
    /// </summary>
    [DataContract(Namespace = "")]
    public abstract class EffectElement : ComponentObject, IChild<ClipData>, IParent<PropertyElement>, ICloneable
    {
        private bool isEnabled = true;
        private bool isExpanded = true;
        private ClipData clipData;
        private IEnumerable<PropertyElement> cachedlist;
        
        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => cachedlist ??= Properties;
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
            get => isEnabled;
            set => SetValue(value, ref isEnabled, nameof(IsEnabled));
        }
        /// <summary>
        /// Get or set whether the expander is open.
        /// </summary>
        /// <remarks><see langword="true"/> if the expander is open, otherwise <see langword="false"/>.</remarks>
        [DataMember]
        public bool IsExpanded
        {
            get => isExpanded;
            set => SetValue(value, ref isExpanded, nameof(IsExpanded));
        }
        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <inheritdoc/>
        public virtual ClipData Parent
        {
            get => clipData;
            internal set
            {
                clipData = value;

                Parallel.ForEach(Children, property => property.Parent = this);
            }
        }

        /// <inheritdoc/>
        public object Clone()
        {
            return this.DeepClone();
        }

        /// <summary>
        /// Called after the constructor or after deserialization.
        /// </summary>
        public virtual void PropertyLoaded()
        {
            Parallel.ForEach(Children, p => p.PropertyLoaded());

            var attributetype = typeof(PropertyMetadataAttribute);
            var type = GetType();
            var properties = type.GetProperties();

            Parallel.ForEach(properties, property =>
            {
                //metadata属性の場合&プロパティがPropertyElement
                if (Attribute.GetCustomAttribute(property, attributetype) is PropertyMetadataAttribute metadata &&
                                    property.GetValue(this) is PropertyElement propertyElement)
                {

                    propertyElement.PropertyMetadata = metadata.PropertyMetadata;
                }
            });
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
        /// Represents a command that changes the boolean for which an effect is enabled.
        /// </summary>
        public sealed class CheckCommand : IUndoRedoCommand
        {
            private readonly EffectElement effect;
            private readonly bool value;

            /// <summary>
            /// <see cref="CheckCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <param name="value">New value</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public CheckCommand(EffectElement effect, bool value)
            {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                this.value = value;
            }

            /// <inheritdoc/>
            public void Do() => effect.IsEnabled = value;
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => effect.IsEnabled = !value;
        }
        /// <summary>
        /// Represents a command that changes the order of the effects.
        /// </summary>
        public sealed class UpCommand : IUndoRedoCommand
        {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="UpCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public UpCommand(EffectElement effect)
            {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                data = effect.Parent;
            }


            /// <inheritdoc/>
            public void Do()
            {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1)
                {
                    data.Effect.Move(index, index - 1);
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1)
                {
                    data.Effect.Move(index, index + 1);
                }
            }
        }
        /// <summary>
        /// Represents a command that changes the order of the effects.
        /// </summary>
        public sealed class DownCommand : IUndoRedoCommand
        {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="DownCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public DownCommand(EffectElement effect)
            {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                data = effect.Parent;
            }


            /// <inheritdoc/>
            public void Do()
            {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != data.Effect.Count() - 1)
                {
                    data.Effect.Move(index, index + 1);
                }
            }
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                //変更前のインデックス
                int index = data.Effect.IndexOf(effect);

                if (index != 1)
                {
                    data.Effect.Move(index, index - 1);
                }
            }
        }
        /// <summary>
        /// Represents a command that removes the effect from the parent element.
        /// </summary>
        public sealed class RemoveCommand : IUndoRedoCommand
        {
            private readonly ClipData data;
            private readonly EffectElement effect;
            private readonly int index;

            /// <summary>
            /// <see cref="RemoveCommand"/> Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public RemoveCommand(EffectElement effect)
            {
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                this.data = effect.Parent;
                index = data.Effect.IndexOf(effect);
            }

            /// <inheritdoc/>
            public void Do() => data.Effect.RemoveAt(index);
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => data.Effect.Insert(index, effect);
        }
        /// <summary>
        /// Represents a command to add an effect.
        /// </summary>
        public sealed class AddCommand : IUndoRedoCommand
        {
            private readonly ClipData data;
            private readonly EffectElement effect;

            /// <summary>
            /// <see cref="AddCommand"/>Initialize a new instance of the class.
            /// </summary>
            /// <param name="effect">The target <see cref="EffectElement"/>.</param>
            /// <exception cref="ArgumentException">effect.ClipData is null.</exception>
            /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
            public AddCommand(EffectElement effect)
            {
                if (effect.Parent is null) throw new ArgumentException("effect.ClipData is null", nameof(effect));
                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));

                this.data = effect.Parent;

                effect.PropertyLoaded();
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

                this.effect = effect ?? throw new ArgumentNullException(nameof(effect));
                this.data = clip;
                effect.Parent = clip;

                effect.PropertyLoaded();
            }

            /// <inheritdoc/>
            public void Do() => data.Effect.Add(effect);
            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo() => data.Effect.Remove(effect);
        }
    }

    public class EffectData
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public List<EffectData> Children { get; set; }

        public static ObservableCollection<EffectData> LoadedEffects { get; } = new ObservableCollection<EffectData> {
            new() {
                Name = Resources.Effects,
                Children = new() {
                    new() { Name = Resources.Border, Type = typeof(Border) },
                    new() { Name = Resources.ColorKey, Type = typeof(ColorKey) },
                    new() { Name = Resources.DropShadow, Type = typeof(Shadow) },
                    new() { Name = Resources.Blur, Type = typeof(Blur) },
                    new() { Name = Resources.Monoc, Type = typeof(Monoc) },
                    new() { Name = Resources.Dilate, Type = typeof(Dilate) },
                    new() { Name = Resources.Erode, Type = typeof(Erode) },
                    new() { Name = Resources.Clipping, Type = typeof(Clipping) },
                    new() { Name = Resources.AreaExpansion, Type = typeof(AreaExpansion) }
                }
            },
            new() {
                Name = Resources.Camera,
                Children = new() {
                    new() { Name = Resources.DepthTest, Type = typeof(DepthTest) },
                    new() { Name = Resources.DirectionalLightSource, Type = typeof(DirectionalLightSource) },
                    new() { Name = Resources.PointLightSource, Type = typeof(PointLightSource) },
                    new() { Name = Resources.SpotLight, Type = typeof(SpotLight) }
                }
            }
        };
    }
}
