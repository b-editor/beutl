using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 入力状態の情報（実際の実装では外部システムから提供）
/// </summary>
public class InputState
{
    public Vector2 MousePosition { get; set; }
    public Vector2 MouseDelta { get; set; }
    public float ScrollDelta { get; set; }
    
    public bool IsMouseButtonDown(MouseButton button) => false; // 実装必要
    public bool IsKeyDown(Key key) => false; // 実装必要
}
