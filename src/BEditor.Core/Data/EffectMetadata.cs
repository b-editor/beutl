using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Data
{
    //Todo: Document
#pragma warning disable CS1591
    public record EffectMetadata(string Name, Func<EffectElement> CreateFunc, Type Type)
    {
        public EffectMetadata(string Name) : this(Name, () => new EffectElement.EmptyClass()) { }

        public EffectMetadata(string Name, Expression<Func<EffectElement>> Create) : this(Name, Create.Compile(), ((NewExpression)Create.Body).Type)
        {

        }


        public IEnumerable<EffectMetadata>? Children { get; set; }

        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new();


        public static EffectMetadata Create<T>(string Name) where T : EffectElement, new()
        {
            return new(Name, () => new T(), typeof(T));
        }
    }
#pragma warning restore CS1591
}
