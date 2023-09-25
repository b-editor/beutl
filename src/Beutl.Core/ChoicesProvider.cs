namespace Beutl;

/// <summary>
/// プロパティの選択肢を提供します。
/// </summary>
public interface IChoicesProvider
{
    /// <summary>
    /// 選択肢を返します。
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>ObservableCollectionを返すとリストの変更が追跡されます。</item>
    /// <item>要素をToStringすることで取得される文字列がUIに表示されます。</item>
    /// </list>
    /// </remarks>
    /// <returns>選択肢</returns>
    static abstract IReadOnlyList<object> GetChoices();
}

/// <summary>
/// プロパティに設定する値を <see cref="IChoicesProvider"/> によって提供される選択肢から選べるようにします。
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ChoicesProviderAttribute : Attribute
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="providerType"><see cref="IChoicesProvider"/>を継承する型</param>
    public ChoicesProviderAttribute(Type providerType)
    {
        if (!providerType.IsAssignableTo(typeof(IChoicesProvider)))
            throw new ArgumentException("'IChoicesProvider'を継承する必要があります。", nameof(providerType));

        ProviderType = providerType;
    }

    /// <summary>
    /// <see cref="IChoicesProvider"/>を継承する型を取得します。
    /// </summary>
    public Type ProviderType { get; }
}
