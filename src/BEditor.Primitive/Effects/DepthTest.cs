using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL depth test.
    /// </summary>
    [DataContract]
    public class DepthTest : EffectElement
    {
        /// <summary>
        /// Represents <see cref="Enabled"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata EnabledMetadata = new(Resources.DepthTestEneble, true);
        /// <summary>
        /// Represents <see cref="Function"/> metadata.
        /// </summary>
        public static readonly SelectorPropertyMetadata FunctionMetadata = new(Resources.DepthFunction, new string[]
        {
                "Never",
                "Less",
                "Equal",
                "Lequal",
                "Greater",
                "Notequal",
                "Gequal",
                "Always"
        }, 1);//初期値はless
        /// <summary>
        /// Represents <see cref="Mask"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata MaskMetadata = new("Mask", true);
        /// <summary>
        /// Represents <see cref="Near"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata NearMetadata = new("Near", 0, 100, 0);
        /// <summary>
        /// Represents <see cref="Far"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata FarMetadata = new("Far", 100, 100, 0);
        private static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new ReadOnlyCollection<DepthFunction>(new DepthFunction[]
        {
            DepthFunction.Never,
            DepthFunction.Less,
            DepthFunction.Equal,
            DepthFunction.Lequal,
            DepthFunction.Greater,
            DepthFunction.Notequal,
            DepthFunction.Gequal,
            DepthFunction.Always
        });

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthTest"/> class.
        /// </summary>
        public DepthTest()
        {
            Enabled = new(EnabledMetadata);
            Function = new(FunctionMetadata);
            Mask = new(MaskMetadata);
            Near = new(NearMetadata);
            Far = new(FarMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.DepthTest;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Enabled,
            Function,
            Mask,
            Near,
            Far
        };
        /// <summary>
        /// Gets the <see cref="CheckProperty"/> that represents the value to enable depth testing.
        /// </summary>
        [DataMember(Order = 0)]
        public CheckProperty Enabled { get; private set; }
        /// <summary>
        /// Get the <see cref="SelectorProperty"/> that selects the function for the depth test.
        /// </summary>
        [DataMember(Order = 1)]
        public SelectorProperty Function { get; private set; }
        /// <summary>
        /// Gets the <see cref="CheckProperty"/> indicating whether the depth mask is enabled.
        /// </summary>
        [DataMember(Order = 2)]
        public CheckProperty Mask { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the depth range.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty Near { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the depth range.
        /// </summary>
        [DataMember(Order = 4)]
        public EaseProperty Far { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            if (Enabled.IsChecked) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.IsChecked);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Enabled.Load(EnabledMetadata);
            Function.Load(FunctionMetadata);
            Mask.Load(MaskMetadata);
            Near.Load(NearMetadata);
            Far.Load(FarMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
