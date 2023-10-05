namespace Beutl.Serialization;

[Flags]
public enum CoreSerializationMode
{
    Read = 1,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}
