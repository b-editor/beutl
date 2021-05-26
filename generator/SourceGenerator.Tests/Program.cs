using System.Collections.Generic;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Property;

namespace SourceGenerator.Tests
{
    [GenerateTarget]
    public partial class UserClass : EffectElement
    {
        public static readonly DirectEditingProperty<UserClass, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, UserClass>("Color",
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);
        public static readonly EditingProperty<ColorProperty> ValueProperty = EditingProperty.Register<ColorProperty, UserClass>("Value");

        public override string Name => string.Empty;

        public override void Apply(EffectApplyArgs args)
        {
            _ = Color;
            _ = Value;
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Enumerable.Empty<PropertyElement>();
        }
    }

    public class Program
    {
        static void Main(string[] args)
        {
        }
    }
}