using System;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.ViewModels.Properties;
using BEditor.Views.CustomTitlebars;
using BEditor.Views.DialogContent;

namespace BEditor.Views.Properties
{
    public sealed class DialogPropertyView : UserControl
    {
        private static readonly ViewBuilder.PropertyViewBuilder builder;
        public static readonly EditingProperty<Control> DialogProperty = EditingProperty.Register<Control, DialogProperty>("GetDialog");
        private readonly DialogProperty _property;

        static DialogPropertyView()
        {
            builder = ViewBuilder.PropertyViewBuilders.Find(builder => builder.PropertyType == typeof(Group))!;
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
                    var child = builder.CreateFunc(property);
                    Grid.SetRow(child, 1);

                    property[DialogProperty] = new Grid
                    {
                        RowDefinitions =
                        {
                            new(1, GridUnitType.Auto),
                            new(1, GridUnitType.Star)
                        },
                        Children =
                        {
                            new WindowsTitlebarButtons { CanResize = false },
                            child
                        }
                    };
                }
                var content = property.GetValue(DialogProperty);
                if (content.Parent is Decorator parent) parent.Child = null;

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