using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

using BeUtl.Commands;
using BeUtl.Services;
using BeUtl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace BeUtl.Views.Editors;

public partial class StyleEditor : UserControl
{
    private IDisposable? _disposable;

    public StyleEditor()
    {
        Resources["ViewModelToViewConverter"] = ViewModelToViewConverter.Instance;
        InitializeComponent();
        targetTypeBox.ItemSelector = (_, obj) => obj.ToString();
        targetTypeBox.FilterMode = AutoCompleteFilterMode.Contains;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _disposable?.Dispose();
        _disposable = null;
        if (DataContext is StyleEditorViewModel viewModel)
        {
            _disposable = viewModel.TargetType.Skip(1).Subscribe(async type =>
            {
                if (DataContext is StyleEditorViewModel viewModel
                    && type != null
                    && viewModel.Style.Value is Styling.Style style)
                {
                    var mismatches = new List<Styling.ISetter>(style.Setters.Count);
                    foreach (Styling.ISetter item in style.Setters)
                    {
                        if (!item.Property.OwnerType.IsAssignableTo(type)
                            || (item.Property.TryGetMetadata(type, out CorePropertyMetadata? metadata)
                            && !metadata.PropertyFlags.HasFlag(PropertyFlags.Styleable)))
                        {
                            mismatches.Add(item);
                        }
                    }

                    if (mismatches.Count > 0)
                    {
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            var dialog = new ContentDialog
                            {
                                Title = "利用できないセッターを削除しますか？",
                                Content = new ListBox
                                {
                                    Items = mismatches,
                                    ItemTemplate = new FuncDataTemplate<Styling.ISetter>((setter, _) =>
                                    {
                                        return new TextBlock()
                                        {
                                            Text = setter.Property.Name
                                        };
                                    })
                                },
                                PrimaryButtonText = "はい",
                                CloseButtonText = "いいえ"
                            };

                            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                            {
                                var command = new RemoveAllCommand<Styling.ISetter>(style.Setters, mismatches);
                                command.DoAndRecord(CommandRecorder.Default);
                            }
                        });
                    }
                }
            });
        }
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StyleEditorViewModel viewModel && viewModel.Style.Value is Styling.Style style)
        {
            CoreProperty[] props = PropertyRegistry.GetRegistered(style.TargetType)
                .Where(x => x.GetMetadata<CorePropertyMetadata>(style.TargetType).PropertyFlags.HasFlag(PropertyFlags.Styleable))
                .ToArray();

            var selectedItem = new ReactivePropertySlim<CoreProperty?>();
            var listBox = new ListBox
            {
                [!SelectingItemsControl.SelectedItemProperty] = new Binding("Value", BindingMode.TwoWay)
                {
                    Source = selectedItem
                },
                SelectionMode = SelectionMode.Single,
                Items = props,
                ItemTemplate = new FuncDataTemplate<CoreProperty>((prop, _) =>
                {
                    return new TextBlock()
                    {
                        Text = prop.Name
                    };
                })
            };

            var dialog = new ContentDialog
            {
                [!ContentDialog.IsPrimaryButtonEnabledProperty] = new Binding("Value")
                {
                    Source = selectedItem.Select(x => x != null).ToReadOnlyReactivePropertySlim()
                },
                Title = "セッターを追加",
                Content = listBox,
                PrimaryButtonText = "追加",
                CloseButtonText = "キャンセル"
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary
                && selectedItem.Value is CoreProperty property)
            {
                Type setterType = typeof(Styling.Setter<>);
                setterType = setterType.MakeGenericType(property.PropertyType);
                ICorePropertyMetadata metadata = property.GetMetadata<ICorePropertyMetadata>(style.TargetType);
                object? defaultValue = metadata.GetDefaultValue();

                if (Activator.CreateInstance(setterType, property, defaultValue) is Styling.ISetter setter)
                {
                    var command = new AddCommand<Styling.ISetter>(style.Setters, setter, style.Setters.Count);
                    command.DoAndRecord(CommandRecorder.Default);
                }
            }
        }
    }

    private sealed class ViewModelToViewConverter : IValueConverter
    {
        public static readonly ViewModelToViewConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is BaseEditorViewModel viewModel)
            {
                Control? editor = PropertyEditorService.CreateEditor(viewModel.WrappedProperty);

                return editor ?? new Label
                {
                    Height = 24,
                    Margin = new Thickness(0, 4),
                    Content = viewModel.WrappedProperty.AssociatedProperty.Name
                };
            }
            else
            {
                return BindingNotification.Null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return BindingNotification.Null;
        }
    }
}
