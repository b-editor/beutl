using System.Collections;
using System.Text.RegularExpressions;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Generators;
using Avalonia.Data.Converters;
using Avalonia.Styling;

using BeUtl.Media;
using BeUtl.ViewModels.Dialogs;

using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.Dialogs;

public partial class PickFontFamily : ContentDialog, IStyleable
{
    private Regex[]? _regies;

    public PickFontFamily()
    {
        Resources["ToAvaloniaFontFamily"] = new FuncValueConverter<FontFamily, Avalonia.Media.FontFamily>(f => new Avalonia.Media.FontFamily(f.Name));
        InitializeComponent();
        searchBox.GetObservable(TextBox.TextProperty).Subscribe(SearchQueryChanged);
        list.Items = FontManager.Instance.FontFamilies;
    }

    Type IStyleable.StyleKey => typeof(ContentDialog);

    private void SearchQueryChanged(string obj)
    {
        if (string.IsNullOrWhiteSpace(obj))
        {
            _regies = null;
        }
        else
        {
            _regies = RegexHelper.CreateRegices(obj);
        }

        list.Items = FontManager.Instance.FontFamilies.Where(i =>
        {
            if (_regies == null)
            {
                return true;
            }
            else
            {
                return RegexHelper.IsMatch(_regies, i.Name);
            }
        });
    }
}
