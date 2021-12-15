namespace BEditorNext.Services.Editors;

public class NumberEditorService
{
    public static readonly NumberEditorService Instance = new();
    private readonly Dictionary<Type, object> _editors = new()
    {
        { typeof(byte), new ByteEditorService() },
        { typeof(decimal), new DecimalEditorService() },
        { typeof(double), new DoubleEditorService() },
        { typeof(float), new FloatEditorService() },
        { typeof(short), new Int16EditorService() },
        { typeof(int), new Int32EditorService() },
        { typeof(long), new Int64EditorService() },
        { typeof(sbyte), new SByteEditorService() },
        { typeof(ushort), new UInt16EditorService() },
        { typeof(uint), new UInt32EditorService() },
        { typeof(ulong), new UInt64EditorService() },
    };

    public INumberEditorService<T> Get<T>()
        where T : struct
    {
        return (INumberEditorService<T>)_editors[typeof(T)];
    }
}
