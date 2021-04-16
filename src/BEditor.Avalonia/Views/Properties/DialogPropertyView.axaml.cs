using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Extensions;
using BEditor.ViewModels.Properties;
using BEditor.Views.DialogContent;

namespace BEditor.Views.Properties
{
    public class DialogPropertyView : UserControl
    {
        private static readonly ViewBuilder.PropertyViewBuilder builder;
        public static readonly EditingProperty<Control> DialogProperty = EditingProperty.Register<Control, DialogProperty>("GetDialog");
        private DialogProperty _property;

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
                    property[DialogProperty] = builder.CreateFunc(property);
                }
                return new EmptyDialog(property.GetValue(DialogProperty));
            }

            await GetCreate(_property).ShowDialog(App.GetMainWindow());
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}