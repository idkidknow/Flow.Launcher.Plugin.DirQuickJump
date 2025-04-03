using Windows.Win32;
using Windows.Win32.Foundation;

namespace Flow.Launcher.Plugin.DirQuickJump;

internal static class Utils
{
    public static unsafe string? GetClassName(HWND handle)
    {
        fixed (char* buf = new char[256])
        {
            return PInvoke.GetClassName(handle, buf, 256) switch
            {
                0 => null,
                _ => new string(buf),
            };
        }
    }

    public static unsafe nint SetWindowText(HWND handle, string text)
    {
        fixed (char* textPtr = text + '\0')
        {
            return PInvoke.SendMessage(handle, PInvoke.WM_SETTEXT, 0, (nint)textPtr).Value;
        }
    }
}