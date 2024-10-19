using Avalonia.Controls;

namespace Beutl.ViewModels;

public partial class EditViewModel : IContextCommandHandler
{
    // IContextCommandHandler を実装しない場合、以下のように ContextCommandAttribute を使用してコマンドを定義します。
    // [ContextCommand]
    // public void PlayPause(KeyEventArgs e)
    // {
    //     if (e.Source is TextBox)
    //         return;
    //     Player.PlayPause.Execute();
    //     e.Handled = true;
    // }
    //
    // [ContextCommand]
    // public void Next()
    // {
    //     Player.Next.Execute();
    // }
    //
    // [ContextCommand]
    // public void Previous()
    // {
    //     Player.Previous.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekStart()
    // {
    //     Player.Start.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekEnd()
    // {
    //     Player.End.Execute();
    // }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        switch (execution.CommandName)
        {
            case "PlayPause" when execution.KeyEventArgs?.Source is not TextBox:
                Player.PlayPause.Execute();
                break;
            case "Next":
                Player.Next.Execute();
                break;
            case "Previous":
                Player.Previous.Execute();
                break;
            case "SeekStart":
                Player.Start.Execute();
                break;
            case "SeekEnd":
                Player.End.Execute();
                break;
            default:
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }
}
