using System;
using System.Collections.Generic;
using System.Text;

using BEditor.ViewModels.Helper;
using BEditor.Views;

using BEditor.Core.Data;

namespace BEditor.ViewModels.ToolControl
{
    public class SceneManagerViewModel : BasePropertyChanged
    {

        public SceneManagerViewModel()
        {
            AddScene.Subscribe(() =>
            {
                new CreateSceneWindow().ShowDialog();
            });
        }

        public DelegateCommand AddScene { get; } = new DelegateCommand();
    }
}
