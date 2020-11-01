using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.Core.Media {
    [DataContract(Namespace ="")]
    public record Font {
        [DataMember()]
        public string Name { get; set; }
        [DataMember()]
        public string Path { get; set; }

        public override string ToString() => $"(Name:{Name} Path:{Path})";
    }
}
