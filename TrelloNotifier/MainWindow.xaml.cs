using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace TrelloNotifier;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "Trello Notifier";

        IntPtr windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(App.GetAssetPath("trello-notifier.ico"));
        DisplayArea displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        int width = Math.Min(1440, displayArea.WorkArea.Width);
        int height = Math.Min(900, displayArea.WorkArea.Height);
        appWindow.Resize(new SizeInt32(width, height));
        int x = displayArea.WorkArea.X + (displayArea.WorkArea.Width - width) / 2;
        int y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - height) / 2;
        appWindow.Move(new PointInt32(x, y));
    }
}
