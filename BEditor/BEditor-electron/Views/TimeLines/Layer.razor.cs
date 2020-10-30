using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;

namespace BEditor_Electron.Views.TimeLines {
    public partial class Layer {
        public int Number { get; set; }
        public Scene Scene { get; set; }
        

        public Layer() {

        }

        public IEnumerable<ClipData> GetLayer() {
            var result = from x in Scene.Datas
                         where x.Layer == Number
                         select x;

            return result;
        }
    }
}
