using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.PortableExecutable;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Extensions.AviUtl
{
    public sealed class LuaScript : ImageEffect
    {
        public static readonly DirectEditingProperty<LuaScript, DocumentProperty> CodeProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, LuaScript>(
            nameof(Code),
            owner => owner.Code,
            (owner, obj) => owner.Code = obj,
            new DocumentPropertyMetadata(string.Empty));

        public override string Name => "スクリプト制御";

        [AllowNull]
        public DocumentProperty Code { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {

        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            throw new System.NotImplementedException();
        }
    }
}