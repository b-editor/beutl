using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace BEditor.Data.Property.Easing
{
    //Todo: Document
    /// <summary>
    /// Initializes a new instance of the <see cref="EasingMetadata"/> class.
    /// </summary>
#pragma warning disable CS1591 // 公開されている型またはメンバーの XML コメントがありません
    public record EasingMetadata(string Name, Expression<Func<EasingFunc>> Create)
    {
        private Func<EasingFunc>? _Func;

        public Type Type => ((NewExpression)Create.Body).Type;
        public Func<EasingFunc> CreateFunc => _Func ??= Create.Compile();


        /// <summary>
        /// Loaded <see cref="EasingMetadata"/>
        /// </summary>
        public static List<EasingMetadata> LoadedEasingFunc { get; } = new()
        {
            new EasingMetadata("Primitive", () => new PrimitiveEasing())
        };
    }
#pragma warning restore CS1591 // 公開されている型またはメンバーの XML コメントがありません
}
