namespace BEditor.Models
{
    public sealed class SceneCache
    {
        public string? Select { get; set; }

        public int PreviewFrame { get; set; }

        public float TimelineScale { get; set; }

        public double TimelineHorizonOffset { get; set; }

        public double TimelineVerticalOffset { get; set; }
    }
}