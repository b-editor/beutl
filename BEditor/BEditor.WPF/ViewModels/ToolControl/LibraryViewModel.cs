using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using BEditor.ViewModels.Helper;
using BEditor.ObjectModel.EffectData;

namespace BEditor.ViewModels.ToolControl
{
    public class LibraryViewModel
    {
        #region シングルトン

        public static LibraryViewModel Current { get; } = new LibraryViewModel();
        private LibraryViewModel()
        {
            MouseDownCommand.Subscribe(obj =>
            {
                if (!Trigger)
                {
                    return;
                }
                if (Mouse.LeftButton == MouseButtonState.Pressed)
                {

                    if (!(obj is EffectData select) || select.Type == null)
                    {
                        return;
                    }

                    // ドラッグ開始
                    DataObject dataObject = new DataObject(select);
                    DragDrop.DoDragDrop(App.Current.MainWindow, dataObject, DragDropEffects.Copy);
                }

                Trigger = false;
            });

            MouseMoveCommand.Subscribe(() =>
            {
                Trigger = true;
            });
        }

        #endregion

        private bool Trigger;
        public DelegateCommand MouseMoveCommand { get; } = new DelegateCommand();
        public DelegateCommand<object> MouseDownCommand { get; } = new DelegateCommand<object>();
    }
}
