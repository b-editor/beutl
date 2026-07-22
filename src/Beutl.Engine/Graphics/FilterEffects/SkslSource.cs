using System.Buffers;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;

namespace Beutl.Graphics.Effects;

public enum ShaderDescriptionKind
{
    CurrentPixel,
    WholeSource,
}

public sealed partial class SkslSource
{
    [GeneratedRegex(
        @"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?(?<type>[A-Za-z_][A-Za-z0-9_]*)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<array>\s*\[\s*(?<extent>[^\]]*)\s*\])?\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex UniformRegex();

    [GeneratedRegex(
        @"\buniform\s+(?:(?:lowp|mediump|highp)\s+)?[A-Za-z_][A-Za-z0-9_]*(?:\s*\[[^\]]*\])*\s+[A-Za-z_][A-Za-z0-9_]*(?:\s*\[[^\]]*\])*\s*,",
        RegexOptions.CultureInvariant)]
    private static partial Regex MultiDeclaratorUniformRegex();

    [GeneratedRegex(
        @"\bhalf4\s+apply\s*\(\s*half4\s+(?<parameter>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CurrentPixelEntryRegex();

    [GeneratedRegex(
        @"\bhalf4\s+main\s*\(\s*float2\s+(?<parameter>[A-Za-z_][A-Za-z0-9_]*)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex WholeSourceEntryRegex();

    private readonly IReadOnlyDictionary<string, SkslUniformDeclaration> _uniforms;

    internal SkslSource(string text, ShaderDescriptionKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string normalized = Normalize(text);
        List<SkslToken> tokens = SkslLexer.Tokenize(normalized);
        ValidateBalancedTokens(tokens);
        if (kind == ShaderDescriptionKind.CurrentPixel)
        {
            _uniforms = new CurrentPixelValidator(tokens).Validate();
        }
        else
        {
            _uniforms = ParseUniforms(normalized);
            ValidateWholeSourceEntryPoint(normalized);
        }

        Text = normalized;
        Kind = kind;
        IdentityHash = ComputeHash(normalized);
    }

    public string Text { get; }

    public string IdentityHash { get; }

    public ShaderDescriptionKind Kind { get; }

    internal IReadOnlyDictionary<string, SkslUniformDeclaration> Uniforms => _uniforms;

    private static string Normalize(string source)
    {
        string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        return normalized + "\n";
    }

    private static void ValidateBalancedTokens(IReadOnlyList<SkslToken> tokens)
    {
        int braces = 0;
        int parentheses = 0;
        int brackets = 0;
        foreach (SkslToken token in tokens)
        {
            switch (token.Text)
            {
                case "{":
                    braces++;
                    break;
                case "}":
                    braces--;
                    break;
                case "(":
                    parentheses++;
                    break;
                case ")":
                    parentheses--;
                    break;
                case "[":
                    brackets++;
                    break;
                case "]":
                    brackets--;
                    break;
            }

            if (braces < 0 || parentheses < 0 || brackets < 0)
                throw new ArgumentException("The SkSL source has unbalanced delimiters.", "source");
        }

        if (braces != 0 || parentheses != 0 || brackets != 0)
            throw new ArgumentException("The SkSL source has unbalanced delimiters.", "source");
    }

    private static IReadOnlyDictionary<string, SkslUniformDeclaration> ParseUniforms(string source)
    {
        string stripped = SkslLexer.StripComments(source);
        if (MultiDeclaratorUniformRegex().IsMatch(stripped))
        {
            throw new ArgumentException(
                "Each shader uniform must use its own declaration so binding names can be rewritten safely.",
                nameof(source));
        }

        var result = new Dictionary<string, SkslUniformDeclaration>(StringComparer.Ordinal);
        foreach (Match match in UniformRegex().Matches(stripped))
        {
            string name = match.Groups["name"].Value;
            string type = match.Groups["type"].Value;
            int? extent = null;
            if (match.Groups["array"].Success)
            {
                string value = match.Groups["extent"].Value.Trim();
                if (!int.TryParse(value, out int parsed) || parsed <= 0)
                    throw new ArgumentException("Shader uniform arrays require a positive fixed extent.", nameof(source));
                extent = parsed;
            }

            if (name.StartsWith("__beutl", StringComparison.Ordinal)
                || name.StartsWith("fe", StringComparison.Ordinal) && name.Contains('_', StringComparison.Ordinal))
            {
                throw new ArgumentException($"The shader binding name '{name}' is reserved by the renderer.", nameof(source));
            }

            if (!result.TryAdd(name, new SkslUniformDeclaration(type, extent)))
                throw new ArgumentException($"The shader declares duplicate binding '{name}'.", nameof(source));
        }

        return new ReadOnlyDictionary<string, SkslUniformDeclaration>(result);
    }

    private static void ValidateWholeSourceEntryPoint(string source)
    {
        string stripped = SkslLexer.StripComments(source);
        MatchCollection currentEntries = CurrentPixelEntryRegex().Matches(stripped);
        MatchCollection wholeEntries = WholeSourceEntryRegex().Matches(stripped);
        if (wholeEntries.Count != 1 || currentEntries.Count != 0)
        {
            throw new ArgumentException(
                "A WholeSource shader must define exactly one 'half4 main(float2 coord)' entry point and no apply entry point.",
                nameof(source));
        }
    }

    private sealed class CurrentPixelValidator
    {
        private static readonly HashSet<string> s_precisionQualifiers =
            new(StringComparer.Ordinal) { "lowp", "mediump", "highp" };

        private static readonly HashSet<string> s_valueTypes = new(StringComparer.Ordinal)
        {
            "bool",
            "int", "int2", "int3", "int4",
            "uint", "uint2", "uint3", "uint4",
            "half", "half2", "half3", "half4",
            "float", "float2", "float3", "float4",
            "half2x2", "half3x3", "half4x4",
            "float2x2", "float3x3", "float4x4",
            "mat2", "mat3", "mat4",
        };

        private static readonly HashSet<string> s_uniformTypes = new(StringComparer.Ordinal)
        {
            "bool",
            "int", "int2", "int3", "int4",
            "half", "half2", "half3", "half4",
            "float", "float2", "float3", "float4",
            "half2x2", "half3x3", "half4x4",
            "float2x2", "float3x3", "float4x4",
            "mat2", "mat3", "mat4",
            "shader",
        };

        // Only deterministic value functions whose result is wholly determined by their explicit arguments are
        // accepted. In particular, derivative, sample, coordinate and stage-interface built-ins are absent.
        private static readonly HashSet<string> s_valueBuiltins = new(StringComparer.Ordinal)
        {
            "abs", "acos", "all", "any", "asin", "atan", "atan2",
            "ceil", "clamp", "cos", "cross", "degrees", "determinant", "distance", "dot",
            "equal", "exp", "exp2", "faceforward", "floor", "fract", "frexp",
            "fromLinearSrgb", "inverse", "inversesqrt", "length",
            "lessThan", "lessThanEqual", "log", "log2", "matrixCompMult", "max", "min", "mix", "mod",
            "normalize", "not", "notEqual", "pow", "premul", "radians", "reflect", "refract", "round",
            "saturate", "sign", "sin", "smoothstep", "sqrt", "step", "tan", "toLinearSrgb", "transpose",
            "trunc", "unpremul",
        };

        private static readonly HashSet<string> s_languageKeywords = new(StringComparer.Ordinal)
        {
            "break", "const", "continue", "do", "else", "false", "for", "if", "return", "true",
            "uniform", "while",
        };

        private static readonly HashSet<string> s_forbiddenIdentifiers = new(StringComparer.Ordinal)
        {
            "dFdx", "dFdy", "fwidth",
            "fragCoord", "deviceCoord", "sampleCoord", "sampleCoords",
        };

        private readonly IReadOnlyList<SkslToken> _tokens;
        private readonly Dictionary<string, SkslUniformDeclaration> _uniforms = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SymbolKind> _globals = new(StringComparer.Ordinal);
        private readonly HashSet<string> _allLocalNames = new(StringComparer.Ordinal);
        private readonly List<FunctionDeclaration> _functions = [];
        private readonly List<ExpressionRange> _globalInitializers = [];
        private int _applyCount;

        internal CurrentPixelValidator(IReadOnlyList<SkslToken> tokens)
        {
            _tokens = tokens;
        }

        internal IReadOnlyDictionary<string, SkslUniformDeclaration> Validate()
        {
            ParseTopLevel();
            if (_applyCount != 1)
            {
                throw ValidationError(
                    "A CurrentPixel shader must define exactly one 'half4 apply(half4 color)' entry point.");
            }

            foreach (ExpressionRange initializer in _globalInitializers)
            {
                ValidateIdentifiers(initializer.Start, initializer.End, null);
                ValidateResourceSampling(initializer.Start, initializer.End, null);
            }
            foreach (FunctionDeclaration function in _functions)
                ValidateFunction(function);

            return new ReadOnlyDictionary<string, SkslUniformDeclaration>(_uniforms);
        }

        private void ParseTopLevel()
        {
            int index = 0;
            while (index < _tokens.Count)
            {
                string token = _tokens[index].Text;
                index = token switch
                {
                    "uniform" => ParseUniform(index),
                    "const" => ParseGlobalConstant(index),
                    ";" => throw ValidationError("Empty top-level declarations are not supported by CurrentPixel."),
                    _ => ParseFunction(index),
                };
            }
        }

        private int ParseUniform(int start)
        {
            int index = start + 1;
            SkipPrecision(ref index);
            string type = ReadIdentifier(ref index, "A CurrentPixel uniform requires a supported type.");
            if (!s_uniformTypes.Contains(type))
                throw ValidationError($"CurrentPixel uniform type '{type}' is not supported.");

            string name = ReadIdentifier(ref index, "A CurrentPixel uniform requires one binding name.");
            int? extent = ParseOptionalFixedArray(ref index);
            Expect(index, ";", "Each CurrentPixel uniform must use one complete declaration.");
            index++;

            if (type == "shader" && extent is not null)
                throw ValidationError("CurrentPixel shader-resource arrays are not supported.");
            ValidateDeclaredName(name, allowApply: false);
            AddGlobal(name, type == "shader" ? SymbolKind.Shader : SymbolKind.Value);
            if (!_uniforms.TryAdd(name, new SkslUniformDeclaration(type, extent)))
                throw ValidationError($"The shader declares duplicate binding '{name}'.");
            return index;
        }

        private int ParseGlobalConstant(int start)
        {
            int index = start + 1;
            SkipPrecision(ref index);
            string type = ReadIdentifier(ref index, "A CurrentPixel constant requires a value type.");
            if (!s_valueTypes.Contains(type))
                throw ValidationError($"CurrentPixel constant type '{type}' is not supported.");

            string name = ReadIdentifier(ref index, "A CurrentPixel constant requires one name.");
            _ = ParseOptionalFixedArray(ref index);
            Expect(index, "=", "A top-level CurrentPixel constant requires an initializer.");
            int expressionStart = ++index;
            int end = FindStatementEnd(index);
            if (expressionStart == end)
                throw ValidationError("A top-level CurrentPixel constant requires an initializer.");
            if (ContainsTopLevelComma(expressionStart, end))
                throw ValidationError("Each top-level CurrentPixel constant must use its own declaration.");
            if (ContainsToken(expressionStart, end, "{") || ContainsToken(expressionStart, end, "}"))
                throw ValidationError("Brace initializers are not supported by the CurrentPixel merger.");

            ValidateDeclaredName(name, allowApply: false);
            AddGlobal(name, SymbolKind.Value);
            _globalInitializers.Add(new ExpressionRange(expressionStart, end));
            return end + 1;
        }

        private int ParseFunction(int start)
        {
            int index = start;
            SkipPrecision(ref index);
            string returnType = ReadIdentifier(ref index, "CurrentPixel supports only value-returning helper functions.");
            if (!s_valueTypes.Contains(returnType))
                throw ValidationError($"CurrentPixel function return type '{returnType}' is not supported.");

            string name = ReadIdentifier(ref index, "A CurrentPixel function requires a name.");
            ValidateDeclaredName(name, allowApply: name == "apply");
            Expect(index, "(", "A CurrentPixel top-level declaration must be a complete function definition.");
            int openParameters = index;
            int closeParameters = FindMatching(openParameters, "(", ")");
            index = closeParameters + 1;
            Expect(index, "{", "CurrentPixel function prototypes and mutable global declarations are not supported.");
            int openBody = index;
            int closeBody = FindMatching(openBody, "{", "}");

            var locals = new HashSet<string>(StringComparer.Ordinal);
            List<ParameterDeclaration> parameters = ParseParameters(openParameters + 1, closeParameters, locals);
            if (name == "apply")
            {
                _applyCount++;
                if (returnType != "half4"
                    || parameters.Count != 1
                    || parameters[0] != new ParameterDeclaration("half4", "color", null))
                {
                    throw ValidationError(
                        "The CurrentPixel entry point must be exactly 'half4 apply(half4 color)'.");
                }
            }
            else if (name == "main")
            {
                throw ValidationError("CurrentPixel shaders cannot define a whole-source main entry point.");
            }

            AddGlobal(name, SymbolKind.Function);
            ParseLocalDeclarations(openBody + 1, closeBody, locals);
            _functions.Add(new FunctionDeclaration(openBody + 1, closeBody, locals));
            return closeBody + 1;
        }

        private List<ParameterDeclaration> ParseParameters(
            int start,
            int end,
            HashSet<string> locals)
        {
            var result = new List<ParameterDeclaration>();
            if (start == end)
                return result;

            int segmentStart = start;
            while (segmentStart < end)
            {
                int segmentEnd = FindTopLevelCommaOrEnd(segmentStart, end);
                int index = segmentStart;
                SkipPrecision(ref index, segmentEnd);
                string type = ReadIdentifier(
                    ref index,
                    "CurrentPixel function parameters require an unqualified value type.",
                    segmentEnd);
                if (!s_valueTypes.Contains(type))
                    throw ValidationError($"CurrentPixel function parameter type '{type}' is not supported.");
                string name = ReadIdentifier(
                    ref index,
                    "Each CurrentPixel function parameter requires one name.",
                    segmentEnd);
                int? extent = ParseOptionalFixedArray(ref index, segmentEnd);
                if (index != segmentEnd)
                    throw ValidationError("CurrentPixel function parameters cannot use qualifiers or multi-declarators.");

                AddLocal(name, locals);
                result.Add(new ParameterDeclaration(type, name, extent));
                segmentStart = segmentEnd + 1;
            }

            return result;
        }

        private void ParseLocalDeclarations(int start, int end, HashSet<string> locals)
        {
            int index = start;
            while (index < end)
            {
                if (!TryGetLocalDeclaration(index, start, out int typeIndex, out int nameIndex))
                {
                    index++;
                    continue;
                }

                string type = _tokens[typeIndex].Text;
                if (!s_valueTypes.Contains(type))
                    throw ValidationError($"CurrentPixel local type '{type}' is not supported.");
                string name = _tokens[nameIndex].Text;
                int cursor = nameIndex + 1;
                _ = ParseOptionalFixedArray(ref cursor, end);
                int statementEnd = FindStatementEnd(cursor, end);
                if (ContainsTopLevelComma(cursor, statementEnd))
                    throw ValidationError("Each CurrentPixel local must use its own declaration.");

                if (cursor == statementEnd || _tokens[cursor].Text != "=")
                {
                    throw ValidationError(
                        "CurrentPixel locals require a value-derived initializer and support only fixed array extents.");
                }

                AddLocal(name, locals);
                index = statementEnd + 1;
            }
        }

        private bool TryGetLocalDeclaration(int index, int bodyStart, out int typeIndex, out int nameIndex)
        {
            typeIndex = index;
            nameIndex = -1;
            if (!IsDeclarationStart(index, bodyStart))
                return false;

            if (_tokens[typeIndex].Text == "const")
                typeIndex++;
            if (typeIndex < _tokens.Count && s_precisionQualifiers.Contains(_tokens[typeIndex].Text))
                typeIndex++;
            if (typeIndex + 1 >= _tokens.Count
                || !_tokens[typeIndex].IsIdentifier
                || !s_valueTypes.Contains(_tokens[typeIndex].Text)
                || !_tokens[typeIndex + 1].IsIdentifier)
            {
                return false;
            }

            nameIndex = typeIndex + 1;
            return true;
        }

        private bool IsDeclarationStart(int index, int bodyStart)
        {
            if (index == bodyStart)
                return true;
            string previous = _tokens[index - 1].Text;
            if (previous is "{" or ";" or "}")
                return true;
            return previous == "("
                   && index >= 2
                   && _tokens[index - 2].Text == "for";
        }

        private void ValidateFunction(FunctionDeclaration function)
        {
            ValidateIdentifiers(function.BodyStart, function.BodyEnd, function.Locals);
            ValidateResourceSampling(function.BodyStart, function.BodyEnd, function.Locals);
        }

        private void ValidateResourceSampling(
            int start,
            int end,
            HashSet<string>? locals)
        {
            for (int index = start; index + 2 < end; index++)
            {
                if (_tokens[index].Text != "." || _tokens[index + 1].Text != "eval")
                    continue;

                if (index == start
                    || !_tokens[index - 1].IsIdentifier
                    || Resolve(_tokens[index - 1].Text, locals) != SymbolKind.Shader
                    || _tokens[index + 2].Text != "(")
                {
                    throw ValidationError(
                        "CurrentPixel resource sampling must use a directly declared shader binding followed by '.eval(...)'.");
                }

                int close = FindMatching(index + 2, "(", ")");
                if (close > end)
                    throw ValidationError("A CurrentPixel resource eval call escapes its validated expression.");
                int argumentStart = index + 3;
                if (argumentStart == close || ContainsTopLevelComma(argumentStart, close))
                {
                    throw ValidationError(
                        "CurrentPixel resource eval requires exactly one value-coordinate expression.");
                }
            }
        }

        private void ValidateIdentifiers(int start, int end, HashSet<string>? locals)
        {
            for (int index = start; index < end; index++)
            {
                SkslToken token = _tokens[index];
                if (!token.IsIdentifier)
                    continue;

                string name = token.Text;
                if (name.StartsWith("sk_", StringComparison.Ordinal)
                    || s_forbiddenIdentifiers.Contains(name))
                {
                    throw ValidationError(
                        $"CurrentPixel cannot use coordinate, derivative, or stage built-in '{name}'.");
                }

                if (index > start && _tokens[index - 1].Text == ".")
                {
                    if (name != "eval" && !IsSwizzle(name))
                    {
                        throw ValidationError(
                            $"CurrentPixel member '{name}' is not a value swizzle or restricted resource eval.");
                    }
                    continue;
                }

                if (s_languageKeywords.Contains(name)
                    || s_precisionQualifiers.Contains(name)
                    || s_valueTypes.Contains(name))
                {
                    continue;
                }

                if (s_valueBuiltins.Contains(name))
                {
                    if (index + 1 >= end || _tokens[index + 1].Text != "(")
                        throw ValidationError($"CurrentPixel built-in '{name}' must be called directly.");
                    continue;
                }

                SymbolKind? kind = Resolve(name, locals);
                switch (kind)
                {
                    case SymbolKind.Value:
                        break;
                    case SymbolKind.Function:
                        if (index + 1 >= end || _tokens[index + 1].Text != "(")
                            throw ValidationError("CurrentPixel helper functions cannot be retained as values.");
                        break;
                    case SymbolKind.Shader:
                        if (index + 3 >= end
                            || _tokens[index + 1].Text != "."
                            || _tokens[index + 2].Text != "eval"
                            || _tokens[index + 3].Text != "(")
                        {
                            throw ValidationError(
                                $"CurrentPixel shader resource '{name}' may be used only through restricted '.eval(...)'.");
                        }
                        break;
                    default:
                        throw ValidationError(
                            $"CurrentPixel identifier '{name}' is not a declared value or an allowed deterministic built-in.");
                }
            }
        }

        private SymbolKind? Resolve(string name, HashSet<string>? locals)
        {
            if (locals?.Contains(name) == true)
                return SymbolKind.Value;
            return _globals.TryGetValue(name, out SymbolKind kind) ? kind : null;
        }

        private void AddGlobal(string name, SymbolKind kind)
        {
            if (_allLocalNames.Contains(name) || !_globals.TryAdd(name, kind))
                throw ValidationError($"CurrentPixel declaration '{name}' conflicts with another declaration.");
        }

        private void AddLocal(string name, HashSet<string> locals)
        {
            ValidateDeclaredName(name, allowApply: false);
            if (_globals.ContainsKey(name) || !locals.Add(name))
                throw ValidationError($"CurrentPixel local '{name}' shadows another declaration.");
            _allLocalNames.Add(name);
        }

        private static void ValidateDeclaredName(string name, bool allowApply)
        {
            if (name == "src" || name == "main" || name == "apply" && !allowApply)
            {
                throw ValidationError($"CurrentPixel declaration name '{name}' is reserved by the renderer.");
            }
            if (name.StartsWith("__beutl", StringComparison.Ordinal)
                || name.StartsWith("fe", StringComparison.Ordinal) && name.Contains('_', StringComparison.Ordinal)
                || name.StartsWith("sk_", StringComparison.Ordinal)
                || s_languageKeywords.Contains(name)
                || s_precisionQualifiers.Contains(name)
                || s_valueTypes.Contains(name)
                || s_valueBuiltins.Contains(name)
                || s_forbiddenIdentifiers.Contains(name))
            {
                throw ValidationError($"CurrentPixel declaration name '{name}' cannot be renamed safely.");
            }
        }

        private bool ContainsToken(int start, int end, string value)
        {
            for (int index = start; index < end; index++)
            {
                if (_tokens[index].Text == value)
                    return true;
            }
            return false;
        }

        private int FindStatementEnd(int start, int limit = int.MaxValue)
        {
            int parentheses = 0;
            int brackets = 0;
            int braces = 0;
            int end = Math.Min(limit, _tokens.Count);
            for (int index = start; index < end; index++)
            {
                switch (_tokens[index].Text)
                {
                    case "(": parentheses++; break;
                    case ")":
                        if (parentheses == 0)
                            throw ValidationError("A CurrentPixel declaration crosses its containing scope.");
                        parentheses--;
                        break;
                    case "[": brackets++; break;
                    case "]":
                        if (brackets == 0)
                            throw ValidationError("A CurrentPixel declaration has an invalid array expression.");
                        brackets--;
                        break;
                    case "{": braces++; break;
                    case "}":
                        if (braces == 0)
                            throw ValidationError("A CurrentPixel declaration crosses its containing block.");
                        braces--;
                        break;
                    case ";" when parentheses == 0 && brackets == 0 && braces == 0:
                        return index;
                }
            }

            throw ValidationError("A CurrentPixel declaration must end with ';'.");
        }

        private bool ContainsTopLevelComma(int start, int end)
        {
            int parentheses = 0;
            int brackets = 0;
            int braces = 0;
            for (int index = start; index < end; index++)
            {
                switch (_tokens[index].Text)
                {
                    case "(": parentheses++; break;
                    case ")": parentheses--; break;
                    case "[": brackets++; break;
                    case "]": brackets--; break;
                    case "{": braces++; break;
                    case "}": braces--; break;
                    case "," when parentheses == 0 && brackets == 0 && braces == 0:
                        return true;
                }
            }
            return false;
        }

        private int FindTopLevelCommaOrEnd(int start, int end)
        {
            int parentheses = 0;
            int brackets = 0;
            for (int index = start; index < end; index++)
            {
                switch (_tokens[index].Text)
                {
                    case "(": parentheses++; break;
                    case ")": parentheses--; break;
                    case "[": brackets++; break;
                    case "]": brackets--; break;
                    case "," when parentheses == 0 && brackets == 0:
                        return index;
                }
            }
            return end;
        }

        private int FindMatching(int openIndex, string open, string close)
        {
            Expect(openIndex, open, $"Expected '{open}'.");
            int depth = 0;
            for (int index = openIndex; index < _tokens.Count; index++)
            {
                if (_tokens[index].Text == open)
                    depth++;
                else if (_tokens[index].Text == close && --depth == 0)
                    return index;
            }
            throw ValidationError($"The CurrentPixel source has an unmatched '{open}'.");
        }

        private int? ParseOptionalFixedArray(ref int index, int limit = int.MaxValue)
        {
            int end = Math.Min(limit, _tokens.Count);
            if (index >= end || _tokens[index].Text != "[")
                return null;
            if (index + 2 >= end
                || !int.TryParse(_tokens[index + 1].Text, out int extent)
                || extent <= 0
                || _tokens[index + 2].Text != "]")
            {
                throw ValidationError("CurrentPixel arrays require one positive fixed integer extent.");
            }
            index += 3;
            return extent;
        }

        private void SkipPrecision(ref int index, int limit = int.MaxValue)
        {
            if (index < Math.Min(limit, _tokens.Count) && s_precisionQualifiers.Contains(_tokens[index].Text))
                index++;
        }

        private string ReadIdentifier(ref int index, string message, int limit = int.MaxValue)
        {
            if (index >= Math.Min(limit, _tokens.Count) || !_tokens[index].IsIdentifier)
                throw ValidationError(message);
            return _tokens[index++].Text;
        }

        private void Expect(int index, string expected, string message)
        {
            if (index >= _tokens.Count || _tokens[index].Text != expected)
                throw ValidationError(message);
        }

        private static bool IsSwizzle(string name)
        {
            if (name.Length is < 1 or > 4)
                return false;
            return IsSwizzleAlphabet(name, "xyzw")
                   || IsSwizzleAlphabet(name, "rgba")
                   || IsSwizzleAlphabet(name, "stpq");
        }

        private static bool IsSwizzleAlphabet(string value, string alphabet)
        {
            foreach (char current in value)
            {
                if (!alphabet.Contains(current, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static ArgumentException ValidationError(string message)
            => new(message, "source");

        private enum SymbolKind
        {
            Value,
            Shader,
            Function,
        }

        private readonly record struct ParameterDeclaration(string Type, string Name, int? ArrayExtent);

        private readonly record struct ExpressionRange(int Start, int End);

        private sealed record FunctionDeclaration(int BodyStart, int BodyEnd, HashSet<string> Locals);
    }

    private static string ComputeHash(string source)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        const int stackBufferSize = 512;
        int byteCount = Encoding.UTF8.GetByteCount(source);
        byte[]? rented = null;
        Span<byte> bytes = byteCount <= stackBufferSize
            ? stackalloc byte[byteCount]
            : (rented = ArrayPool<byte>.Shared.Rent(byteCount));
        ulong hash = offset;
        try
        {
            int written = Encoding.UTF8.GetBytes(source, bytes);
            foreach (byte value in bytes[..written])
            {
                hash ^= value;
                hash *= prime;
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }

        return hash.ToString("x16");
    }
}

internal readonly record struct SkslUniformDeclaration(string Type, int? ArrayExtent)
{
    public bool IsShader => Type == "shader";
}
