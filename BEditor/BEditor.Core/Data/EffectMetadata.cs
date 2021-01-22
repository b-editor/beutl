using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

using BEditor.Core.Data.Primitive.Effects;
using BEditor.Core.Properties;

namespace BEditor.Core.Data
{
    public record EffectMetadata(string Name, Expression<Func<EffectElement>> Create)
    {
        private Func<EffectElement>? _Func;

        public EffectMetadata(string Name) : this(Name, () => new EffectElement.EmptyClass()) { }

        public Type Type => ((NewExpression)Create.Body).Type;
        public Func<EffectElement> CreateFunc => _Func ??= Create.Compile();
        public IEnumerable<EffectMetadata>? Children { get; set; }



        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new()
        {
            new(Resources.Effects)
            {
                Children = new EffectMetadata[]
                {
                    new(Resources.Border, () => new Border()),
                    new(Resources.ColorKey, () => new ColorKey()),
                    new(Resources.DropShadow, () => new Shadow()),
                    new(Resources.Blur, () => new Blur()),
                    new(Resources.Monoc, () => new Monoc()),
                    new(Resources.Dilate, () => new Dilate()),
                    new(Resources.Erode, () => new Erode()),
                    new(Resources.Clipping, () => new Clipping()),
                    new(Resources.AreaExpansion, () => new AreaExpansion()),
                }
            },
            new(Resources.Camera)
            {
                Children = new EffectMetadata[]
                {
                    new(Resources.DepthTest, () => new DepthTest()),
                    new(Resources.DirectionalLightSource, () => new DirectionalLightSource()),
                    new(Resources.PointLightSource, () => new PointLightSource()),
                    new(Resources.SpotLight, () => new SpotLight()),
                }
            },
#if DEBUG
            new("TestEffect", () => new TestEffect()),
#endif
        };
    }
}
