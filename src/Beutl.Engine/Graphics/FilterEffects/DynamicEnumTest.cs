#if DEBUG

using System.Text.Json.Nodes;

using Beutl.Collections;

namespace Beutl.Graphics.Effects;

public sealed class DynamicEnumTest : FilterEffect
{
    public static readonly CoreProperty<MyDynamicEnum> DynamicEnumProperty;
    private MyDynamicEnum _dynamicEnum = new();

    static DynamicEnumTest()
    {
        DynamicEnumProperty = ConfigureProperty<MyDynamicEnum, DynamicEnumTest>(nameof(DynamicEnum))
            .Accessor(o => o.DynamicEnum, (o, v) => o.DynamicEnum = v)
            .Register();

        AffectsRender<DynamicEnumTest>(DynamicEnumProperty);
    }

    public MyDynamicEnum DynamicEnum
    {
        get => _dynamicEnum;
        set => SetAndRaise(DynamicEnumProperty, ref _dynamicEnum, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
    }

    public sealed class MyDynamicEnumValue(string display/*, object? metadata*/) : IDynamicEnumValue
    {
        public string DisplayName { get; } = display;

        public bool Equals(IDynamicEnumValue? other)
        {
            return other is MyDynamicEnumValue @enum && @enum.DisplayName == DisplayName;
        }
    }

    public sealed class MyDynamicEnum : IDynamicEnum
    {
        private static readonly CoreList<IDynamicEnumValue> s_enumValues =
        [
            new MyDynamicEnumValue("Enum 1"),
            new MyDynamicEnumValue("Enum 2"),
            new MyDynamicEnumValue("Enum 3"),
            new MyDynamicEnumValue("Enum 4"),
        ];

        public MyDynamicEnum()
        {
            SelectedValue = s_enumValues[0];
        }

        public MyDynamicEnum(IDynamicEnumValue? selectedValue)
        {
            SelectedValue = selectedValue;
        }

        // 選択されている値
        public IDynamicEnumValue? SelectedValue { get; private set; }

        // 選択する候補を返す
        public ICoreReadOnlyList<IDynamicEnumValue> Values => s_enumValues;

        // 新しい値で初期化
        public IDynamicEnum WithNewValue(IDynamicEnumValue? value)
        {
            return new MyDynamicEnum(value);
        }

        // 通常はEnumに関連付けられた情報を復元する
        public void ReadFromJson(JsonObject json)
        {
            if (json[nameof(SelectedValue)] is JsonValue val
                && val.TryGetValue(out string? str))
            {
                SelectedValue = s_enumValues.FirstOrDefault(v => v.DisplayName == str);
            }
        }

        public void WriteToJson(JsonObject json)
        {
            json[nameof(SelectedValue)] = SelectedValue?.DisplayName;
        }
    }
}
#endif
