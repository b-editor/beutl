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
    public partial class ClipElement : EditingObject, IParent<EffectElement>, IChild<Scene>, IHasName, IHasId
    {
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
    }
}
