using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Core.Data;
using BEditor.Models;

using MatBlazor;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BEditor.Shared
{
    public partial class MainLayout
    {
        private bool SnackbarIsOpened = false;
        private string SnackbarText = "";
        private MatDrawer Drawer;
        private ElementReference Image;
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

                var buffers = new byte[file.Size];
                await file.OpenReadStream().ReadAsync(buffers);

                using var stream = new MemoryStream(buffers);

                AppData.Current.Project.Value = new Project(stream, AppData.Current);

                await this.InvokeAsync(StateHasChanged);
            }
        }
    }
}
