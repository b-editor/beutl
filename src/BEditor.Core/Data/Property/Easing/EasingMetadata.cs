using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace BEditor.Core.Data.Property.Easing
{
    public record EasingMetadata(string Name, Expression<Func<EasingFunc>> Create)
    {
        private Func<EasingFunc>? _Func;

        public Type Type => ((NewExpression)Create.Body).Type;
        public Func<EasingFunc> CreateFunc => _Func ??= Create.Compile();


        /// <summary>
        /// 読み込まれているイージング関数のType
        /// </summary>
        public static List<EasingMetadata> LoadedEasingFunc { get; } = new()
        {
            new EasingMetadata("Primitive", () => new PrimitiveEasing())
        };
    }
}
