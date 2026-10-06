namespace Beutl.JsonDiscriminator;

/*
 * 名前空間とアセンブリ名が同じ場合
 * [Beutl.Graphics]:Point
 *
 * 型がグローバル空間にある場合
 * [Beutl.Graphics]global::Point
 *
 * 名前空間とアセンブリ名が途中まで同じ場合
 * [Beutl.Graphics].Shapes:Ellipse
 *
 * 名前空間とアセンブリ名が一致しない場合
 * [Beutl.Graphics]Beutl.Audio:Sound
 *
 * ジェネリック引数がある場合
 * [System.Collections].Generic:List<[System.Runtime]System:Int32>
 *
 * 入れ子になったクラス
 * [System.Net.Mail]System.Net.Mime:MediaTypeNames:Application
 */
internal enum TokenType
{
    BeginAssembly,

    EndAssembly,

    // Colon
    Colon,

    Period,

    Comma,

    BeginGenericArguments,

    EndGenericArguments,

    Part,
}
