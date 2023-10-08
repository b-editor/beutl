# Beutl.Controls

## `./PropertyEditors` `./Styling/PropertyEditors`

**イベント**
- ValueChanged: 値の変更が確定された。  
                値が変更された状態で、フォーカスが外れたときに発生します。
- ValueChanging: 値が変更されたときに発生します。

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
<!--
### 特別
-->
