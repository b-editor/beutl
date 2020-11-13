using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace BEditor.Core.Exceptions
{
    public class IntPtrZeroException : ArgumentException
    {
        public IntPtrZeroException(string? paramName) : base("", paramName) { }
        public IntPtrZeroException(string? message, string? paramName) : base(message, paramName) { }
    }
}
