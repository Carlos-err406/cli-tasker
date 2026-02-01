using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TaskerTray.ViewModels;

namespace TaskerTray;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private AppViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // No main window - tray only
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _viewModel = new AppViewModel();
            _viewModel.QuitRequested += () => desktop.Shutdown();

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "Tasker",
            IsVisible = true,
            Menu = CreateTrayMenu()
        };

        // Use a simple text-based icon for now (will add proper icon later)
        // On macOS, the tray icon will show as a template image
    }

    private NativeMenu CreateTrayMenu()
    {
        var menu = new NativeMenu();

        // Add task item
        var addItem = new NativeMenuItem("Add Task...");
        addItem.Click += (_, _) => _viewModel?.AddTaskCommand.Execute(null);
        menu.Items.Add(addItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Refresh item
        var refreshItem = new NativeMenuItem("Refresh");
        refreshItem.Click += (_, _) => _viewModel?.RefreshCommand.Execute(null);
        menu.Items.Add(refreshItem);

        menu.Items.Add(new NativeMenuItemSeparator());

        // Quit item
        var quitItem = new NativeMenuItem("Quit Tasker");
        quitItem.Click += (_, _) => _viewModel?.QuitCommand.Execute(null);
        menu.Items.Add(quitItem);

        return menu;
    }
}
