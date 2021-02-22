using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Controls;

using BEditor.Data;
using BEditor.Properties;
using BEditor.Views.SettingsControl;
using BEditor.Views.SettingsControl.General;
using BEditor.Views.SettingsControl.Plugins;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

namespace BEditor.ViewModels.SettingsControl
{
    public class SettingsWindowViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs Args = new(nameof(ViewControl));
        private object? viewControl;

        public SettingsWindowViewModel()
        {
            TreeSelectCommand.Subscribe(obj =>
            {
                if (obj is TreeViewChild child)
                {
                    ViewControl = child.Control;
                }
            });


            #region General

            var general = new TreeViewChild(Resources.General, PackIconKind.Settings, new Root());

            // 外観
            general.TreeViewChildren.Add(new(Resources.Appearance, PackIconKind.WindowMaximize, new Appearance()));

            // フォント
            general.TreeViewChildren.Add(new(Resources.Font, PackIconKind.FormatSize, new IncludeFont()));

            // その他
            general.TreeViewChildren.Add(new(Resources.Others, PackIconKind.DotsVertical, new Other()));

            #endregion

            #region Project

            var project = new TreeViewChild(Resources.Project, PackIconKind.File, new ProjectSetting());

            #endregion

            #region Plugins

            var plugins = new TreeViewChild(Resources.Plugins, PackIconKind.Puzzle, new Grid());

            plugins.TreeViewChildren.Add(new(Resources.InstalledPlugins, PackIconKind.None, new InstalledPlugins()));

            plugins.TreeViewChildren.Add(new(Resources.DisabledPlugins, PackIconKind.None, new DisabledPlugins()));

            #endregion

            #region AppInfo

            var appInfo = new TreeViewChild(Resources.Infomation, PackIconKind.Information, new AppInfo());

            #endregion

            #region License

            var license = new TreeViewChild(Resources.License, PackIconKind.License, new Views.SettingsControl.License());

            #endregion

            TreeViewProperty.Add(general);
            TreeViewProperty.Add(project);
            TreeViewProperty.Add(plugins);
            TreeViewProperty.Add(appInfo);
            TreeViewProperty.Add(license);
        }

        /// <summary>
        /// 表示されているコントロール
        /// </summary>
        public object? ViewControl { get => viewControl; set => SetValue(value, ref viewControl, Args); }


        public ReactiveCommand<object> TreeSelectCommand { get; } = new();

        public ObservableCollection<TreeViewChild> TreeViewProperty { get; set; } = new ObservableCollection<TreeViewChild>();
    }

    public record TreeViewChild(string Text, PackIconKind PackIconKind, object? Control)
    {
        public ObservableCollection<TreeViewChild> TreeViewChildren { get; set; } = new ObservableCollection<TreeViewChild>();
    }
}
