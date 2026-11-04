using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Editor.Services.Captions;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

// Asks which look the clips of dropped subtitle files take.
internal static class CaptionTemplateChooser
{
    public static async Task<CaptionTemplate?> ShowAsync(
        IReadOnlyList<CaptionTemplate> templates, CaptionTemplate selected, IReadOnlyList<string> files)
    {
        ComboBox picker = CreatePicker(templates, selected);
        var dialog = new FAContentDialog
        {
            Title = Strings.SubtitleFile_Add,
            Content = CreateContent(picker, files),
            PrimaryButtonText = Strings.Add,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Primary
        };

        return await dialog.ShowAsync() == FAContentDialogResult.Primary ? templates[picker.SelectedIndex] : null;
    }

    internal static ComboBox CreatePicker(IReadOnlyList<CaptionTemplate> templates, CaptionTemplate selected)
    {
        var picker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = templates.Select(template => template.Descriptor.Name).ToArray(),
            SelectedIndex = templates.ToList().FindIndex(template => template.Descriptor.Id == selected.Descriptor.Id)
        };
        AutomationProperties.SetName(picker, Strings.AiSubtitle_Template);
        return picker;
    }

    internal static Control CreateContent(ComboBox picker, IReadOnlyList<string> files)
    {
        var description = new TextBlock
        {
            FontSize = 12,
            Text = Strings.AiSubtitle_TemplateDescription,
            TextWrapping = TextWrapping.Wrap
        };
        description.Bind(TextBlock.ForegroundProperty, description.GetResourceObservable("TextFillColorSecondaryBrush"));

        return new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock
                {
                    Margin = new Avalonia.Thickness(0, 0, 0, 12),
                    Text = string.Join(", ", files.Select(Path.GetFileName)),
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = Strings.AiSubtitle_Template },
                picker,
                description
            }
        };
    }
}
