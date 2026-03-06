using Beutl.Media;

namespace Beutl.Graphics.Particles;

internal struct Particle
{
    public float BirthTime;
    public float Lifetime;
    public float X, Y;
    public float VelocityX, VelocityY;
    public float Rotation;
    public float AngularVelocity;
    public float BaseSize;
    public float BaseOpacity;
    public Color BaseColor;
    public float CurrentSize;
    public float CurrentOpacity;
    public Color CurrentColor;
    public bool IsAlive;
}
