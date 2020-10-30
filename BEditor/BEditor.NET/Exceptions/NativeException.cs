using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace BEditor.NET.Exceptions {
    public class NativeException : Exception {
        public NativeException(string? message) : base(message) { }
    }
}
