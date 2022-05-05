# ProjectSystem - API改修

※ProjectをWorkspaceとして使う

## ProjectAPIとSceneAPIを疎結合する

サンプルAPI
``` csharp
/*
FrameRate, SampleRateフィールドを付けると疎結合の意味がないので、
VariablesでFrameRate, SampleRateを設定する
*/
public interface IWorkspace : ITopLevel
{
    ICoreList<IWorkspaceItem> Items { get; }

    IDictionary<string, string> Variables { get; }

    Version AppVersion { get; }

    Version MinAppVersion { get; }
}

public interface IWorkspaceItem : IStorable, IElement
{
}

```
