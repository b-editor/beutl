using System;

using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.ViewModels.Properties;
using BEditor.Views.DialogContent;

namespace BEditor.Views.Properties
{
    public sealed class DialogPropertyView : UserControl
    {
        private static readonly ViewBuilder.PropertyViewBuilder _builder;
        public static readonly EditingProperty<Control> DialogProperty = EditingProperty.Register<Control, DialogProperty>("GetDialog");
        private readonly DialogProperty _property;

        static DialogPropertyView()
        {
            _builder = ViewBuilder.PropertyViewBuilders.Find(builder => builder.PropertyType == typeof(Group))!;
        }

#pragma warning disable CS8618
        public DialogPropertyView()
#pragma warning restore CS8618
        {
            InitializeComponent();
        }

        public DialogPropertyView(DialogProperty property)
        {
            DataContext = new DialogPropertyViewModel(property);
            InitializeComponent();
            _property = property;
            property.Showed += Property_Showed;
        }

        private async void Property_Showed(object? sender, EventArgs e)
        {
            static Window GetCreate(DialogProperty property)
            {
                if (property[DialogProperty] is null)
                {
                    property[DialogProperty] = _builder.CreateFunc(property);
                }
                var content = property.GetValue(DialogProperty);
                if (content.Parent is Decorator parent)
                {
                    parent.Child = null;
                }
                else if (content.Parent is ContentControl parent2)
                {
                    parent2.Content = null;
                }
                else if (content.Parent is ContentPresenter parent3)
                {
                    parent3.Content = null;
                }
                else if (content.Parent is Panel parent4)
                {
                    parent4.Children.Remove(content);
                }

                return new EmptyDialog(content)
                {
                    CanResize = true
                };
            }

            await GetCreate(_property).ShowDialog(App.GetMainWindow());
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}