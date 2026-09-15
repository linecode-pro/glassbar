using System;
using System.Runtime.InteropServices;

namespace TranslucentTabBar.NativeMethods;

public enum TaskbarBrush
{
    Acrylic = 0,
    SolidColor = 1,
}

[ComImport]
[Guid("5bcf9150-c28a-4ef2-913c-4c3ea2f5ead0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITaskbarAppearanceService
{
    [PreserveSig]
    int SetTaskbarAppearance(nint taskbar, TaskbarBrush brush, uint color);

    [PreserveSig]
    int SetTaskbarBlur(nint taskbar, uint color, float blurAmount);

    [PreserveSig]
    int ReturnTaskbarToDefaultAppearance(nint taskbar);

    [PreserveSig]
    int SetTaskbarBorderVisibility(nint taskbar, bool visible);

    [PreserveSig]
    int RestoreAllTaskbarsToDefault();

    [PreserveSig]
    int RestoreAllTaskbarsToDefaultWhenProcessDies(uint pid);

    [PreserveSig]
    int KillExplorerWhenPackageUninstalls([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int InjectExplorerTAPDelegate(nint window, in Guid riid, out nint ppv);

internal static partial class Kernel32
{
    [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint LoadLibrary(string lpLibFileName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
    public static extern nint GetProcAddress(nint hModule, string procName);
}
