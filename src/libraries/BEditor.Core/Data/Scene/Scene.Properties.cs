// Scene.Properties.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
        private bool _useCache = true;

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
        [Obsolete("Use Name.")]
        public string SceneName
        {
            get => Name;
            set
            {
                var old = Name;
                Name = value;
                if (old == value)
                {
                    RaisePropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(SceneName)));
                }
            }
        }

        /// <summary>
        /// Gets or sets the total frame.
        /// </summary>
        public Frame TotalFrame
        {
            get => _totalframe;
            set => SetAndRaise(value, ref _totalframe, _totalFrameArgs);
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
        /// Gets the cache of this scene.
        /// </summary>
        public SceneCache Cache { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether to use the cache or not.
        /// </summary>
        public bool UseCache
        {
            get => _useCache;
            set
            {
                _useCache = value;
                if (!value)
                {
                    Cache.Clear();
                }
            }
        }

        /// <summary>
        /// Gets or sets the selected <see cref="ClipElement"/>.
        /// </summary>
        public ClipElement? SelectItem
        {
            get => _selectItem;
            set
            {
                _selectItem = value;
                RaisePropertyChanged(_selectItemArgs);
            }
        }

        /// <summary>
        /// Gets graphic context.
        /// </summary>
        public GraphicsContext? GraphicsContext { get; private set; }

        /// <summary>
        /// Gets drawing context.
        /// </summary>
        [Obsolete("Use IApplication.DrawingContext.")]
        public DrawingContext? DrawingContext => this.GetParent<IApplication>()?.DrawingContext;

        /// <summary>
        /// Gets sampling context.
        /// </summary>
        public SamplingContext? SamplingContext { get; private set; }

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
            set => SetAndRaise(Math.Clamp(value, 0, TotalFrame), ref _previewframe, _previreFrameArgs);
        }

        /// <summary>
        /// Gets or sets the scale of the timeline.
        /// </summary>
        [Obsolete("Use TimeLineScale")]
        public float TimeLineZoom
        {
            get => TimeLineScale * 200;
            set => TimeLineScale = value / 200;
        }

        /// <summary>
        /// Gets or sets the scale of the timeline.
        /// </summary>
        public float TimeLineScale
        {
            get => _timeLineScale;
            set
            {
                if (SetAndRaise(Math.Clamp(value, 0.1f, 1), ref _timeLineScale, _scaleArgs))
                {
#pragma warning disable CS0612
                    RaisePropertyChanged(_zoomArgs);
#pragma warning restore CS0612
                }
            }
        }

        /// <summary>
        /// Gets or sets the horizontal scrolling offset of the timeline.
        /// </summary>
        public double TimeLineHorizonOffset
        {
            get => _timeLineHorizonOffset;
            set => SetAndRaise(value, ref _timeLineHorizonOffset, _hoffsetArgs);
        }

        /// <summary>
        /// Gets or sets the vertical scrolling offset of the timeline.
        /// </summary>
        public double TimeLineVerticalOffset
        {
            get => _timeLineVerticalOffset;
            set => SetAndRaise(value, ref _timeLineVerticalOffset, _voffsetArgs);
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
                Children.SetParent<Scene, ClipElement>(i => i.Parent = this);
            }
        }

        /// <summary>
        /// Gets or sets the settings for this scene.
        /// </summary>
        public SceneSettings Settings
        {
            get => new(Width, Height, Name);
            set
            {
                Width = value.Width;
                Height = value.Height;
                Name = value.Name;

                GraphicsContext?.Dispose();
                GraphicsContext = new(Width, Height);
            }
        }
    }
}