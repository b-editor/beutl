using System.ComponentModel;
using Beutl.Collections;

namespace Beutl.Configuration;

public sealed class TutorialConfig : ConfigurationBase
{
    public static readonly CoreProperty<CoreList<string>> CompletedTutorialIdsProperty;
    public static readonly CoreProperty<bool> ShowTutorialsOnStartupProperty;

    static TutorialConfig()
    {
        CompletedTutorialIdsProperty = ConfigureProperty<CoreList<string>, TutorialConfig>(
                nameof(CompletedTutorialIds)
            )
            .DefaultValue([])
            .Register();

        ShowTutorialsOnStartupProperty = ConfigureProperty<bool, TutorialConfig>(
                nameof(ShowTutorialsOnStartup)
            )
            .DefaultValue(true)
            .Register();
    }

    public CoreList<string> CompletedTutorialIds
    {
        get => GetValue(CompletedTutorialIdsProperty);
        set => SetValue(CompletedTutorialIdsProperty, value);
    }

    public bool ShowTutorialsOnStartup
    {
        get => GetValue(ShowTutorialsOnStartupProperty);
        set => SetValue(ShowTutorialsOnStartupProperty, value);
    }

    public void MarkCompleted(string tutorialId)
    {
        if (!CompletedTutorialIds.Contains(tutorialId))
        {
            CompletedTutorialIds.Add(tutorialId);
            OnChanged();
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
