namespace Beutl.Media.TextFormatting;

public record struct FormattedTextInfo(
    Typeface Typeface,
    float Size,
    Brush.Resource? Brush,
    float Space,
    Pen.Resource? Pen);
