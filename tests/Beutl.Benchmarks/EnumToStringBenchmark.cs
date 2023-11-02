using BenchmarkDotNet.Attributes;

namespace Beutl.Benchmarks;

#pragma warning disable CA1822 // メンバーを static に設定します
public class EnumToStringBenchmark
{
    [Benchmark]
    public void ObjectToString()
    {
        MyEnum a = MyEnum.Alpha;
        MyEnum b = MyEnum.Beta;
        MyEnum g = MyEnum.Gamma;
        MyEnum d = MyEnum.Delta;

        for (int i = 0; i < 500; i++)
        {
            _ = a.ToString();
            _ = b.ToString();
            _ = g.ToString();
            _ = d.ToString();
        }
    }

    [Benchmark]
    public void ToStringEx()
    {
        MyEnum a = MyEnum.Alpha;
        MyEnum b = MyEnum.Beta;
        MyEnum g = MyEnum.Gamma;
        MyEnum d = MyEnum.Delta;

        for (int i = 0; i < 500; i++)
        {
            _ = ToStringEx(a);
            _ = ToStringEx(b);
            _ = ToStringEx(g);
            _ = ToStringEx(d);
        }
    }

    public enum MyEnum
    {
        Alpha,
        Beta,
        Gamma,
        Delta
    }

    private static string ToStringEx(MyEnum myEnum)
    {
        return myEnum switch
        {
            MyEnum.Alpha => nameof(MyEnum.Alpha),
            MyEnum.Beta => nameof(MyEnum.Beta),
            MyEnum.Gamma => nameof(MyEnum.Gamma),
            MyEnum.Delta => nameof(MyEnum.Delta),
            _ => myEnum.ToString(),
        };
    }
}
