using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BEditor.Core.Data;
using BEditor.Models;
using BEditor.Views;

using MatBlazor;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace BEditor.Shared
{
    public partial class MainLayout
    {
        public static MainLayout Current { get; private set; }
        private bool FileDialogIsOpened = false;
        public PreviewImage Image;
        private bool SnackbarIsOpened = false;
        private string SnackbarText = "";
        private MatDrawer Drawer;
        private MatTheme Theme = new()
        {
            OnSurface = "#ffffff",
            Surface = "#424242",
            OnPrimary = "#ffffff",
            OnSecondary = "#ffffff"
        };

        protected override void OnInitialized()
        {
            base.OnInitialized();
            Current = this;
        }

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

                AppData.Current.ProjectThread = SynchronizationContext.Current;
                AppData.Current.Project.Value = new Project(stream, AppData.Current);
                AppData.Current.Project.Value.Loaded();

                FileDialogIsOpened = false;
                await this.InvokeAsync(StateHasChanged);
            }
        }
    }
}
