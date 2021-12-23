using System.Collections;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Data.Converters;
using Avalonia.Styling;

using BEditorNext.Media;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;

namespace BEditorNext.Views.Dialogs;

public partial class PickFontFamily : ContentDialog, IStyleable
{
    public PickFontFamily()
    {
        Resources["ToAvaloniaFontFamily"] = new FuncValueConverter<FontFamily, Avalonia.Media.FontFamily>(f => new Avalonia.Media.FontFamily(f.Name));
        InitializeComponent();
        searchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    private void SearchQueryChanged(string obj)
    {
        if (string.IsNullOrWhiteSpace(obj))
        {
            SetVisibility(list.Items, true);
        }
        else
        {
            Regex[] regies = RegexHelper.CreateRegices(obj);
            var items = new List<FontFamily>();

            foreach (FontFamily item in list.Items.OfType<FontFamily>())
            {
                if (RegexHelper.IsMatch(regies, item.Name))
                {
                    items.Add(item);
                }
            }

            SetVisibility(items, true);
        }
    }

    // itemsÇ…ä‹Ç‹ÇÍÇƒÇ¢ÇÈÉAÉCÉeÉÄÇÃVisibilityÇê›íËÇ∑ÇÈ
    private void SetVisibility(IEnumerable items, bool isVisible)
    {
        IItemContainerGenerator container = list.ItemContainerGenerator;
        if (items == null || container == null) return;

        int index = 0;
        foreach (object? item in list.Items)
        {
            IControl control = container.ContainerFromIndex(index);
            if (control != null)
            {
                control.IsVisible = items.Contains(item) ? isVisible : !isVisible;
            }

            index++;
        }
    }
}
