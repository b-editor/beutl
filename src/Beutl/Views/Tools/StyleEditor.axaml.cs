using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Commands;
using Beutl.ViewModels.Tools;

using FluentAvalonia.UI.Controls;

using Reactive.Bindings;

namespace Beutl.Views.Tools;

public sealed partial class StyleEditor : UserControl
{
    private IDisposable? _disposable;

    public StyleEditor()
    {
        InitializeComponent();
        targetTypeBox.ItemSelector = (_, obj) => obj.ToString() ?? "";
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
                        if (!item.Property.OwnerType.IsAssignableTo(type))
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
                                Title = Strings.RemoveUnavailableSetters,
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
                                PrimaryButtonText = Strings.Yes,
                                CloseButtonText = Strings.No
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
            CoreProperty[] props = PropertyRegistry.GetRegistered(style.TargetType).ToArray();

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
                Title = Strings.AddSetter,
                Content = listBox,
                PrimaryButtonText = Strings.Add,
                CloseButtonText = Strings.Cancel
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
                    style.Setters.BeginRecord<Styling.ISetter>()
                        .Add(setter)
                        .ToCommand()
                        .DoAndRecord(CommandRecorder.Default);
                }
            }
        }
    }

    private sealed class RemoveAllCommand<T> : IRecordableCommand
    {
        public RemoveAllCommand(IList<T> list, IReadOnlyList<T> items)
        {
            List = list;
            Items = items;
            Indices = new int[items.Count];

            for (int i = 0; i < items.Count; i++)
            {
                Indices[i] = list.IndexOf(items[i]);
            }

            Array.Sort(Indices);
        }

        public IList<T> List { get; }

        public IReadOnlyList<T> Items { get; }

        public int[] Indices { get; }

        public void Do()
        {
            for (int i = Indices.Length - 1; i >= 0; i--)
            {
                List.RemoveAt(Indices[i]);
            }
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            for (int i = 0; i < Indices.Length; i++)
            {
                List.Insert(Indices[i], Items[i]);
            }
        }
    }

}
