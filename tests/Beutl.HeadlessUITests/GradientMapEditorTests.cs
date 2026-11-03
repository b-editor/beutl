using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class GradientMapEditorTests
{
    [AvaloniaTest]
    public void The_stops_are_edited_on_the_gradient_bar_and_the_settings_have_their_own_editors()
    {
        var effect = new GradientMap();
        var models = new List<PropertyEditorViewModel>();
        var panel = new StackPanel();
        var window = new Window { Width = 320, Height = 420, Content = panel };
        try
        {
            Control stops = Add(effect.GradientStops);
            Add(effect.InterpolationSpace);
            Add(effect.InterpolatePremultiplied);
            Add(effect.InputBlack);
            Add(effect.InputWhite);
            Add(effect.Strength);

            window.Show();
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(window, nameof(GradientMapEditorTests), "editors");

            Assert.Multiple(() =>
            {
                Assert.That(stops, Is.TypeOf<GradientStopsEditor>());
                Assert.That(panel.Children, Has.All.Matches<Control>(control => control is { Bounds.Width: > 0, Bounds.Height: > 0 }));
            });
        }
        finally
        {
            window.Close();
            foreach (PropertyEditorViewModel model in models) model.Dispose();
        }

        Control Add(IProperty property)
        {
            var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(property, effect));
            models.Add(model);
            panel.Children.Add(control);
            return control;
        }
    }
}
