using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;

using OpenTK.Graphics.OpenGL;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class DepthTest : EffectElement
    {
        public static readonly CheckPropertyMetadata EnabledMetadata = new(Resources.DepthTestEneble, true);
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
        });//初期値はless
        public static readonly CheckPropertyMetadata MaskMetadata = new("Mask", true);
        public static readonly EasePropertyMetadata NearMetadata = new("Near", 0, 100, 0);
        public static readonly EasePropertyMetadata FarMetadata = new("Far", 100, 100, 0);
        public static readonly ReadOnlyCollection<DepthFunction> DepthFunctions = new ReadOnlyCollection<DepthFunction>(new DepthFunction[]
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

        public DepthTest()
        {
            Enabled = new(EnabledMetadata);
            Function = new(FunctionMetadata);
            Mask = new(MaskMetadata);
            Near = new(NearMetadata);
            Far = new(FarMetadata);
        }

        public override string Name => Resources.DepthTest;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Enabled,
            Function,
            Mask,
            Near,
            Far
        };
        [DataMember(Order = 0)]
        public CheckProperty Enabled { get; private set; }
        [DataMember(Order = 1)]
        public SelectorProperty Function { get; private set; }
        [DataMember(Order = 2)]
        public CheckProperty Mask { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Near { get; private set; }
        [DataMember(Order = 4)]
        public EaseProperty Far { get; private set; }

        public override void Render(EffectRenderArgs args)
        {
            if (Enabled.IsChecked) GL.Enable(EnableCap.DepthTest);
            else GL.Disable(EnableCap.DepthTest);

            GL.DepthFunc(DepthFunctions[Function.Index]);

            GL.DepthMask(Mask.IsChecked);

            GL.DepthRange(Near.GetValue(args.Frame) / 100, Far.GetValue(args.Frame) / 100);
        }
        public override void Loaded()
        {
            base.Loaded();
            Enabled.ExecuteLoaded(EnabledMetadata);
            Function.ExecuteLoaded(FunctionMetadata);
            Mask.ExecuteLoaded(MaskMetadata);
            Near.ExecuteLoaded(NearMetadata);
            Far.ExecuteLoaded(FarMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            foreach (var pr in Children)
            {
                pr.Unloaded();
            }
        }
    }
}
