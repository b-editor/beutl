using System;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;

namespace BEditorCore.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class ColorProperty : PropertyElement {
        private byte r;
        private byte g;
        private byte b;
        private byte a;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public ColorProperty(ColorPropertyMetadata metadata) {
            Red = metadata.Red;
            Green = metadata.Green;
            Blue = metadata.Blue;
            Alpha = metadata.Alpha;
            PropertyMetadata = metadata;
        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public byte Red { get => r; set => SetValue(value, ref r, nameof(Red)); }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public byte Green { get => g; set => SetValue(value, ref g, nameof(Green)); }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public byte Blue { get => b; set => SetValue(value, ref b, nameof(Blue)); }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public byte Alpha { get => a; set => SetValue(value, ref a, nameof(Alpha)); }

        public static implicit operator Media.Color4(ColorProperty val) => Media.Color4.FromRgba(val.Red, val.Green, val.Blue, val.Alpha);
        public static implicit operator Media.Color3(ColorProperty val) => Media.Color3.FromRgb(val.Red, val.Green, val.Blue);

        /// <summary>
        /// 
        /// </summary>
        public class ChangeColor : IUndoRedoCommand {
            private readonly ColorProperty Color;
            private readonly byte r, g, b, a;
            private readonly byte or, og, ob, oa;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="r"></param>
            /// <param name="g"></param>
            /// <param name="b"></param>
            /// <param name="a"></param>
            public ChangeColor(ColorProperty property, byte r, byte g, byte b, byte a) {
                Color = property;
                (this.r, this.g, this.b, this.a) = (r, g, b, a);
                (or, og, ob, oa) = (property.Red, property.Green, property.Blue, property.Alpha);
            }


            /// <inheritdoc/>
            public void Do() {
                Color.Red = r;
                Color.Green = g;
                Color.Blue = b;
                Color.Alpha = a;
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                Color.Red = or;
                Color.Green = og;
                Color.Blue = ob;
                Color.Alpha = oa;
            }
        }


    }

    /// <summary>
    /// 
    /// </summary>
    public class ColorPropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public ColorPropertyMetadata(string name) : base(name) {
            Red = 255;
            Green = 255;
            Blue = 255;
            Alpha = 255;
            UseAlpha = false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="a"></param>
        /// <param name="usealpha"></param>
        public ColorPropertyMetadata(string name, byte r, byte g, byte b, byte a = 255, bool usealpha = false) : base(name) {
            Red = r;
            Green = g;
            Blue = b;
            Alpha = a;
            UseAlpha = usealpha;
        }

        /// <summary>
        /// 
        /// </summary>
        public byte Red { get; }
        /// <summary>
        /// 
        /// </summary>
        public byte Green { get; }
        /// <summary>
        /// 
        /// </summary>
        public byte Blue { get; }
        /// <summary>
        /// 
        /// </summary>
        public byte Alpha { get; }
        /// <summary>
        /// 
        /// </summary>
        public bool UseAlpha { get; }
    }
}
