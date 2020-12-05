using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MatBlazor;

using Microsoft.AspNetCore.Components.Forms;

namespace BEditor.Shared
{
    public partial class MainLayout
    {
        private bool SnackbarIsOpened = false;
        private string SnackbarText = "";
        private MatDrawer Drawer;
        private MatTheme Theme = new()
        {
            OnSurface = "#ffffff",
            Surface = "#424242"
        };

        private void MenuButtonClicked()
        {
            Drawer.Opened = !Drawer.Opened;
        }
        private async void LoadFile(InputFileChangeEventArgs e)
        {
            if (e.FileCount is not 0)
            {
                var files = e.GetMultipleFiles();
                var file = files[0];

                var stream = file.OpenReadStream();
            }
        }
    }
}
