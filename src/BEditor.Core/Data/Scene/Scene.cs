using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Command;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    public partial class Scene : EditingObject
    {
        private static readonly PropertyInfo _ClipDataID = typeof(ClipElement).GetProperty(nameof(ClipElement.Id))!;
        private static readonly PropertyChangedEventArgs _SelectItemArgs = new(nameof(SelectItem));
        private static readonly PropertyChangedEventArgs _PrevireFrameArgs = new(nameof(PreviewFrame));
        private static readonly PropertyChangedEventArgs _TotalFrameArgs = new(nameof(TotalFrame));
        private static readonly PropertyChangedEventArgs _ZoomArgs = new(nameof(TimeLineZoom));
        private static readonly PropertyChangedEventArgs _HoffsetArgs = new(nameof(TimeLineHorizonOffset));
        private static readonly PropertyChangedEventArgs _VoffsetArgs = new(nameof(TimeLineVerticalOffset));
        private static readonly PropertyChangedEventArgs _SceneNameArgs = new(nameof(SceneName));
        private ClipElement? _selectItem;
        private ObservableCollection<ClipElement>? _selectItems;
        private Frame _previewframe;
        private Frame _totalframe = 1000;
        private float _timeLineZoom = 150;
        private double _timeLineHorizonOffset;
        private double _timeLineVerticalOffset;
        private string _sceneName = string.Empty;
        private IPlayer? _player;
        private WeakReference<Project?>? _parent;

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
        /// Gets the <see cref="ClipElement"/> from its <see cref="IHasName.Name"/>.
        /// </summary>
        /// <param name="name">Value of <see cref="IHasName.Name"/>.</param>
        public ClipElement? this[string? name]
        {
            [return: NotNullIfNotNull("name")]
            get
            {
                if (name is null)
                {
                    return null;
                }

                return this.Find(name);
            }
        }

        private sealed class RemoveLayerCommand : IRecordCommand
        {
            private readonly IEnumerable<IRecordCommand> _clips;

            public RemoveLayerCommand(Scene scene, int layer)
            {
                _clips = scene.GetLayer(layer).Select(clip => clip.Parent.RemoveClip(clip)).ToArray();
            }

            public string Name => CommandName.RemoveLayer;

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