using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Beutl.Views.Tools;

public partial class AiPromptChoiceListView : UserControl
{
    /// <summary>
    /// Which of the library's two lists this instance shows. The commands come
    /// from the shared view model either way, so only the items differ.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> ChoicesProperty =
        AvaloniaProperty.Register<AiPromptChoiceListView, IEnumerable?>(nameof(Choices));

    public AiPromptChoiceListView()
    {
        InitializeComponent();
    }

    public IEnumerable? Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }
}
