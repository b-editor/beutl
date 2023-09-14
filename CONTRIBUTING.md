## Beutlへの貢献についてのガイドライン

### Pull request
既に作業中である可能性もあるので、
PRを送る前にIssueを開くことをおすすめします。

変更内容が少ない場合はIssueを開かなくても良いです。

履歴を見やすくするために
**リベースと強制プッシュを忘れないで下さい。**

### コードガイドライン

[.NETのコードスタイル](https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)を使います。

**UIの実装**
- UserControlのイベントハンドラが複雑になる場合は、Behaviorに分けるか、
  `partial`でファイルを分割してください。

XAMLファイル
- インデントは4つのスペースにして下さい。
- コントロールにプロパティを追加する場合、
  最初のプロパティは項目と同じ行に配置し、
  残りのすべてのプロパティは最初のプロパティに合わせて別の行に配置します。
- `Binding`を使う場合、[コンパイル済みのバインディング](https://docs.avaloniaui.net/docs/next/basics/data/data-binding/compiled-bindings)を使用して下さい。
```xaml
<UserControl x:CompileBindings="True"
             x:DataType="viewModel:MyViewModel">
    <TextBox Foreground="White"
             MaxWidth="240"
             Text="{Binding Text.Value}" />
</UserControl>
```
