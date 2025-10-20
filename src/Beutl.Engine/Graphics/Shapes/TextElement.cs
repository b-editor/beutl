using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

[DebuggerDisplay("{Text}")]
public class TextElement
{
    [Display(Name = nameof(Strings.FontWeight), ResourceType = typeof(Strings))]
    public FontWeight FontWeight { get; set; } = FontWeight.Regular;

    [Display(Name = nameof(Strings.FontStyle), ResourceType = typeof(Strings))]
    public FontStyle FontStyle { get; set; } = FontStyle.Normal;

    [Display(Name = nameof(Strings.FontFamily), ResourceType = typeof(Strings))]
    public FontFamily FontFamily { get; set; } = FontFamily.Default;

    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Size { get; set; }

    [Display(Name = nameof(Strings.CharactorSpacing), ResourceType = typeof(Strings))]
    public float Spacing { get; set; }

    [Display(Name = nameof(Strings.Text), ResourceType = typeof(Strings))]
    public string Text { get; set; } = string.Empty;

    public Brush.Resource? Brush { get; set; }

    public Pen.Resource? Pen { get; set; }

    public bool IgnoreLineBreaks { get; set; }

    internal int GetFormattedTexts(Span<FormattedText> span, bool startWithNewLine, out bool endWithNewLine)
    {
        int prevIdx = 0;
        int ii = 0;
        bool nextReturn = startWithNewLine;
        endWithNewLine = false;

        for (int i = 0; i < Text.Length; i++)
        {
            char c = Text[i];
            int nextIdx = i + 1;
            bool isReturnCode = c is '\n' or '\r';
            bool isLast = nextIdx == Text.Length;

            if (isReturnCode || isLast)
            {
                FormattedText item = span[ii];
                SetFields(item, new StringSpan(Text, prevIdx, (isReturnCode ? i : nextIdx) - prevIdx));
                item.BeginOnNewLine = nextReturn;
                nextReturn = !IgnoreLineBreaks && isReturnCode;

                ii++;
                if (c is '\r'
                    && nextIdx < Text.Length
                    && Text[nextIdx] is '\n')
                {
                    i++;
                    isLast = (nextIdx + 1) == Text.Length;
                }

                prevIdx = i + 1;

                if (!IgnoreLineBreaks && isReturnCode && isLast)
                {
                    endWithNewLine = true;
                }
            }
        }

        return ii;
    }

    internal int CountElements()
    {
        int count = 0;
        for (int i = 0; i < Text.Length; i++)
        {
            char c = Text[i];
            int nextIdx = i + 1;
            bool isReturnCode = c is '\n' or '\r';
            bool isLast = nextIdx == Text.Length;

            if (isReturnCode || isLast)
            {
                if (c is '\r'
                    && nextIdx < Text.Length
                    && Text[nextIdx] is '\n')
                {
                    i++;
                }

                count++;
            }
        }

        return count;
    }

    private void SetFields(FormattedText text, StringSpan s)
    {
        text.Weight = FontWeight;
        text.Style = FontStyle;
        text.Font = FontFamily;
        text.Size = Size;
        text.Spacing = Spacing;
        text.Text = s;
        text.Brush = Brush;
        text.Pen = Pen;
    }
}

