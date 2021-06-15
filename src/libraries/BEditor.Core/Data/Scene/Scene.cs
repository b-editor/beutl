// Scene.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Command;
using BEditor.Media;
using BEditor.Resources;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    public partial class Scene : EditingObject
    {
        private static readonly PropertyChangedEventArgs _selectItemArgs = new(nameof(SelectItem));
        private static readonly PropertyChangedEventArgs _previreFrameArgs = new(nameof(PreviewFrame));
        private static readonly PropertyChangedEventArgs _totalFrameArgs = new(nameof(TotalFrame));
        private static readonly PropertyChangedEventArgs _zoomArgs = new(nameof(TimeLineZoom));
        private static readonly PropertyChangedEventArgs _hoffsetArgs = new(nameof(TimeLineHorizonOffset));
        private static readonly PropertyChangedEventArgs _voffsetArgs = new(nameof(TimeLineVerticalOffset));
        private static readonly PropertyChangedEventArgs _sceneNameArgs = new(nameof(SceneName));
        private ClipElement? _selectItem;
        private Frame _previewframe;
        private Frame _totalframe = 1000;
        private float _timeLineZoom = 150;
        private double _timeLineHorizonOffset;
        private double _timeLineVerticalOffset;
        private string _sceneName = string.Empty;
        private IPlayer? _player;
        private Project? _parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="Scene"/> class.
        /// </summary>
        /// <param name="width">The width of the frame buffer.</param>
        /// <param name="height">The height of the frame buffer.</param>
        public Scene(int width, int height)
        {
            Width = width;
            Height = height;
            Datas = new ObservableCollection<ClipElement>();
        }

        /// <summary>
        /// Gets the <see cref="ClipElement"/> from its <see cref="ClipElement.Name"/>.
        /// </summary>
        /// <param name="name">Value of <see cref="ClipElement.Name"/>.</param>
        public ClipElement? this[string? name]
        {
            [return: NotNullIfNotNull("name")]
            get
            {
                if (name is null)
                {
                    return null;
                }

                for (var i = 0; i < Datas.Count; i++)
                {
                    var item = Datas[i];
                    if (item.Name == name)
                    {
                        return item;
                    }
                }

                return null;
            }
        }

        private sealed class RemoveLayerCommand : IRecordCommand
        {
            private readonly IEnumerable<IRecordCommand> _clips;

            public RemoveLayerCommand(Scene scene, int layer)
            {
                _clips = scene.GetLayer(layer).Select(clip => clip.Parent.RemoveClip(clip)).ToArray();
            }

            public string Name => Strings.RemoveLayer;

            public void Do()
            {
                foreach (var clip in _clips)
                {
                    clip.Do();
                }
            }

            public void Redo()
            {
                foreach (var clip in _clips)
                {
                    clip.Redo();
                }
            }

            public void Undo()
            {
                foreach (var clip in _clips)
                {
                    clip.Undo();
                }
            }
        }
    }

    /// <summary>
    /// Represents a <see cref="Scene"/> setting.
    /// </summary>
    public record SceneSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSettings"/> class.
        /// </summary>
        /// <param name="width">The width of the frame buffer.</param>
        /// <param name="height">The height of the frame buffer.</param>
        /// <param name="name">The name of the <see cref="Scene"/>.</param>
        public SceneSettings(int width, int height, string name)
        {
            Width = width;
            Height = height;
            Name = name;
        }

        /// <summary>
        /// Gets the width.
        /// </summary>
        public int Width { get; init; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        public int Height { get; init; }

        /// <summary>
        /// Gets the name.
        /// </summary>
        public string Name { get; init; }
    }
}