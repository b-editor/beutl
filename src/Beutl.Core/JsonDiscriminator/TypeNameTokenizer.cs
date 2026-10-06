namespace Beutl.JsonDiscriminator;

internal class TypeNameTokenizer(string s)
{
    public List<Token> Tokenize()
    {
        var list = new List<Token>();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (IsKigou(c))
            {
                switch (c)
                {
                    case '[':
                        list.Add(new(TokenType.BeginAssembly));
                        break;
                    case ']':
                        list.Add(new(TokenType.EndAssembly));
                        break;
                    case ':':
                        list.Add(new(TokenType.Colon));
                        break;
                    case '<':
                        list.Add(new(TokenType.BeginGenericArguments));
                        break;
                    case '>':
                        list.Add(new(TokenType.EndGenericArguments));
                        break;
                    case '.':
                        list.Add(new(TokenType.Period));
                        break;
                    case ',':
                        list.Add(new(TokenType.Comma));
                        break;
                }
            }
            else
            {
                int start = i;
                while (true)
                {
                    c = s[i];

                    if (IsKigou(c))
                    {
                        list.Add(new(TokenType.Part, s.Substring(start, i - start)));
                        i--;
                        break;
                    }

                    i++;
                    if (i >= s.Length)
                    {
                        list.Add(new(TokenType.Part, s.Substring(start, i - start)));
                        break;
                    }
                }
            }
        }

        return list;
    }

    private static bool IsKigou(char c)
    {
        return c switch
        {
            '[' or ']' or ':' or '<' or '>' or '.' or ',' => true,
            _ => false,
        };
    }
}
