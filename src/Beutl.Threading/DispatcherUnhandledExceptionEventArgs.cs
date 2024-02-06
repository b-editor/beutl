using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Threading;

public class DispatcherUnhandledExceptionEventArgs(Exception exception) : EventArgs
{
    public bool Handled { get; set; }

    public Exception Exception { get; } = exception;
}
