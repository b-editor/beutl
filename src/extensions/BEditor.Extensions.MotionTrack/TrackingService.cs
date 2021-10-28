using System.Collections.Generic;

using BEditor.Drawing;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class TrackingService
    {
        public Dictionary<int, Rectangle> Saved { get; } = new();

        public Rectangle this[int id]
        {
            get
            {
                return Saved.TryGetValue(id, out var val) ? val : default;
            }
            set
            {
                if (!Saved.TryAdd(id, value))
                {
                    Saved[id] = value;
                }
            }
        }
    }
}