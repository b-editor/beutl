using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.ViewModels;

namespace BEditor.Models
{
    public class ProjectConfig
    {
        public static readonly AttachedProperty<ConfigurationViewModel.BackgroundType> BackgroundTypeProperty
            = EditingProperty.RegisterAttached<ConfigurationViewModel.BackgroundType, Project>(
                "BackgroundType",
                EditingPropertyOptions<ConfigurationViewModel.BackgroundType>.Create()
                .Initialize(() => ConfigurationViewModel.BackgroundType.Transparent)
                .Notify(true));

        public ConfigurationViewModel.BackgroundType BackgroundType { get; set; } = ConfigurationViewModel.BackgroundType.Transparent;

        public static void SetBackgroundType(Project project, ConfigurationViewModel.BackgroundType value)
        {
            project.SetValue(BackgroundTypeProperty, value);
        }

        public static ConfigurationViewModel.BackgroundType GetBackgroundType(Project project)
        {
            return project.GetValue(BackgroundTypeProperty);
        }
    }
}