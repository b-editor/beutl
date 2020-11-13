using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace BEditor.Core.Exceptions
{
    public class NativeException : Exception
    {
        public NativeException(string? message) : base(message) { }
    }
}
