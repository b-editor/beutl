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

    public IProperty<CoreList<string>> CompletedTutorialIds { get; } = new ValueOnlyProperty<CoreList<string>>([]);

    public void MarkCompleted(string tutorialId)
    {
        if (!CompletedTutorialIds.Value.Contains(tutorialId))
        {
            CompletedTutorialIds.Value.Add(tutorialId);
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
