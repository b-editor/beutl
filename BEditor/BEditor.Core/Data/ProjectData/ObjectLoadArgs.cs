using System.Collections.Generic;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;

namespace BEditor.Core.Data.ProjectData {
    public class ObjectLoadArgs {

        public ObjectLoadArgs(int frame, List<ClipData> schedules) {
            Frame = frame;
            Schedules = schedules;
        }

        public int Frame { get; }
        public List<ClipData> Schedules { get; }
        public bool Handled { get; set; }
    }

    public class EffectLoadArgs {

        public EffectLoadArgs(int frame, List<EffectElement> schedules) {
            Frame = frame;
            Schedules = schedules;
        }

        public int Frame { get; }
        public List<EffectElement> Schedules { get; }
        public bool Handled { get; set; }
    }
}
