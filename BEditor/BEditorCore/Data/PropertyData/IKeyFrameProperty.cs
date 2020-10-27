using System;
using System.Collections.Generic;
using System.Text;

using BEditorCore.Data.ProjectData;

namespace BEditorCore.Data.PropertyData {
    public interface IKeyFrameProperty {
        public Scene Scene { get; }
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}
