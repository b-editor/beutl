// LookupTable.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents a lookup table.
    /// </summary>
    public sealed unsafe class LookupTable : IDisposable
    {
        private readonly UnmanagedArray<byte> _array;

        /// <summary>
        /// Initializes a new instance of the <see cref="LookupTable"/> class.
        /// </summary>
        /// <param name="size">The size of the <see cref="LookupTable"/>.</param>
        /// <param name="dim">The dimension of the <see cref="LookupTable"/>.</param>
        public LookupTable(int size = 256, int dim = 1)
        {
            _array = new(size * dim);
            Size = size;
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
        /// Gets the size of this <see cref="LookupTable"/>.
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Gets the dimension of this <see cref="LookupTable"/>.
        /// </summary>
        public int Dimension { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Creates a lookup table to adjust the contrast.
        /// </summary>
        /// <param name="contrast">The contrast [range: -255-255].</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable Contrast(short contrast)
        {
            contrast = Math.Clamp(contrast, (short)-255, (short)255);
            var table = new LookupTable();

            Image.PixelOperate(256, new ContrastOperation(contrast, (byte*)table.GetPointer()));

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

            Image.PixelOperate(256, new GammaOperation(gamma, (byte*)table.GetPointer()));

            return table;
        }

        /// <summary>
        /// Creates a lookup table from a Cube file.
        /// </summary>
        /// <param name="file">The Cube file to create the lookup table.</param>
        /// <param name="lutsize">The size of lookup table.</param>
        /// <returns>Returns the lookup table created by this method.</returns>
        public static LookupTable FromCube(string file, int lutsize = 33)
        {
            var table = new LookupTable(lutsize * lutsize * lutsize, 3);
            using var reader = new StreamReader(file);
            var i = 0;
            var data = (BGR24*)table.GetPointer();

            while (i < table.Size)
            {
                var line = reader.ReadLine();
                if (line is not null)
                {
                    var values = SplitNaive(line);
                    if (values.Count is not 3) continue;

                    data[i].B = (byte)(double.Parse(values[0]) * 255);
                    data[i].G = (byte)(double.Parse(values[1]) * 255);
                    data[i].R = (byte)(double.Parse(values[2]) * 255);
                    i++;
                }
            }

            return table;
        }

        /// <summary>
        /// Creates a new span over a lookup table.
        /// </summary>
        /// <returns> The span representation of the lookup table.</returns>
        public Span<byte> AsSpan()
        {
            return _array.AsSpan();
        }

        /// <summary>
        /// Gets the pointer.
        /// </summary>
        /// <returns>Returns the pointer.</returns>
        public IntPtr GetPointer()
        {
            return _array.Pointer;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                _array.Dispose();
                GC.SuppressFinalize(this);
                IsDisposed = true;
            }
        }

        private static List<string> SplitNaive(string str)
        {
            var list = new List<string>();
            var item = new StringBuilder();
            foreach (var ch in str)
            {
                if (ch == ' ')
                {
                    if (item.Length is not 0)
                    {
                        list.Add(item.ToString());
                    }

                    item.Clear();
                }
                else
                {
                    item.Append(ch);
                }
            }

            if (item.Length is not 0)
            {
                list.Add(item.ToString());
            }

            return list;
        }
    }
}
