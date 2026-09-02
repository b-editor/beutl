namespace Beutl.Graphics.Effects;

internal readonly record struct SkslToken(
    string Text,
    bool IsIdentifier,
    int BraceDepth,
    int ParenthesisDepth,
    int Start,
    int Length)
{
    public int Depth => BraceDepth + ParenthesisDepth;
}
