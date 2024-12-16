using System.Numerics;
using Beutl.Graphics;

namespace Beutl.Graphics3D;

public class GraphicsContext3D : IDisposable, IPopable
{
    private readonly Stack<Action> _popActions = new();

    public Matrix4x4 Transform { get; private set; } = Matrix4x4.Identity;

    public void Dispose()
    {
        _popActions.Clear();
        Transform = Matrix4x4.Identity;
    }

    public PushedState PushTransform(Matrix4x4 mat)
    {
        Matrix4x4 old = Transform;
        Transform = mat * Transform;
        _popActions.Push(() => Transform = old);
        return new(this, _popActions.Count);
    }

    public void Pop(int count)
    {
        if (count < 0)
        {
            while (count < 0
                   && _popActions.TryPop(out Action? restorer))
            {
                restorer();
                count++;
            }
        }
        else
        {
            while (_popActions.Count >= count
                   && _popActions.TryPop(out Action? restorer))
            {
                restorer();
            }
        }
    }
}
