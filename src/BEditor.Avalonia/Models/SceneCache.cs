namespace BEditor.Models
{
    public sealed class SceneCache
    {
        public SceneCache(string[] selects)
        {
            Selects = selects;
        }

        public string? Select { get; set; }

        public string[] Selects { get; set; }

        public int PreviewFrame { get; set; }

        public float TimelineScale { get; set; }

        public double TimelineHorizonOffset { get; set; }

        public double TimelineVerticalOffset { get; set; }
    }
}