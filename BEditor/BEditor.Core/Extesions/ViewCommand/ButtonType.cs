using System;

namespace BEditor.Core.Extesions.ViewCommand {
    [Flags]
    public enum ButtonType {
        Ok = 1,
        Yes = 2,
        No = 4,
        Cancel = 8,
        Retry = 16,
        Close = 32,
    }
}
