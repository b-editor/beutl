// ClipElement.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data of a clip to be placed in the timeline.
    /// </summary>
    public partial class ClipElement : EditingObject, IParent<EffectElement>, IChild<Scene>
    {
        private static readonly PropertyChangedEventArgs _startArgs = new(nameof(Start));
        private static readonly PropertyChangedEventArgs _endArgs = new(nameof(End));
        private static readonly PropertyChangedEventArgs _layerArgs = new(nameof(Layer));
        private static readonly PropertyChangedEventArgs _textArgs = new(nameof(LabelText));
        private string? _name;
        private Frame _start;
        private Frame _end;
        private int _layer;
        private string _labelText = string.Empty;
        private Scene _parent;
        private ObservableCollection<EffectElement> _effect;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipElement"/> class.
        /// </summary>
        /// <param name="start">The starting frame of the clip.</param>
        /// <param name="end">The ending frame of the clip.</param>
        /// <param name="layer">The layer number of the clip.</param>
        /// <param name="scene">The scene where this clip will be placed.</param>
        /// <param name="metadata">The metadata for the <see cref="ObjectElement"/> contained in this clip.</param>
        public ClipElement(Frame start, Frame end, int layer, Scene scene, ObjectMetadata metadata)
        {
            _start = start;
            _end = end;
            _layer = layer;
            _effect = new() { metadata.CreateFunc() };
            Metadata = metadata;
            Parent = _parent = scene;
            LabelText = Name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipElement"/> class.
        /// </summary>
        /// <param name="start">The starting frame of the clip.</param>
        /// <param name="end">The ending frame of the clip.</param>
        /// <param name="layer">The layer number of the clip.</param>
        /// <param name="scene">The scene where this clip will be placed.</param>
        /// <param name="obj">The <see cref="ObjectElement"/> contained in this clip.</param>
        public ClipElement(Frame start, Frame end, int layer, Scene scene, ObjectElement obj)
        {
            _start = start;
            _end = end;
            _layer = layer;
            _effect = new() { obj };
            Metadata = ObjectMetadata.LoadedObjects.First(i => i.Type == Effect[0].GetType());
            Parent = _parent = scene;
            LabelText = Name;
        }

        /// <summary>
        /// Gets the name of this <see cref="ClipElement"/>.
        /// </summary>
        public string Name => _name ??= Effect[0].GetType().Name;

        /// <summary>
        /// Gets or sets the starting frame of this <see cref="ClipElement"/>.
        /// </summary>
        public Frame Start
        {
            get => _start;
            set => SetAndRaise(value, ref _start, _startArgs);
        }

        /// <summary>
        /// Gets or sets the ending frame of this <see cref="ClipElement"/>.
        /// </summary>
        public Frame End
        {
            get => _end;
            set => SetAndRaise(value, ref _end, _endArgs);
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
                SetAndRaise(value, ref _layer, _layerArgs);
            }
        }

        /// <summary>
        /// Gets or sets the character displayed in this <see cref="ClipElement"/>.
        /// </summary>
        public string LabelText
        {
            get => _labelText;
            set => SetAndRaise(value, ref _labelText, _textArgs);
        }

        /// <inheritdoc/>
        public Scene Parent
        {
            get => _parent;
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
        /// Gets the metadata for the <see cref="ObjectElement"/> contained in this <see cref="ClipElement"/>.
        /// </summary>
        public ObjectMetadata Metadata { get; private set; }

        /// <summary>
        /// Gets the effects included in this <see cref="ClipElement"/>.
        /// </summary>
        public ObservableCollection<EffectElement> Effect => _effect;

        /// <inheritdoc/>
        public IEnumerable<EffectElement> Children => Effect;
    }
}