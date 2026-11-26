using Avalonia;
using Avalonia.Controls;
using GameDock.Presentation.Services.Toasts;

namespace GameDock.Presentation.Views.Toasts;

/// <summary>
/// Overlay that shows the toasts of <see cref="IToastService"/>.
/// Every window that can be in front hosts one; a host hides itself while one of its window's
/// owned dialogs (e.g. Settings) is open, so toasts appear only once, in the frontmost window.
/// </summary>
public partial class ToastHost : UserControl
{
    private Window? _window;

    public ToastHost()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
            return;

        // Created by XAML, so constructor injection is not available here.
        DataContext = App.Services.GetRequiredService<IToastService>();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is null)
            return;

        _window.Activated += OnWindowActivationChanged;
        _window.Deactivated += OnWindowActivationChanged;
        UpdateVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.Activated -= OnWindowActivationChanged;
            _window.Deactivated -= OnWindowActivationChanged;
            _window = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowActivationChanged(object? sender, EventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (_window is null)
            return;

        // An open owned dialog hosts the toasts itself (it is in front of this window).
        var hasOpenDialog = false;
        if (!_window.IsActive)
        {
            foreach (var owned in _window.OwnedWindows)
            {
                if (owned.IsVisible)
                {
                    hasOpenDialog = true;
                    break;
                }
            }
        }

        IsVisible = !hasOpenDialog;
    }
}
