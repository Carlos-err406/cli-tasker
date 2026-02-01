using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TaskerTray.Views;

public partial class QuickAddWindow : Window
{
    public string? TaskDescription { get; private set; }

    public QuickAddWindow()
    {
        InitializeComponent();

        // Focus the input when window opens
        Opened += (_, _) => TaskInput.Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        Submit();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Cancel();
    }

    private void Submit()
    {
        var text = TaskInput.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            TaskDescription = text;
            Close(true);
        }
    }

    private void Cancel()
    {
        TaskDescription = null;
        Close(false);
    }
}
