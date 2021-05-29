// Scene.Properties.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using BEditor.Audio;
using BEditor.Data.Internals;
using BEditor.Drawing;
using BEditor.Graphics;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    public partial class Scene : IParent<ClipElement>, IChild<Project>
    {
        /// <summary>
        /// Gets the width of the frame buffer.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height of the frame buffer.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets or sets the name of this <see cref="Scene"/>.
        /// </summary>
        public virtual string SceneName
        {
            get => _sceneName;
            set => SetValue(value, ref _sceneName, _SceneNameArgs);
        }

        /// <summary>
        /// Gets or sets the total frame.
        /// </summary>
        public Frame TotalFrame
        {
            get => _totalframe;
            set => SetValue(value, ref _totalframe, _TotalFrameArgs);
        }

        /// <summary>
        /// Gets the number of the hidden layer.
        /// </summary>
        public List<int> HideLayer { get; private set; } = new List<int>();

        /// <summary>
        /// Gets the <see cref="ClipElement"/> contained in this <see cref="Scene"/>.
        /// </summary>
        public ObservableCollection<ClipElement> Datas { get; private set; }

        /// <summary>
        /// Gets or sets the selected <see cref="ClipElement"/>.
        /// </summary>
        public ClipElement? SelectItem
        {
            get => _selectItem ??= SelectItems.FirstOrDefault();
            set
            {
                _selectItem = value;
                RaisePropertyChanged(_SelectItemArgs);
            }
        }

        /// <summary>
        /// Gets the selected <see cref="ClipElement"/>.
        /// </summary>
        public ObservableCollection<ClipElement> SelectItems
        {
            get
            {
                if (_selectItems is null)
                {
                    _selectItems = new();

                    _selectItems.CollectionChanged += (s, e) =>
                    {
                        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Remove)
                        {
                            if (SelectItems.Count == 0)
                            {
                                SelectItem = null;
                            }
                        }
                    };
                }

                return _selectItems;
            }
        }

        /// <summary>
        /// Gets graphic context.
        /// </summary>
        public GraphicsContext? GraphicsContext { get; private set; }

        /// <summary>
        /// Gets drawing context.
        /// </summary>
        public DrawingContext? DrawingContext { get; private set; }

        /// <summary>
        /// Gets audio context.
        /// </summary>
        public AudioContext? AudioContext { get; private set; }

        /// <summary>
        /// Gets a player to play this <see cref="Scene"/>.
        /// </summary>
        public IPlayer Player =>
            _player ??= new ScenePlayer(this);

        /// <summary>
        /// Gets or sets the frame number during preview.
        /// </summary>
        public Frame PreviewFrame
        {
            get => _previewframe;
            set => SetValue(value, ref _previewframe, _PrevireFrameArgs);
        }

        /// <summary>
        /// Gets or sets the scale of the timeline.
        /// </summary>
        public float TimeLineZoom
        {
            get => _timeLineZoom;
            set => SetValue(Math.Clamp(value, 1, 200), ref _timeLineZoom, _ZoomArgs);
        }

        /// <summary>
        /// Gets or sets the horizontal scrolling offset of the timeline.
        /// </summary>
        public double TimeLineHorizonOffset
        {
            get => _timeLineHorizonOffset;
            set => SetValue(value, ref _timeLineHorizonOffset, _HoffsetArgs);
        }

        /// <summary>
        /// Gets or sets the vertical scrolling offset of the timeline.
        /// </summary>
        public double TimeLineVerticalOffset
        {
            get => _timeLineVerticalOffset;
            set => SetValue(value, ref _timeLineVerticalOffset, _VoffsetArgs);
        }

        /// <inheritdoc/>
        public IEnumerable<ClipElement> Children => Datas;

        /// <inheritdoc/>
        public Project Parent
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
        /// Gets or sets the settings for this scene.
        /// </summary>
        public SceneSettings Settings
        {
            get => new(Width, Height, SceneName);
            set
            {
                Width = value.Width;
                Height = value.Height;
                SceneName = value.Name;

                GraphicsContext?.Dispose();
                GraphicsContext = new(Width, Height);
            }
        }
    }
}