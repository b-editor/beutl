using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq.Expressions;

namespace BEditor.Data
{
#pragma warning disable CS1591
    public record EffectMetadata(string Name, Expression<Func<EffectElement>> Create)
    {
        private Func<EffectElement>? _Func;

        public EffectMetadata(string Name) : this(Name, () => new EffectElement.EmptyClass()) { }

        public Type Type => ((NewExpression)Create.Body).Type;
        public Func<EffectElement> CreateFunc => _Func ??= Create.Compile();
        public IEnumerable<EffectMetadata>? Children { get; set; }



        public static ObservableCollection<EffectMetadata> LoadedEffects { get; } = new();
    }
#pragma warning restore CS1591
}
