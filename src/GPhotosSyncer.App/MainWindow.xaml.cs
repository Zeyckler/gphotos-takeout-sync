using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace GPhotosSyncer.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "GPhotos Takeout Sync";

        // The folder picker lives here because unpackaged pickers need the window HWND.
        View.Vm.PickFolder = PickFolderAsync;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(960, 840));
        TrySetWindowIcon(appWindow);
    }

    // Kept alive for the process lifetime: AppWindow.SetIcon does not duplicate the HICON,
    // so destroying it after assignment would blank the icon we just set.
    private static IntPtr _windowIconHandle;

    /// <summary>Sets the title-bar/taskbar icon from the .exe's own embedded icon (ApplicationIcon),
    /// so it works even as a single-file build with no loose .ico on disk.</summary>
    private static void TrySetWindowIcon(AppWindow appWindow)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            if (ExtractIconEx(exe, 0, out var large, out var small, 1) == 0) return;
            var hIcon = large != IntPtr.Zero ? large : small;
            if (hIcon == IntPtr.Zero) return;
            appWindow.SetIcon(Win32Interop.GetIconIdFromIcon(hIcon));
            _windowIconHandle = hIcon;
            var unused = hIcon == large ? small : large;
            if (unused != IntPtr.Zero) DestroyIcon(unused);
        }
        catch { /* the icon is cosmetic — never fail startup over it */ }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
