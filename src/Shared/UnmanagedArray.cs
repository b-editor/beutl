// UnmanagedArray.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BEditor
{
    internal unsafe class UnmanagedArray<T> : IDisposable, IEnumerable<T>, ICloneable
        where T : unmanaged
    {
        private readonly T* _ptr;
        private readonly int _length;

        public UnmanagedArray(int length)
        {
            _length = length;
            _ptr = (T*)Marshal.AllocCoTaskMem(sizeof(T) * length);
        }

        ~UnmanagedArray()
        {
            Dispose();
        }

        public int Length
        {
            get
            {
                ThrowIfDisposed();
                return _length;
            }
        }

        public IntPtr Pointer
        {
            get
            {
                ThrowIfDisposed();
                return new(_ptr);
            }
        }

        public bool IsDisposed { get; private set; }

        public ref T this[int index]
        {
            get
            {
                ThrowIfDisposed();
                if (index is < 0 || index >= Length) throw new ArgumentOutOfRangeException(nameof(index));

                return ref _ptr[index];
            }
        }

        public Span<T> AsSpan()
        {
            ThrowIfDisposed();
            return new((T*)Pointer, Length);
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            ThrowIfDisposed();
            return new ArrayEnumerator(this);
        }

        public UnmanagedArray<T> Clone()
        {
            ThrowIfDisposed();
            var array = new UnmanagedArray<T>(Length);
            AsSpan().CopyTo(array.AsSpan());

            return array;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            ThrowIfDisposed();
            return new ArrayEnumerator(this);
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(UnmanagedArray<T>));
        }

        private sealed class ArrayEnumerator : IEnumerator<T>, ICloneable
        {
            private UnmanagedArray<T>? _array;
            private int _index;
            private T _currentElement;

            public ArrayEnumerator(UnmanagedArray<T> array)
            {
                _array = array;
                _index = -1;
            }

            public T Current
            {
                get
                {
                    if (_index == -1)
                        throw new InvalidOperationException();
                    if (_index >= _array!.Length)
                        throw new InvalidOperationException();
                    return _currentElement;
                }
            }

            object? IEnumerator.Current => Current;

            public object Clone()
            {
                return MemberwiseClone();
            }

            public bool MoveNext()
            {
                if (_index < (_array!.Length - 1))
                {
                    _index++;
                    _currentElement = _array[_index];
                    return true;
                }
                else
                {
                    _index = _array.Length;
                }

                return false;
            }

            public void Dispose()
            {
                if (_array != null)
                    _index = _array.Length;
                _array = null;
            }

            public void Reset()
            {
                _currentElement = default;
                _index = -1;
            }
        }
    }
}