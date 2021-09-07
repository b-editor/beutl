
using BEditor.Data;
using BEditor.ViewModels;

namespace BEditor.Models
{
    public sealed class ProjectConfig
    {
        public static readonly AttachedProperty<ConfigurationViewModel.BackgroundType> BackgroundTypeProperty
            = EditingProperty.RegisterAttached<ConfigurationViewModel.BackgroundType, Project>(
                "BackgroundType",
                EditingPropertyOptions<ConfigurationViewModel.BackgroundType>.Create()
                .Initialize(() => ConfigurationViewModel.BackgroundType.Transparent)
                .Notify(true));

        public static readonly AttachedProperty<double> SpeedProperty
            = EditingProperty.RegisterAttached<double, Project>(
                "Speed",
                EditingPropertyOptions<double>.Create()
                .Initialize(() => 1)
                .Notify(true));

        public ConfigurationViewModel.BackgroundType BackgroundType { get; set; } = ConfigurationViewModel.BackgroundType.Transparent;

        public double Speed { get; set; } = 1;

        public static void SetSpeed(Project project, double value)
        {
            project.SetValue(SpeedProperty, value);
        }

        public static double GetSpeed(Project project)
        {
            return project.GetValue(SpeedProperty);
        }

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