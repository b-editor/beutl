using System;
using System.Collections.Generic;
using System.Text;

namespace BEditor.NET.Extesions.ViewCommand {
    public static class Message {
        public static event Func<string, IconType, ButtonType[], ButtonType> DialogFunc;
        public static event Action<string> SnackberFunc;
    
        public static ButtonType? Dialog(string text, IconType icon = IconType.Info, ButtonType[] types = null) {
            if (types is null) types = new ButtonType[] { ButtonType.Ok, ButtonType.Close };

            return DialogFunc?.Invoke(text, icon, types);
        }

        public static void Snackbar(string text = "") => SnackberFunc?.Invoke(text);
    }
}
