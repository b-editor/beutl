using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BEditor.Page.Languages
{
    public class English : IResources
    {
        public string OpenSource => "Open source";
        public string Free => "Free";
        public string Extension => "Extension";

        public string OpenSourceDescription { get; }
        public string FreeDescription { get; }
        public string ExtensionDescription { get; }
    }
}
