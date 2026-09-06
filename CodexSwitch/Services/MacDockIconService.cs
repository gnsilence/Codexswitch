using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace CodexSwitch.Services;

internal static class MacDockIconService
{
    private static readonly Uri IconUri = new("avares://CodexSwitch/Assets/icons/aioss.png");

    public static void ConfigureForWindowVisibility(bool hasVisibleWindow)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            SetShowsInDock(hasVisibleWindow);
            if (hasVisibleWindow)
            {
                var iconPath = CopyBundledIconToTempFile();
                SetApplicationIcon(iconPath);
                ActivateApplication();
            }
        }
        catch
        {
            // Dock integration is cosmetic; startup must not depend on AppKit interop.
        }
    }

    private static string CopyBundledIconToTempFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CodexSwitch");
        Directory.CreateDirectory(directory);

        var iconPath = Path.Combine(directory, "dock-icon.png");
        using var input = AssetLoader.Open(IconUri);
        using var output = File.Create(iconPath);
        input.CopyTo(output);

        return iconPath;
    }

    private static void SetApplicationIcon(string iconPath)
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        var nsImageClass = objc_getClass("NSImage");

        if (nsApplicationClass == IntPtr.Zero || nsImageClass == IntPtr.Zero)
            return;

        var application = IntPtr_objc_msgSend(nsApplicationClass, sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
            return;

        var image = CreateImage(nsImageClass, iconPath);
        if (image == IntPtr.Zero)
            return;

        try
        {
            Void_objc_msgSend_IntPtr(application, sel_registerName("setApplicationIconImage:"), image);
        }
        finally
        {
            Void_objc_msgSend(image, sel_registerName("release"));
        }
    }

    private static void SetShowsInDock(bool showsInDock)
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        if (nsApplicationClass == IntPtr.Zero)
            return;

        var application = IntPtr_objc_msgSend(nsApplicationClass, sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
            return;

        nint policy = showsInDock ? 0 : 1;
        Bool_objc_msgSend_Nint(application, sel_registerName("setActivationPolicy:"), policy);
    }

    private static void ActivateApplication()
    {
        var nsApplicationClass = objc_getClass("NSApplication");
        if (nsApplicationClass == IntPtr.Zero)
            return;

        var application = IntPtr_objc_msgSend(nsApplicationClass, sel_registerName("sharedApplication"));
        if (application == IntPtr.Zero)
            return;

        Void_objc_msgSend_Bool(application, sel_registerName("activateIgnoringOtherApps:"), true);
    }

    private static IntPtr CreateImage(IntPtr nsImageClass, string iconPath)
    {
        var pathString = CreateNSString(iconPath);
        if (pathString == IntPtr.Zero)
            return IntPtr.Zero;

        var image = IntPtr_objc_msgSend(nsImageClass, sel_registerName("alloc"));
        if (image == IntPtr.Zero)
            return IntPtr.Zero;

        return IntPtr_objc_msgSend_IntPtr(image, sel_registerName("initWithContentsOfFile:"), pathString);
    }

    private static IntPtr CreateNSString(string value)
    {
        var nsStringClass = objc_getClass("NSString");
        if (nsStringClass == IntPtr.Zero)
            return IntPtr.Zero;

        var valuePointer = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return IntPtr_objc_msgSend_IntPtr(
                nsStringClass,
                sel_registerName("stringWithUTF8String:"),
                valuePointer);
        }
        finally
        {
            Marshal.FreeCoTaskMem(valuePointer);
        }
    }

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr objc_getClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Bool_objc_msgSend_Nint(IntPtr receiver, IntPtr selector, nint argument);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void Void_objc_msgSend_Bool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool argument);
}
