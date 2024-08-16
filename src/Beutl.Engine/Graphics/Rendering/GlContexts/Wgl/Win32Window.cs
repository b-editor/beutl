namespace Beutl.Graphics.Rendering.GlContexts;

internal class Win32Window : IDisposable
{
    private ushort classRegistration;

    public string WindowClassName { get; }

    public nint WindowHandle { get; private set; }

    public nint DeviceContextHandle { get; private set; }

    public Win32Window(string className)
    {
        WindowClassName = className;

        var wc = new WNDCLASS
        {
            cbClsExtra = 0,
            cbWndExtra = 0,
            hbrBackground = nint.Zero,
            hCursor = User32.LoadCursor(nint.Zero, (int)User32.IDC_ARROW),
            hIcon = User32.LoadIcon(nint.Zero, (nint)User32.IDI_APPLICATION),
            hInstance = Kernel32.CurrentModuleHandle,
            lpfnWndProc = User32.DefWindowProc,
            lpszClassName = WindowClassName,
            lpszMenuName = null,
            style = User32.CS_HREDRAW | User32.CS_VREDRAW | User32.CS_OWNDC
        };

        classRegistration = User32.RegisterClass(ref wc);
        if (classRegistration == 0)
            throw new Exception($"Could not register window class: {className}");

        WindowHandle = User32.CreateWindow(
            WindowClassName,
            $"The Invisible Man ({className})",
            WindowStyles.WS_OVERLAPPEDWINDOW,
            0, 0,
            1, 1,
            nint.Zero, nint.Zero, Kernel32.CurrentModuleHandle, nint.Zero);
        if (WindowHandle == nint.Zero)
            throw new Exception($"Could not create window: {className}");

        DeviceContextHandle = User32.GetDC(WindowHandle);
        if (DeviceContextHandle == nint.Zero)
        {
            Dispose();
            throw new Exception($"Could not get device context: {className}");
        }
    }

    public void Dispose()
    {
        if (WindowHandle != nint.Zero)
        {
            if (DeviceContextHandle != nint.Zero)
            {
                User32.ReleaseDC(WindowHandle, DeviceContextHandle);
                DeviceContextHandle = nint.Zero;
            }

            User32.DestroyWindow(WindowHandle);
            WindowHandle = nint.Zero;
        }

        if (classRegistration != 0)
        {
            User32.UnregisterClass(WindowClassName, Kernel32.CurrentModuleHandle);
            classRegistration = 0;
        }
    }
}
