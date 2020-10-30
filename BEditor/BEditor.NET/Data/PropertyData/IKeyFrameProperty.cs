using System;
using System.Collections.Generic;
using System.Text;

using BEditor.NET.Data.ProjectData;

namespace BEditor.NET.Data.PropertyData {
    public interface IKeyFrameProperty {
        public Scene Scene { get; }
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}
