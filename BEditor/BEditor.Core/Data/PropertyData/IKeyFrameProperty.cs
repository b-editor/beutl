using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Core.Data.ProjectData;

namespace BEditor.Core.Data.PropertyData {
    public interface IKeyFrameProperty {
        public Scene Scene { get; }
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}
