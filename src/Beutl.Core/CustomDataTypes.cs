using System.ComponentModel.DataAnnotations;

namespace Beutl;

// Values for [DataType(string)] that Beutl's property editors understand, beside the DataType enum's own.
public static class CustomDataTypes
{
    /// <summary>
    /// Source code, such as a shader or a script. It is edited like <see cref="DataType.MultilineText"/>, but in a
    /// monospace face that keeps its lines unwrapped, and Tab indents instead of moving focus.
    /// </summary>
    public const string Code = "Code";

    /// <summary>Whether the attribute marks <see cref="Code"/>.</summary>
    public static bool IsCode(this DataTypeAttribute attribute)
        => attribute is { DataType: DataType.Custom, CustomDataType: Code };
}
