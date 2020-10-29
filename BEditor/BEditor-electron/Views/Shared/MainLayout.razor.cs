using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BEditorCore.Data.ProjectData;
using BEditorCore.Extesions.ViewCommand;
using BEditorCore.Properties;
using ElectronNET.API;
using ElectronNET.API.Entities;

namespace BEditor_Electron.Views.Shared {
    public partial class MainLayout {
        private bool MenuOpened = false;
        private bool SnackbarIsOpened = false;
        private string SnackbarText = "";

        protected override void OnInitialized() {
            base.OnInitialized();
            Message.SnackberFunc += (text) => {
                SnackbarText = text;
                SnackbarIsOpened = true;
                this.StateHasChanged();
            };
        }

        private void MenuButtonClicked() {
            MenuOpened = !MenuOpened;
        }

        private void ProjectOpen() {
            Task.Run(async () => {
                var strArray = await Electron.Dialog.ShowOpenDialogAsync(Startup.BrowserWindow, new OpenDialogOptions() {
                    Filters = new FileFilter[] {
                    new FileFilter() { Name = "プロジェクトファイル", Extensions = new string[] { "bedit" } },
                    new FileFilter() { Name = "バックアップファイル", Extensions = new string[] { "backup" } }
                },
                    Properties = new OpenDialogProperty[] { OpenDialogProperty.openFile }
                });

                if (strArray.Length == 0) return;

                if (Project.Open(strArray[0]) == null) {
                    Message.Snackbar(string.Format(Resources.FailedToLoad, "Project"));
                }
                Message.Snackbar("Project loaded");
            });
        }
    }
}
