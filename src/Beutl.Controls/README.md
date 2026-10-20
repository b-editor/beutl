# Beutl.Controls

## `./PropertyEditors` `./Styling/PropertyEditors`

**イベント**
- ValueChanged: 編集中に値が変わるたびに発生します。
- ValueConfirmed: 値が確定したとき（フォーカスが外れたときなど）に発生します。

**プロパティ**
- Header: プロパティの名前を表示。
- IsReadOnly: プロパティが読み取り専用かどうか。
- MenuContent: プロパティのメニュー

**命名規則**
型名の後に`Editor`を付ける
```
{TypeName}Editor
```

- 型がインターフェイスの場合、プレフィックス: `I`を削除したものを型名とする。
