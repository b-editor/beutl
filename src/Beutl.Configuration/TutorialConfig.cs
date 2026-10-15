using System.ComponentModel;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

[DroppedMembers("ShowTutorialsOnStartup")]
public sealed class TutorialConfig : ConfigurationBase
{
    public static readonly CoreProperty<CoreList<string>> CompletedTutorialIdsProperty;

    static TutorialConfig()
    {
        CompletedTutorialIdsProperty = ConfigureProperty<CoreList<string>, TutorialConfig>(nameof(CompletedTutorialIds))
            .DefaultValue([])
            .Register();
    }

    public CoreList<string> CompletedTutorialIds
    {
        get => GetValue(CompletedTutorialIdsProperty);
        set => SetValue(CompletedTutorialIdsProperty, value);
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
