using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore;

public partial class MainWindow
{
    private bool _keyboardWindowRoutingInstalled;
    private Window? _keyboardPickedWindow;

    private void InstallScopedWindowKeyboardCommands()
    {
        if (_keyboardWindowRoutingInstalled) return;
        _keyboardWindowRoutingInstalled = true;
        EventManager.RegisterClassHandler(typeof(Window), Keyboard.PreviewKeyDownEvent, new KeyEventHandler((sender, args) =>
        {
            if (sender is not Window window || !IsScopedKeyboardWindow(window) || !BelongsToKeyboardOwner(window) || args.Handled) return;
            if (HandleCustomKeyboardShortcut(window, args)) args.Handled = true;
        }));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window && IsScopedKeyboardWindow(window) && BelongsToKeyboardOwner(window))
                DiscoverScopedWindowKeyboardCommands(window);
        }));
    }

    private bool IsScopedKeyboardWindow(Window window) =>
        !ReferenceEquals(window, this) && window is not DetachedSheetWindow && window is not KeyboardShortcutSettingsDialog;

    private bool BelongsToKeyboardOwner(Window window)
    {
        for (Window? owner = window; owner != null; owner = owner.Owner)
            if (ReferenceEquals(owner, this)) return true;
        return false;
    }

    private static string KeyboardWindowPrefix(Window window)
    {
        string type = window.GetType().FullName ?? window.GetType().Name;
        // Several existing dialogs are plain Window instances. Their exact dialog title
        // keeps an OK/Delete action from being replayed in a different kind of dialog.
        if (window.GetType() == typeof(Window)) type += ":" + Uri.EscapeDataString(window.Title);
        return "surface:window:" + type + "|";
    }

    private void DiscoverScopedWindowKeyboardCommands(Window window)
    {
        foreach (FrameworkElement element in WalkKeyboardSurface(window))
            if (IsKeyboardCommandSurface(element)) RegisterKeyboardSurface(element);
    }

    private bool KeyboardCommandBelongsToInputWindow(KeyboardCommandDefinition command, object sender)
    {
        if (sender is Window window && IsScopedKeyboardWindow(window))
            return command.Id.StartsWith(KeyboardWindowPrefix(window), StringComparison.Ordinal);
        return !command.Id.StartsWith("surface:window:", StringComparison.Ordinal);
    }

    private void ExecuteScopedWindowKeyboardCommand(string id, object sender)
    {
        if (sender is not Window window || !IsScopedKeyboardWindow(window) || !BelongsToKeyboardOwner(window) ||
            !id.StartsWith(KeyboardWindowPrefix(window), StringComparison.Ordinal)) return;
        string selector = id[KeyboardWindowPrefix(window).Length..];
        if (selector.StartsWith("menu:", StringComparison.Ordinal))
        {
            ExecuteKeyboardPopupMenu(window, selector["menu:".Length..]);
            return;
        }
        FrameworkElement? control = selector.StartsWith("name:", StringComparison.Ordinal)
            ? window.FindName(selector[5..]) as FrameworkElement : null;
        control ??= WalkKeyboardSurface(window).FirstOrDefault(element => IsKeyboardCommandSurface(element) && KeyboardSurfacePath(element) == selector);
        if (control != null) InvokeKeyboardSurface(control);
    }

    private Window? KeyboardSurfaceOwner(FrameworkElement element)
    {
        Window? window = Window.GetWindow(element);
        if (window != null) return window;
        if (element is MenuItem item)
        {
            ItemsControl? owner = ItemsControl.ItemsControlFromItemContainer(item);
            while (owner is MenuItem parent) owner = ItemsControl.ItemsControlFromItemContainer(parent);
            if (owner is ContextMenu { PlacementTarget: DependencyObject target }) return Window.GetWindow(target);
        }
        return null;
    }
}
