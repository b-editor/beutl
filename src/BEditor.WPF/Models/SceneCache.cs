using System.Runtime.Serialization;

namespace BEditor.Models
{
    [DataContract]
    public class SceneCache
    {
        public SceneCache(string[] selects)
        {
            Selects = selects;
        }

        [DataMember]
        public string? Select { get; set; }
        [DataMember]
        public string[] Selects { get; set; }
        [DataMember]
        public int PreviewFrame { get; set; }
        [DataMember]
        public float TimelineScale { get; set; }
        [DataMember]
        public double TimelineHorizonOffset { get; set; }
        [DataMember]
        public double TimelineVerticalOffset { get; set; }
    }
}
