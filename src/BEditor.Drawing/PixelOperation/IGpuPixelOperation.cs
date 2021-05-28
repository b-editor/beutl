// IGpuPixelOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Represents a pixel operation using a Gpu.
    /// </summary>
    public interface IGpuPixelOperation
    {
        // C# 10 の static virtual 使う

        /// <summary>
        /// Gets the OpenCL C program.
        /// </summary>
        /// <returns>Returns the source code of the program.</returns>
        public string GetSource();

        /// <summary>
        /// Gets the name of the kernel to run.
        /// </summary>
        /// <returns>Returns the kernel name.</returns>
        public string GetKernel();
    }

    /// <inheritdoc cref="IGpuPixelOperation"/>
    /// <typeparam name="T">The type of first argument.</typeparam>
    public interface IGpuPixelOperation<T>
        where T : notnull
    {
        /// <inheritdoc cref="IGpuPixelOperation.GetSource"/>
        public string GetSource();

        /// <inheritdoc cref="IGpuPixelOperation.GetKernel"/>
        public string GetKernel();
    }

    /// <inheritdoc cref="IGpuPixelOperation"/>
    /// <typeparam name="T1">The type of first argument.</typeparam>
    /// <typeparam name="T2">The type of second argument.</typeparam>
    public interface IGpuPixelOperation<T1, T2>
        where T1 : notnull
        where T2 : notnull
    {
        /// <inheritdoc cref="IGpuPixelOperation.GetSource"/>
        public string GetSource();

        /// <inheritdoc cref="IGpuPixelOperation.GetKernel"/>
        public string GetKernel();
    }

    /// <inheritdoc cref="IGpuPixelOperation"/>
    /// <typeparam name="T1">The type of first argument.</typeparam>
    /// <typeparam name="T2">The type of second argument.</typeparam>
    /// <typeparam name="T3">The type of third argument.</typeparam>
    public interface IGpuPixelOperation<T1, T2, T3>
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        /// <inheritdoc cref="IGpuPixelOperation.GetSource"/>
        public string GetSource();

        /// <inheritdoc cref="IGpuPixelOperation.GetKernel"/>
        public string GetKernel();
    }

    /// <inheritdoc cref="IGpuPixelOperation"/>
    /// <typeparam name="T1">The type of first argument.</typeparam>
    /// <typeparam name="T2">The type of second argument.</typeparam>
    /// <typeparam name="T3">The type of third argument.</typeparam>
    /// <typeparam name="T4">The type of fourth argument.</typeparam>
    public interface IGpuPixelOperation<T1, T2, T3, T4>
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        where T4 : notnull
    {
        /// <inheritdoc cref="IGpuPixelOperation.GetSource"/>
        public string GetSource();

        /// <inheritdoc cref="IGpuPixelOperation.GetKernel"/>
        public string GetKernel();
    }
}