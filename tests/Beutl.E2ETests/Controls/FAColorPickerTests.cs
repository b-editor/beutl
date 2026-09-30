using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using PickerColorSpectrumComponents = FluentAvalonia.UI.Controls.ColorSpectrumComponents;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class FAColorPickerTests
{
    [AvaloniaTest]
    public void Component_modes_select_their_matching_radio_buttons()
    {
        var picker = new FAColorPicker { IsCompact = false, UseSpectrum = true };
        var window = new Window { Width = 700, Height = 700, Content = picker };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            foreach ((PickerColorSpectrumComponents component, string buttonName) in new[]
            {
                (PickerColorSpectrumComponents.SaturationValue, "HueRadio"),
                (PickerColorSpectrumComponents.ValueHue, "SatRadio"),
                (PickerColorSpectrumComponents.SaturationHue, "ValRadio"),
                (PickerColorSpectrumComponents.BlueGreen, "RedRadio"),
                (PickerColorSpectrumComponents.BlueRed, "GreenRadio"),
                (PickerColorSpectrumComponents.GreenRed, "BlueRadio"),
            })
            {
                picker.Component = component;
                HeadlessTestHelpers.Render();
                RadioButton button = picker.GetVisualDescendants().OfType<RadioButton>()
                    .Single(control => control.Name == buttonName);
                Assert.That(button.IsChecked, Is.True, $"{component} did not select {buttonName}.");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
