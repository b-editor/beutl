using System.Text;

namespace Beutl.JsonDiscriminator;

internal class TypeNameFormatter(Type type)
{
    private void WriteNamespace(StringBuilder sb)
    {
        string? asmName = type.Assembly.GetName().Name;
        string? ns = type.Namespace;
        if (ns != null)
        {
            if (asmName != null)
            {
                if (ns.StartsWith(asmName))
                {
                    ns = ns.Substring(asmName.Length);
                }
            }
        }
        else
        {
            ns = "global:";
        }

        sb.Append(ns);
    }

    public string Format()
    {
        var sb = new StringBuilder();
        string? asmName = type.Assembly.GetName().Name;
        if (asmName != null)
        {
            sb.Append('[');
            sb.Append(asmName);
            sb.Append(']');
        }

        WriteNamespace(sb);

        WriteTypeName(sb, type);

        return sb.ToString();
    }

    private static void WriteGenericArguments(StringBuilder sb, Type type)
    {
        sb.Append('<');
        Type[] array = type.GetGenericArguments();
        for (int i = 0; i < array.Length;)
        {
            Type? item = array[i];
            var formatter = new TypeNameFormatter(item);
            sb.Append(formatter.Format());
            if (++i < array.Length)
            {
                sb.Append(", ");
            }
        }
        sb.Append('>');
    }

    private static void WriteTypeName(StringBuilder sb, Type type)
    {
        Type? declaringType = type.DeclaringType;
        if (type.IsNested && declaringType != null)
        {
            WriteTypeName(sb, declaringType);
        }

        sb.Append(':');
        sb.Append(TrimTypeName(type.Name));
        if (type.IsGenericType)
        {
            WriteGenericArguments(sb, type);
        }
    }

    private static string TrimTypeName(string typeName)
    {
        int idx = typeName.IndexOf('`');
        if (idx < 0)
        {
            return typeName;
        }
        else
        {
            return typeName[..idx];
        }
    }
}
