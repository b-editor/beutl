using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BEditor.Page.Languages {
    public interface IResources {
        public string OpenSource { get; }
        public string OpenSourceDescription { get; }
        public string Free { get; }
        public string FreeDescription { get; }
        public string Extension { get; }
        public string ExtensionDescription { get; }
    }
}
