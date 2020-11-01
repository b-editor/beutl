using System;
using System.Runtime.Serialization;

using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData {

    [DataContract(Namespace = "")]
    public abstract class ExpandGroup : Group, IEasingSetting {
        private bool isOpen;


        [DataMember]
        public bool IsExpanded { get => isOpen; set => SetValue(value, ref isOpen, nameof(IsExpanded)); }

        public ExpandGroup(PropertyElementMetadata metadata) {
            PropertyMetadata = metadata;
        }


        public override string ToString() => $"(IsExpanded:{IsExpanded} Name:{PropertyMetadata?.Name})";
    }
}
