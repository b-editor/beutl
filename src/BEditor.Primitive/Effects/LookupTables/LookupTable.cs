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

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects.LookupTables
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
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Strength, 100, 100, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="LUTFile"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<LookupTable, FileProperty> LUTFileProperty = EditingProperty.RegisterDirect<FileProperty, LookupTable>(
            nameof(LUTFile),
            owner => owner.LUTFile,
            (owner, obj) => owner.LUTFile = obj,
            EditingPropertyOptions<FileProperty>.Create(
                new FilePropertyMetadata(Strings.CubeFile, Filter: new(Strings.CubeFile, new FileExtension[] { new("cube"), new("dat"), new("3dl"), new("m3d") })))
            .Serialize());

        private Drawing.LookupTable? _lut;

        private IDisposable? _disposable;

        /// <summary>
        /// Initializes a new instance of the <see cref="LookupTable"/> class.
        /// </summary>
        public LookupTable()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ApplyLookupTable;

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
                args.Value.Apply(_lut, Strength[args.Frame] / 100);
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
                _lut?.Dispose();
                if (File.Exists(f))
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
