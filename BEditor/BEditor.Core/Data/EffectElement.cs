using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Data.Primitive.Effects.PrimitiveImages;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the base class of the effect.
    /// </summary>
    [DataContract]
    public abstract class EffectElement : ComponentObject, IChild<ClipData>, IParent<PropertyElement>, ICloneable, IHasId, IElementObject
    {
        #region Fields

        private static readonly PropertyChangedEventArgs isEnabledArgs = new(nameof(IsEnabled));
        private static readonly PropertyChangedEventArgs isExpandedArgs = new(nameof(IsExpanded));
        private bool isEnabled = true;
        private bool isExpanded = true;
        private ClipData clipData;
        private IEnumerable<PropertyElement> cachedlist;

        #endregion


        /// <inheritdoc/>
        public IEnumerable<PropertyElement> Children => cachedlist ??= Properties.ToArray();
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
            set => SetValue(value, ref isEnabled, isEnabledArgs);
        }
        /// <summary>
        /// Get or set whether the expander is open.
        /// </summary>
        /// <remarks><see langword="true"/> if the expander is open, otherwise <see langword="false"/>.</remarks>
        [DataMember]
        public bool IsExpanded
        {
            get => isExpanded;
            set => SetValue(value, ref isExpanded, isExpandedArgs);
        }
        /// <summary>
        /// Get the <see cref="PropertyElement"/> to display on the GUI.
        /// </summary>
        public abstract IEnumerable<PropertyElement> Properties { get; }
        /// <inheritdoc/>
        public ClipData Parent
        {
            get => clipData;
            internal set
            {
                clipData = value;

                Parallel.ForEach(Children, property => property.Parent = this);
            }
        }
        /// <inheritdoc/>
        public int Id => Parent.Effect.IndexOf(this);
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }


        #region Methods

        /// <inheritdoc/>
        public object Clone()
        {
            return this.DeepClone();
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
        public virtual void Loaded()
        {
            if (IsLoaded) return;

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public virtual void Unloaded()
        {
            if (!IsLoaded) return;

            IsLoaded = false;
        }

        #endregion


        /// <summary>
        /// Represents a command that changes the boolean for which an effect is enabled.
        /// </summary>
        internal sealed class CheckCommand : IRecordCommand
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
        internal sealed class UpCommand : IRecordCommand
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
        internal sealed class DownCommand : IRecordCommand
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
        internal sealed class RemoveCommand : IRecordCommand
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
            public void Do()
            {
                data.Effect.RemoveAt(index);
                effect.Unloaded();
            }

            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                effect.Loaded();
                data.Effect.Insert(index, effect);
            }
        }
        /// <summary>
        /// Represents a command to add an effect.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
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
                if (!(data.Effect[0] as ObjectElement).EffectFilter(effect)) throw new NotSupportedException();
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
                if (!(data.Effect[0] as ObjectElement).EffectFilter(effect)) throw new NotSupportedException();
            }

            /// <inheritdoc/>
            public void Do()
            {
                effect.Loaded();
                data.Effect.Add(effect);
            }

            /// <inheritdoc/>
            public void Redo() => Do();
            /// <inheritdoc/>
            public void Undo()
            {
                data.Effect.Remove(effect);
                effect.Unloaded();
            }
        }
    }

    public class EffectMetadata
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public Func<EffectElement> CreateFunc { get; set; }
        public IEnumerable<EffectMetadata> Children { get; set; }

        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new()
        {
            new()
            {
                Name = Resources.Effects,
                Children = new EffectMetadata[]
                {
                    new()
                    {
                        Name = Resources.Border,
                        Type = typeof(Border),
                        CreateFunc = () => new Border()
                    },
                    new()
                    {
                        Name = Resources.ColorKey,
                        Type = typeof(ColorKey),
                        CreateFunc = () => new ColorKey()
                    },
                    new()
                    {
                        Name = Resources.DropShadow,
                        Type = typeof(Shadow),
                        CreateFunc = () => new Shadow()
                    },
                    new()
                    {
                        Name = Resources.Blur,
                        Type = typeof(Blur),
                        CreateFunc = () => new Blur()
                    },
                    new()
                    {
                        Name = Resources.Monoc,
                        Type = typeof(Monoc),
                        CreateFunc = () => new Monoc()
                    },
                    new()
                    {
                        Name = Resources.Dilate,
                        Type = typeof(Dilate),
                        CreateFunc = () => new Dilate()
                    },
                    new()
                    {
                        Name = Resources.Erode,
                        Type = typeof(Erode),
                        CreateFunc = () => new Erode()
                    },
                    new()
                    {
                        Name = Resources.Clipping,
                        Type = typeof(Clipping),
                        CreateFunc = () => new Clipping()
                    },
                    new()
                    {
                        Name = Resources.AreaExpansion,
                        Type = typeof(AreaExpansion),
                        CreateFunc = () => new AreaExpansion()
                    }
                }
            },
            new()
            {
                Name = Resources.Camera,
                Children = new EffectMetadata[]
                {
                    new()
                    {
                        Name = Resources.DepthTest,
                        Type = typeof(DepthTest),
                        CreateFunc = () => new DepthTest()
                    },
                    new()
                    {
                        Name = Resources.DirectionalLightSource,
                        Type = typeof(DirectionalLightSource),
                        CreateFunc = () => new DirectionalLightSource()
                    },
                    new()
                    {
                        Name = Resources.PointLightSource,
                        Type = typeof(PointLightSource),
                        CreateFunc = () => new PointLightSource()
                    },
                    new()
                    {
                        Name = Resources.SpotLight,
                        Type = typeof(SpotLight),
                        CreateFunc = () => new SpotLight()
                    }
                }
            },
#if DEBUG
            new()
            {
                Name = "TestEffect",
                Type = typeof(TestEffect),
                CreateFunc = () => new TestEffect()
            }

#endif
        };
    }
}
