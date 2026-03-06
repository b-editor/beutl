using System.Reactive.Linq;
using Beutl.Controls.PropertyEditors;
using DynamicData;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public class TargetPickerFlyoutViewModel
{
    private PinnableLibraryItem[] _allItems = [];

    public TargetPickerFlyoutViewModel()
    {
        SearchText
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(100))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => ProcessSearchText());
    }

    public ReactiveCollection<PinnableLibraryItem> Items { get; } = [];

    public ReactiveProperty<string?> SearchText { get; } = new();

    public ReactiveProperty<PinnableLibraryItem?> SelectedItem { get; } = new();

    public void Initialize(IReadOnlyList<TargetObjectInfo> targets)
    {
        _allItems = targets
            .Select(t => new PinnableLibraryItem(t.DisplayName, false, t))
            .ToArray();

        ProcessSearchText();
    }

    private void ProcessSearchText()
    {
        Items.ClearOnScheduler();

        if (string.IsNullOrWhiteSpace(SearchText.Value))
        {
            Items.AddRange(_allItems);
        }
        else
        {
            string[] segments = SearchText.Value.Split(' ')
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            var filtered = _allItems
                .Where(x => segments.Any(s =>
                    x.DisplayName.Contains(s, StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Items.AddRange(filtered);
        }
    }
}
