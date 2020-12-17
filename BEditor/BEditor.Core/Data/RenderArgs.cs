using System.Collections.Generic;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the data to be passed to the <see cref="ClipData"/> at rendering time.
    /// </summary>
    public record ClipRenderArgs
    {
        /// <summary>
        /// <see cref="ClipRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public ClipRenderArgs(int frame, List<ClipData> schedules, RenderType type = RenderType.Preview)
        {
            Frame = frame;
            Schedules = schedules;
            Type = type;
        }
        /// <summary>
        /// <see cref="ClipRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public ClipRenderArgs() { }

        /// <summary>
        /// Get the frame to render.
        /// </summary>
        public int Frame { get; init; }
        /// <summary>
        /// Get the <see cref="ClipData"/> to render.
        /// </summary>
        public List<ClipData> Schedules { get; init; }
        /// <summary>
        /// Gets or sets a value that indicates the current state of the process.
        /// </summary>
        public bool Handled { get; set; }
        /// <summary>
        /// Get the rendering type.
        /// </summary>
        public RenderType Type { get; init; }
    }

    /// <summary>
    /// Represents the data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public record EffectRenderArgs
    {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public EffectRenderArgs() { }
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public EffectRenderArgs(int frame, List<EffectElement> schedules, RenderType type = RenderType.Preview)
        {
            Frame = frame;
            Schedules = schedules;
            Type = type;
        }

        /// <summary>
        /// Get the frame to render.
        /// </summary>
        public int Frame { get; init; }
        /// <summary>
        /// Get the <see cref="EffectElement"/> to render.
        /// </summary>
        public List<EffectElement> Schedules { get; init; }
        /// <summary>
        /// Gets or sets a value that indicates the current state of the process.
        /// </summary>
        public bool Handled { get; set; }
        /// <summary>
        /// Get the rendering type.
        /// </summary>
        public RenderType Type { get; init; }
    }
    /// <summary>
    /// Represents the data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public record EffectRenderArgs<T> : EffectRenderArgs
    {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public EffectRenderArgs() { }
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the record.
        /// </summary>
        public EffectRenderArgs(int frame, List<EffectElement> schedules) : base(frame, schedules)
        {
        }

        /// <summary>
        /// Gets or sets the value used to render the effect.
        /// </summary>
        public T Value { get; set; }
    }
    public enum RenderType
    {
        Preview,
        Output
    }
}
