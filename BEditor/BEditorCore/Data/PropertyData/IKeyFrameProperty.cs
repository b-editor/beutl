using System;
using System.Collections.Generic;
using System.Text;

using BEditorCore.Data.ProjectData;

namespace BEditorCore.Data.PropertyData {
    public interface IKeyFrameProperty {
        public Scene Scene { get; }
        public dynamic ComponentData { get; }
        public bool Contains(string key);
    }
}
