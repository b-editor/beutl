namespace Beutl.Controls.PropertyEditors;

public class ColorWheelPicker : SimpleColorPicker
{
    public ColorWheelPicker()
    {
        // Prefer working in HSV for grading-style wheels.
        InputType = SimpleColorPickerInputType.Hsv;
    }
}

