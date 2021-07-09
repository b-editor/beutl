using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions.AviUtl.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor.Extensions.AviUtl
{
    public sealed class LuaScript : ImageEffect
    {
        public static readonly DirectProperty<LuaScript, DocumentProperty> CodeProperty = EditingProperty.RegisterDirect<DocumentProperty, LuaScript>(
            nameof(Code),
            owner => owner.Code,
            (owner, obj) => owner.Code = obj,
            EditingPropertyOptions<DocumentProperty>.Create(new DocumentPropertyMetadata(string.Empty)).Serialize());

        public override string Name => "スクリプト制御";

        [AllowNull]
        public DocumentProperty Code { get; private set; }

        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is ImageObject obj)
            {
                var lua = Plugin.Loader.Global;
                var table = new ObjectTable(args, obj);
                lua.SetValue("obj", table);
                lua.SetValue("rand", new ObjectTable.RandomDelegate(table.rand));

                try
                {
                    var result = lua.DoChunk(Code.Value, Plugin.Loader.BaseDirectory);
                }
                catch (Exception e)
                {
                    ServicesLocator.Current.Logger.LogError(e, Strings.FailedToExecuteScript);
                    ServiceProvider?.GetService<IMessage>()?.Snackbar(Strings.FailedToExecuteScript);
                }
            }
            Parent.Parent.GraphicsContext!.PlatformImpl.MakeCurrent();
        }

        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Code;
        }
    }
}