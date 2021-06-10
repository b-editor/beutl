// LookupTable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

using BEditor.Drawing.LookupTables;

using OpenCvSharp;

namespace BEditor.Drawing
{
    /// <summary>
    /// Specifies the dimension of the lookup table.
    /// </summary>
    public enum LookupTableDimension
    {
        /// <summary>
        /// The lookup table is 1D.
        /// </summary>
        OneDimension = 1,

        /// <summary>
        /// The lookup table is 3D.
        /// </summary>
        ThreeDimension = 3,
    }

    /// <summary>
    /// Represents a lookup table.
    /// </summary>
    public sealed unsafe class LookupTable : IDisposable
    {
        private static readonly Regex _lutSizeReg = new("^LUT_(?<dim>.*?)_SIZE (?<size>.*?)$");
        private static readonly Regex _titleReg = new("^TITLE \"(?<text>.*?)\"$");
        private static readonly Regex _domainMinReg = new("^DOMAIN_MIN (?<red>.*?) (?<green>.*?) (?<blue>.*?)$");
        private static readonly Regex _domainMaxReg = new("^DOMAIN_MAX (?<red>.*?) (?<green>.*?) (?<blue>.*?)$");
        private readonly UnmanagedArray<float>[] _arrays;

        /// <summary>
        /// Initializes a new instance of the <see cref="LookupTable"/> class.
        /// </summary>
        /// <param name="length">The length of the <see cref="LookupTable"/>.</param>
        /// <param name="lutsize">The size of the <see cref="LookupTable"/>.</param>
        /// <param name="dim">The dimension of the <see cref="LookupTable"/>.</param>
        public LookupTable(int length = 256, int lutsize = 256, LookupTableDimension dim = LookupTableDimension.OneDimension)
        {
            _arrays = new UnmanagedArray<float>[(int)dim];

            for (var i = 0; i < _arrays.Length; i++)
            {
                _arrays[i] = new(length);
            }

            Size = lutsize;
            Length = length;
            Dimension = dim;
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="LookupTable"/> class.
        /// </summary>
        ~LookupTable()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the length of this <see cref="LookupTable"/>.
        /// </summary>
        public int Length { get; }

        /// <summary>
        /// Gets the size of this <see cref="LookupTable"/>.
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Gets the dimension of this <see cref="LookupTable"/>.
        /// </summary>
        public LookupTableDimension Dimension { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Creates a lookup table to do the solarization process.
        /// </summary>
        /// <param name="cycle">The cycle.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Solarisation(int cycle = 2)
        {
            var table = new LookupTable();
            var data = (float*)table.GetPointer();
            for (var i = 0; i < 256; i++)
            {
                data[i] = (float)((Math.Sin(i * cycle * Math.PI / 255) + 1) / 2);
            }

            return table;
        }

        /// <summary>
        /// Creates a lookup table to flip the image negative-positive.
        /// </summary>
        /// <param name="value">The threshold value.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Negaposi(byte value = 255)
        {
            var table = new LookupTable();
            var data = (float*)table.GetPointer();
            Image.PixelOperate(256, new Negaposi1DOperation(value, data));
            return table;
        }

        /// <summary>
        /// Creates a lookup table to flip the image negative-positive.
        /// </summary>
        /// <param name="red">The red threshold value.</param>
        /// <param name="green">The green threshold value.</param>
        /// <param name="blue">The blue threshold value.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Negaposi(byte red = 255, byte green = 255, byte blue = 255)
        {
            if (red == green && green == blue) return Negaposi(red);

            var table = new LookupTable(256, 256, LookupTableDimension.ThreeDimension);
            var rData = (float*)table.GetPointer(0);
            var gData = (float*)table.GetPointer(1);
            var bData = (float*)table.GetPointer(2);
            Image.PixelOperate(256, new Negaposi3DOperation(red, green, blue, rData, gData, bData));

            return table;
        }

        /// <summary>
        /// Creates a lookup table to adjust the contrast.
        /// </summary>
        /// <param name="contrast">The contrast [range: -255-255].</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Contrast(short contrast)
        {
            contrast = Math.Clamp(contrast, (short)-255, (short)255);
            var table = new LookupTable();

            Image.PixelOperate(256, new ContrastOperation(contrast, (float*)table.GetPointer()));

            return table;
        }

        /// <summary>
        /// Creates a lookup table to adjust the contrast.
        /// </summary>
        /// <param name="gamma">The gamma. [range: 0.01-3.0].</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Gamma(float gamma)
        {
            gamma = Math.Clamp(gamma, 0.01f, 3f);
            var table = new LookupTable();

            Image.PixelOperate(256, new GammaOperation(gamma, (float*)table.GetPointer()));

            return table;
        }

        /// <summary>
        /// Creates a lookup table from a Cube file.
        /// </summary>
        /// <param name="stream">The stream to create the lookup table.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable FromStream(Stream stream)
        {
            using var reader = new StreamReader(stream);
            var i = 0;
            ReadInfo(reader, out _, out var dim, out var size, out _, out _);

            var length = (int)Math.Pow(size, (int)dim);
            var table = new LookupTable(length, size, dim);
            var rData = (float*)table.GetPointer(0);
            var gData = (float*)table.GetPointer(1);
            var bData = (float*)table.GetPointer(2);

            while (i < length)
            {
                var line = reader.ReadLine();
                if (line is not null)
                {
                    var values = line.Split(' ');
                    if (values.Length is not 3) continue;

                    if (float.TryParse(values[0], out var r) &&
                        float.TryParse(values[1], out var g) &&
                        float.TryParse(values[2], out var b))
                    {
                        rData[i] = r;
                        gData[i] = g;
                        bData[i] = b;
                        i++;
                    }
                }
            }

            return table;
        }

        /// <summary>
        /// Creates a lookup table from a Cube file.
        /// </summary>
        /// <param name="file">The Cube file to create the lookup table.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable FromCube(string file)
        {
            using var stream = new FileStream(file, FileMode.Open);

            return FromStream(stream);
        }

        /// <summary>
        /// Creates a new span over a lookup table.
        /// </summary>
        /// <param name="dimension">The dimension of the table.</param>
        /// <returns> The span representation of the lookup table.</returns>
        public Span<float> AsSpan(int dimension = 0)
        {
            return _arrays[dimension].AsSpan();
        }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        /// <param name="dimension">The dimension of the table.</param>
        /// <returns>Returns the pointer.</returns>
        public IntPtr GetPointer(int dimension = 0)
        {
            return _arrays[dimension].Pointer;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                foreach (var item in _arrays)
                {
                    item.Dispose();
                }

                GC.SuppressFinalize(this);
                IsDisposed = true;
            }
        }

        private static void ReadInfo(StreamReader reader, out string title, out LookupTableDimension dim, out int size, out Vector3 min, out Vector3 max)
        {
            title = string.Empty;
            dim = LookupTableDimension.ThreeDimension;
            size = 33;
            min = new(0, 0, 0);
            max = new(1, 1, 1);
            var titleFound = false;
            var lutSizeFound = false;
            var minFound = false;
            var maxFound = false;

            while (!reader.EndOfStream)
            {
                if (titleFound && lutSizeFound && minFound && maxFound) break;
                var line = reader.ReadLine();
                if (line is not null)
                {
                    if (_lutSizeReg.IsMatch(line))
                    {
                        lutSizeFound = true;
                        var match = _lutSizeReg.Match(line);
                        size = int.Parse(match.Groups["size"].Value);
                        dim = match.Groups["dim"].Value is "3D" ? LookupTableDimension.ThreeDimension : LookupTableDimension.OneDimension;
                    }
                    else if (_titleReg.IsMatch(line))
                    {
                        titleFound = true;
                        var match = _lutSizeReg.Match(line);
                        title = match.Groups["text"].Value;
                    }
                    else if (_domainMaxReg.IsMatch(line))
                    {
                        maxFound = true;
                        var match = _domainMaxReg.Match(line);
                        var r = float.Parse(match.Groups["red"].Value);
                        var g = float.Parse(match.Groups["green"].Value);
                        var b = float.Parse(match.Groups["blue"].Value);
                        max = new(r, g, b);
                    }
                    else if (_domainMinReg.IsMatch(line))
                    {
                        minFound = true;
                        var match = _domainMinReg.Match(line);
                        var r = float.Parse(match.Groups["red"].Value);
                        var g = float.Parse(match.Groups["green"].Value);
                        var b = float.Parse(match.Groups["blue"].Value);
                        min = new(r, g, b);
                    }
                }
            }

            reader.BaseStream.Position = 0;
        }
    }
}