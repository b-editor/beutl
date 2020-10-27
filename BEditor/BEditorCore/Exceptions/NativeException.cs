using System;
using System.Collections.Generic;
using System.Text;

#nullable enable

namespace BEditorCore.Exceptions {
    public class NativeException : Exception {
        public NativeException(string? message) : base(message) { }
    }
}
