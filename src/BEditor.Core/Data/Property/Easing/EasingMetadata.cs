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
    public record EasingMetadata(string Name, Func<EasingFunc> CreateFunc, Type Type)
    {
        public EasingMetadata(string Name, Expression<Func<EasingFunc>> Create) : this(Name, Create.Compile(), ((NewExpression)Create.Body).Type)
        {

        }


        /// <summary>
        /// Loaded <see cref="EasingMetadata"/>
        /// </summary>
        public static List<EasingMetadata> LoadedEasingFunc { get; } = new()
        {
            Create<PrimitiveEasing>("Primitive")
        };


        public static EasingMetadata Create<T>(string Name) where T : EasingFunc, new()
        {
            return new(Name, () => new T(), typeof(T));
        }
    }
#pragma warning restore CS1591 // 公開されている型またはメンバーの XML コメントがありません
}
