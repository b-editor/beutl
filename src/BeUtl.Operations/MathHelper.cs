namespace BeUtl.Operations;

internal static class MathHelper
{
    public static float ToDegrees(float radians)
    {
        const float radToDeg = 180.0f / MathF.PI;
        return radians * radToDeg;
    }

    public static float ToRadians(float degrees)
    {
        const float degToRad = MathF.PI / 180.0f;
        return degrees * degToRad;
    }
}
