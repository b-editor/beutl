using System;
using System.Collections.Generic;
using System.Text;

using BEditor.ViewModels.Helper;
using BEditor.Views;

using BEditor.Core.Data;
using BEditor.Core.Data.ProjectData;

namespace BEditor.ViewModels.ToolControl {
    public class SceneManagerViewModel : BasePropertyChanged {

        public SceneManagerViewModel() {
            AddScene.Subscribe(() => {
                new CreateSceneWindow().ShowDialog();
            });
            Project.ProjectOpend += (_, _) => RaisePropertyChanged(nameof(GetProject));
            Project.ProjectClosed += (_, _) => RaisePropertyChanged(nameof(GetProject));
        }

        public Project GetProject => Component.Current.Project;

        public DelegateCommand AddScene { get; } = new DelegateCommand();
    }
}
