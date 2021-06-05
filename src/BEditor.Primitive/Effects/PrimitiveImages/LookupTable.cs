// LookupTable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the effect to which the Lookup Table is applied.
    /// </summary>
    public sealed class LookupTable : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Strength"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LookupTable, EaseProperty> StrengthProperty = EditingProperty.RegisterDirect<EaseProperty, LookupTable>(
            nameof(Strength),
            owner => owner.Strength,
            (owner, obj) => owner.Strength = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("強さ", 100)).Serialize());

        /// <summary>
        /// Defines the <see cref="LUTFile"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LookupTable, FileProperty> LUTFileProperty = EditingProperty.RegisterDirect<FileProperty, LookupTable>(
            nameof(LUTFile),
            owner => owner.LUTFile,
            (owner, obj) => owner.LUTFile = obj,
            EditingPropertyOptions<FileProperty>.Create(
                new FilePropertyMetadata("Cubeファイル", Filter: new("Cubeファイル", new FileExtension[] { new("CUBE"), new("cube") })))
            .Serialize());

        private Drawing.LookupTable? _lut;

        private IDisposable? _disposable;

        /// <inheritdoc/>
        public override string Name => "Apply Lookup Table";

        /// <summary>
        /// Gets the strength.
        /// </summary>
        [AllowNull]
        public EaseProperty Strength { get; private set; }

        /// <summary>
        /// Gets the LUT file to use.
        /// </summary>
        [AllowNull]
        public FileProperty LUTFile { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (_lut is not null)
            {
                args.Value.ApplyLookupTable(_lut, Strength[args.Frame] / 100);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Strength;
            yield return LUTFile;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            _disposable = LUTFile.Subscribe(f =>
            {
                if (!File.Exists(f))
                {
                    _lut?.Dispose();
                }
                else
                {
                    _lut = Drawing.LookupTable.FromCube(f);
                }
            });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            _disposable?.Dispose();
            _lut?.Dispose();
        }
    }
}
