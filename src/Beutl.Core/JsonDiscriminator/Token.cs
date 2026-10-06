namespace Beutl.JsonDiscriminator;

internal class Token
{
    public Token(TokenType type, string? text = null)
    {
        Type = type;
        Text = text;

        if (text == null && type != TokenType.Part)
        {
            Text = type switch
            {
                TokenType.BeginAssembly => "[",
                TokenType.EndAssembly => "]",
                TokenType.Colon => ":",
                TokenType.Period => ".",
                TokenType.BeginGenericArguments => "<",
                TokenType.EndGenericArguments => ">",
                _ => null,
            };
        }
    }

    public TokenType Type { get; }

    public string? Text { get; }
}
