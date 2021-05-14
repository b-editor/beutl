using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Objects;

using Neo.IronLua;
using SkiaSharp;
using System.IO;
using System.Diagnostics;

namespace BEditor.Extensions.AviUtl
{
    public sealed class LuaScript : ImageEffect
    {
        public static readonly DirectEditingProperty<LuaScript, DocumentProperty> CodeProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, LuaScript>(
            nameof(Code),
            owner => owner.Code,
            (owner, obj) => owner.Code = obj,
            new DocumentPropertyMetadata(string.Empty));

        internal static readonly Lua LuaEngine = new();

        internal static readonly LuaGlobal LuaGlobal = LuaEngine.CreateEnvironment();

        static LuaScript()
        {
            //LuaGlobal.SetValue("obj", ObjectTable);
        }

        public override string Name => "スクリプト制御";

        [AllowNull]
        public DocumentProperty Code { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is ImageObject obj)
            {
                var table = new ObjectTable(args, obj);
                LuaGlobal.SetValue("obj", table);

                try
                {
                    var result = LuaGlobal.DoChunk(Code.Value, "main");
                }
                catch
                {
                    //Debug.Fail(string.Empty);
                }
            }
            Parent.Parent.GraphicsContext!.MakeCurrentAndBindFbo();
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Code;
        }
    }
}