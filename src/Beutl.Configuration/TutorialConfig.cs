using System.ComponentModel;

using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Configuration;

[DroppedMembers("ShowTutorialsOnStartup")]
public sealed class TutorialConfig : ConfigurationBase
{
    public TutorialConfig()
    {
        ScanProperties<TutorialConfig>();
    }

    public IProperty<CoreList<string>> CompletedTutorialIdsProperty { get; } = new ValueOnlyProperty<CoreList<string>>([]);

    public CoreList<string> CompletedTutorialIds
    {
        get => CompletedTutorialIdsProperty.Value;
        set => CompletedTutorialIdsProperty.Value = value;
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
