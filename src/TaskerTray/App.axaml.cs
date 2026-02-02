using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TaskerTray.Views;

namespace TaskerTray;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private TaskListPopup? _popup;
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    static App()
    {
        // Hide from Dock as early as possible
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            HideFromDock();
        }
    }

    public override void Initialize()
    {
        // Hide again before XAML loads
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            HideFromDock();
        }
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Hide from Dock on macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            HideFromDock();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;

            // No main window - tray only
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Popup is created lazily on first use
            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();

        // Hide from Dock again after everything is initialized
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            HideFromDock();
            System.Threading.Tasks.Task.Delay(500).ContinueWith(_ =>
                Avalonia.Threading.Dispatcher.UIThread.Post(HideFromDock));
        }
    }

    private void SetupTrayIcon()
    {
        // Load tray icon from embedded assets
        using var iconStream = AssetLoader.Open(new Uri("avares://TaskerTray/Assets/tray-icon.png"));
        var bitmap = new Bitmap(iconStream);
        var icon = new WindowIcon(bitmap);

        // Create menu - on macOS we need a menu for the tray icon to be clickable
        var menu = new NativeMenu();

        // Show popup when menu opens
        menu.Opening += (_, _) =>
        {
            // Capture mouse position (near tray icon)
            CaptureMousePosition();
            // Show popup
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowPopup());
        };

        // Empty menu - just used to detect clicks
        // Quit is available via Cmd+Q when popup is focused

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "Tasker",
            IsVisible = true,
            Menu = menu
        };
    }

    private PixelPoint? _lastMousePosition;

    private void CaptureMousePosition()
    {
        // Try to get mouse position using native macOS API
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var mousePos = GetMacOSMousePosition();
                if (mousePos.HasValue)
                {
                    _lastMousePosition = mousePos.Value;
                }
            }
            catch { }
        }
    }

    private void ShowPopup()
    {
        // Create popup lazily on first use
        if (_popup == null)
        {
            _popup = new TaskListPopup();
            _popup.QuitRequested += () => _desktop?.Shutdown();
        }

        // Ensure we stay hidden from Dock
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            HideFromDock();
        }

        if (_popup.IsVisible)
        {
            _popup.Hide();
        }
        else
        {
            var screen = _popup.Screens.Primary;
            if (screen != null)
            {
                var workingArea = screen.WorkingArea;
                var popupWidth = 320;
                var popupHeight = 450;

                int x, y;

                // Use captured mouse position to center popup below tray icon
                if (_lastMousePosition.HasValue)
                {
                    // Center horizontally on mouse X position
                    x = _lastMousePosition.Value.X - (popupWidth / 2);
                    // Position below menu bar
                    y = workingArea.Y + 5;

                    // Keep within screen bounds
                    x = Math.Max(workingArea.X + 10, Math.Min(x, workingArea.X + workingArea.Width - popupWidth - 10));
                }
                else
                {
                    // Fallback: top-right corner
                    x = workingArea.X + workingArea.Width - popupWidth - 10;
                    y = workingArea.Y + 5;
                }

                _popup.ShowAtPosition(new PixelPoint(x, y));
            }
            else
            {
                _popup.ShowAtPosition(new PixelPoint(100, 100));
            }
        }
    }

    // macOS: Get current mouse position
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern CGPoint CGEventGetLocation(IntPtr eventRef);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CFRelease(IntPtr cf);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    private PixelPoint? GetMacOSMousePosition()
    {
        try
        {
            var eventRef = CGEventCreate(IntPtr.Zero);
            if (eventRef != IntPtr.Zero)
            {
                var point = CGEventGetLocation(eventRef);
                CFRelease(eventRef);
                return new PixelPoint((int)point.X, (int)point.Y);
            }
        }
        catch { }
        return null;
    }

    // macOS: Hide app from Dock (set as accessory app)
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_ReturnIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend_SetLong(IntPtr receiver, IntPtr selector, long arg);

    private static void HideFromDock()
    {
        try
        {
            var nsAppClass = objc_getClass("NSApplication");
            var sharedAppSel = sel_registerName("sharedApplication");
            var setPolicySel = sel_registerName("setActivationPolicy:");

            var nsApp = objc_msgSend_ReturnIntPtr(nsAppClass, sharedAppSel);
            // 1 = NSApplicationActivationPolicyAccessory (no Dock icon)
            objc_msgSend_SetLong(nsApp, setPolicySel, 1);
        }
        catch
        {
            // Silently fail on non-macOS or if API changes
        }
    }
}
