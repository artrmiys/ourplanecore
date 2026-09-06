using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OurPlanCore;

public partial class MainWindow
{
    private readonly Dictionary<string, WeakReference<FrameworkElement>> _keyboardSurfaceTargets = new(StringComparer.Ordinal);
    private Action<KeyboardCommandDefinition>? _keyboardCommandPicker;
    private bool _keyboardMenuTrackingInstalled;
    private WeakReference<ContextMenu>? _keyboardLastOpenedMenu;

    private void DiscoverKeyboardSurfaceCommands()
    {
        if (!_keyboardMenuTrackingInstalled)
        {
            _keyboardMenuTrackingInstalled = true;
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is not ContextMenu menu) return;
                    if (menu.PlacementTarget is not DependencyObject placement || Window.GetWindow(placement) is not Window window || !BelongsToKeyboardOwner(window)) return;
                    _keyboardLastOpenedMenu = new(menu);
                    if (menu.PlacementTarget is FrameworkElement { Name.Length: > 0 } anchor)
                        RegisterKeyboardPopupMenu(anchor.Name, menu);
                }));
        }
        foreach (FrameworkElement element in WalkKeyboardSurface(this))
        {
            if (!IsKeyboardCommandSurface(element)) continue;
            RegisterKeyboardSurface(element);
        }
        if (PagesTree.SelectedItem is TreeViewItem page)
            RegisterKeyboardContextMenu("PagesTree", BuildPagesContextMenu(page));
        if (TakeoffsTree.SelectedItem is TreeViewItem takeoff)
            RegisterKeyboardContextMenu("TakeoffsTree", FreshTakeoffsKeyboardMenu(takeoff));
    }

    private static bool IsKeyboardCommandSurface(FrameworkElement element) =>
        element is ButtonBase or MenuItem || element is ComboBox { Name.Length: > 0 } ||
        element is Slider { Name.Length: > 0 } || element is TextBox { Name.Length: > 0 };

    private static bool IsDataRowKeyboardSurface(FrameworkElement element)
    {
        for (DependencyObject? current = element; current != null; current = GetShortcutParent(current))
            if (current is TreeViewItem or DataGridRow or ListBoxItem) return true;
        return false;
    }

    private static IEnumerable<FrameworkElement> WalkKeyboardSurface(DependencyObject root)
    {
        var seen = new HashSet<DependencyObject>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            DependencyObject current = queue.Dequeue();
            if (!seen.Add(current)) continue;
            if (current is FrameworkElement element) yield return element;
            // Data rows have per-object commands: use the current selection's fresh context menu.
            if (current is TreeViewItem or DataGrid or ListBox) continue;
            foreach (object child in LogicalTreeHelper.GetChildren(current))
                if (child is DependencyObject dependency) queue.Enqueue(dependency);
        }
    }

    private KeyboardCommandDefinition RegisterKeyboardSurface(FrameworkElement element)
    {
        Window? window = KeyboardSurfaceOwner(element);
        bool scopedWindow = window != null && IsScopedKeyboardWindow(window);
        if (!scopedWindow && KeyboardControlCommandAliases.TryGetValue(element.Name, out string? alias) && _keyboardCommands.TryGetValue(alias, out KeyboardCommandDefinition? existing))
            return existing;
        string path = KeyboardSurfacePath(element);
        string id = scopedWindow ? KeyboardWindowPrefix(window!) + path : "surface:" + path;
        string title = KeyboardSurfaceTitle(element);
        string category = scopedWindow ? "Dialog / " + window!.Title : KeyboardSurfaceCategory(element);
        if (element is TextBox or Slider) title = "Edit " + title;
        if (element is ComboBox) title = "Choose " + title;
        var command = new KeyboardCommandDefinition(id, title, category, KeyboardCommandContext.Surface, [],
            "Uses the same control and availability rules as the mouse action.");
        _keyboardCommands[id] = command;
        _keyboardSurfaceTargets[id] = new(element);
        return command;
    }

    private static string KeyboardSurfacePath(FrameworkElement element)
    {
        if (!string.IsNullOrWhiteSpace(element.Name)) return "name:" + element.Name;
        var segments = new List<string> { element.GetType().Name + ":" + KeyboardSurfaceTitle(element) };
        DependencyObject child = element;
        for (DependencyObject? parent = GetShortcutParent(element); parent != null; parent = GetShortcutParent(parent))
        {
            if (parent is FrameworkElement { Name.Length: > 0 } named)
            { segments.Insert(0, "name:" + named.Name); break; }
            if (parent is TabItem tab) segments.Insert(0, "tab:" + (tab.Tag ?? tab.Header));
            if (parent is GroupBox group) segments.Insert(0, "group:" + group.Header);
            // Anonymous manager buttons may share captions. Keep their structural owner path,
            // rather than binding an arbitrary last-seen button with the same label.
            if (parent is Panel)
            {
                var siblings = LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ToList();
                segments.Insert(0, parent.GetType().Name + "[" + siblings.IndexOf(child) + "]");
            }
            child = parent;
        }
        return string.Join("/", segments);
    }

    private static string KeyboardSurfaceTitle(FrameworkElement element)
    {
        string text = AutomationProperties.GetName(element);
        if (string.IsNullOrWhiteSpace(text)) text = element.ToolTip as string ?? "";
        if (string.IsNullOrWhiteSpace(text)) text = element switch
        {
            MenuItem menu => menu.Header as string ?? "",
            ContentControl control when control.Content is string value => value,
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(text) && element is ContentControl { Content: DependencyObject content })
            text = string.Join(" ", WalkKeyboardSurface(content).OfType<TextBlock>().Select(block => block.Text));
        if (string.IsNullOrWhiteSpace(text)) text = element.Name;
        return string.IsNullOrWhiteSpace(text) ? element.GetType().Name : text.Replace("_", "", StringComparison.Ordinal).Trim();
    }

    private static string KeyboardSurfaceCategory(FrameworkElement element)
    {
        for (DependencyObject? parent = element; parent != null; parent = GetShortcutParent(parent))
            if (parent is TabItem tab) return "Controls / " + (tab.Header is string title ? title : tab.Tag?.ToString() ?? tab.Name);
        return "Controls / Workspace";
    }

    private ContextMenu FreshTakeoffsKeyboardMenu(TreeViewItem item)
    {
        if (item.Tag is TakeoffItem takeoff) AttachContextMenu(item, takeoff);
        else if (item.Tag is TakeoffFolderNode folder) AttachFolderContextMenu(item, folder);
        else if (item.Tag is TakeoffMeasurementNode section) return BuildTakeoffSectionContextMenu(section);
        return item.ContextMenu ?? new ContextMenu();
    }

    private void RegisterKeyboardContextMenu(string owner, ContextMenu menu)
    {
        foreach (var (path, item) in WalkKeyboardMenu(menu.Items))
        {
            if (item.HasItems) continue;
            string id = "surface:context:" + owner + "/" + path;
            _keyboardCommands[id] = new(id, path.Replace("/", " / ", StringComparison.Ordinal),
                "Context menu / " + owner, owner == "PagesTree" ? KeyboardCommandContext.Pages : KeyboardCommandContext.Takeoffs,
                [], "Applies to the current tree selection; the menu is rebuilt before execution.");
        }
    }

    private void RegisterKeyboardPopupMenu(string owner, ContextMenu menu)
    {
        Window? window = menu.PlacementTarget == null ? null : Window.GetWindow(menu.PlacementTarget);
        foreach (var (path, item) in WalkKeyboardMenu(menu.Items))
        {
            if (item.HasItems) continue;
            string id = (window != null && IsScopedKeyboardWindow(window) ? KeyboardWindowPrefix(window) : "surface:") + "menu:" + owner + "/" + path;
            _keyboardCommands[id] = new(id, path.Replace("/", " / ", StringComparison.Ordinal),
                "Menu / " + KeyboardSurfaceTitle(menu.PlacementTarget as FrameworkElement ?? menu),
                KeyboardCommandContext.Surface, [], "Uses the current menu state.");
        }
    }

    private static IEnumerable<(string Path, MenuItem Item)> WalkKeyboardMenu(IEnumerable items, string prefix = "")
    {
        foreach (MenuItem item in items.OfType<MenuItem>())
        {
            string path = prefix + KeyboardSurfaceTitle(item);
            yield return (path, item);
            foreach (var child in WalkKeyboardMenu(item.Items, path + "/")) yield return child;
        }
    }

    private void ExecuteKeyboardSurfaceCommand(string id, object sender)
    {
        if (id.StartsWith("surface:window:", StringComparison.Ordinal))
        { ExecuteScopedWindowKeyboardCommand(id, sender); return; }
        if (id.StartsWith("surface:menu:", StringComparison.Ordinal))
        {
            ExecuteKeyboardPopupMenu(this, id["surface:menu:".Length..]);
            return;
        }
        if (id.StartsWith("surface:context:", StringComparison.Ordinal))
        {
            string path = id["surface:context:".Length..];
            int slash = path.IndexOf('/');
            string owner = path[..slash];
            TreeView tree = owner == "PagesTree" ? PagesTree : TakeoffsTree;
            if (tree.SelectedItem is not TreeViewItem selected) return;
            ContextMenu menu = owner == "PagesTree" ? BuildPagesContextMenu(selected) : FreshTakeoffsKeyboardMenu(selected);
            MenuItem? target = WalkKeyboardMenu(menu.Items).FirstOrDefault(pair => pair.Path == path[(slash + 1)..]).Item;
            if (target != null) InvokeKeyboardSurface(target);
            else TxtStatus.Text = "This context command is unavailable for the current selection.";
            return;
        }
        FrameworkElement? control = null;
        if (id.StartsWith("surface:name:", StringComparison.Ordinal)) control = FindName(id["surface:name:".Length..]) as FrameworkElement;
        if (control == null && _keyboardSurfaceTargets.TryGetValue(id, out WeakReference<FrameworkElement>? reference)) reference.TryGetTarget(out control);
        if (control == null)
        {
            DiscoverKeyboardSurfaceCommands();
            if (_keyboardSurfaceTargets.TryGetValue(id, out reference)) reference.TryGetTarget(out control);
        }
        if (control == null) { TxtStatus.Text = "Open the command's panel before using this shortcut."; return; }
        if (sender is DetachedSheetWindow detached && control.Name is "BtnMirrorHorizontal" or "BtnMirrorVertical")
        {
            detached.Viewport.ExecuteCustomKeyboardCommand(control.Name == "BtnMirrorHorizontal" ? "edit.mirrorHorizontal" : "edit.mirrorVertical");
            return;
        }
        InvokeKeyboardSurface(control);
    }

    private void ExecuteKeyboardPopupMenu(Window window, string path)
    {
        int slash = path.IndexOf('/');
        if (slash <= 0 || window.FindName(path[..slash]) is not FrameworkElement anchor) return;
        InvokeKeyboardSurface(anchor);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (_keyboardLastOpenedMenu?.TryGetTarget(out ContextMenu? menu) != true || menu == null || !menu.IsOpen || !ReferenceEquals(menu.PlacementTarget, anchor))
            { TxtStatus.Text = "The command's menu is unavailable."; return; }
            MenuItem? target = WalkKeyboardMenu(menu.Items).FirstOrDefault(pair => pair.Path == path[(slash + 1)..]).Item;
            if (target == null) TxtStatus.Text = "This menu command is unavailable in the current context.";
            else InvokeKeyboardSurface(target);
            menu.IsOpen = false;
        }));
    }

    private void InvokeKeyboardSurface(FrameworkElement target)
    {
        if (IsDataRowKeyboardSurface(target))
        { TxtStatus.Text = "Use the current selection's context-menu command for row actions."; return; }
        if (!target.IsEnabled) { TxtStatus.Text = "This command is disabled for the current selection."; return; }
        if (target is MenuItem menu)
        {
            if (menu.IsCheckable) menu.IsChecked = !menu.IsChecked;
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, menu));
        }
        else if (target is ToggleButton toggle)
        {
            toggle.IsChecked = toggle is RadioButton ? true : toggle.IsChecked == true
                ? toggle.IsThreeState ? null : false : toggle.IsChecked.HasValue;
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, toggle));
        }
        else if (target is Button button)
        {
            if (UIElementAutomationPeer.CreatePeerForElement(button)?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
            else button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        }
        else
        {
            for (DependencyObject? parent = GetShortcutParent(target); parent != null; parent = GetShortcutParent(parent))
                if (parent is TabItem tab) tab.IsSelected = true;
            target.Focus();
            if (target is ComboBox combo) combo.IsDropDownOpen = true;
            if (target is TextBox box) box.SelectAll();
        }
    }

    private void BeginKeyboardCommandPick(Action<KeyboardCommandDefinition> picked)
    {
        _keyboardCommandPicker = picked;
        InputManager.Current.PreProcessInput -= KeyboardCommandPickInput;
        InputManager.Current.PreProcessInput += KeyboardCommandPickInput;
        TxtStatus.Text = "Choose a button or menu command. Hold Ctrl to open menus or switch panels while choosing. Esc cancels.";
    }

    private void KeyboardCommandPickInput(object sender, PreProcessInputEventArgs args)
    {
        if (_keyboardCommandPicker == null) return;
        if (args.StagingItem.Input is KeyEventArgs { RoutedEvent: var keyEvent } key && keyEvent == Keyboard.PreviewKeyDownEvent && key.Key == Key.Escape)
        {
            InputManager.Current.PreProcessInput -= KeyboardCommandPickInput;
            _keyboardCommandPicker = null;
            key.Handled = true;
            TxtStatus.Text = "Shortcut command selection cancelled.";
            _keyboardShortcutDialog?.Show();
            return;
        }
        if (args.StagingItem.Input is not MouseButtonEventArgs { ChangedButton: MouseButton.Left, ButtonState: MouseButtonState.Pressed } mouse ||
            mouse.RoutedEvent != Mouse.PreviewMouseDownEvent) return;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
        FrameworkElement? element = Mouse.DirectlyOver as FrameworkElement;
        while (element != null && !IsKeyboardCommandSurface(element)) element = GetShortcutParent(element) as FrameworkElement;
        if (element == null) return;
        if (IsDataRowKeyboardSurface(element))
        {
            mouse.Handled = true; args.Cancel();
            TxtStatus.Text = "Choose this action from the row's context menu to bind it to the current selection.";
            return;
        }
        Window? pickedWindow = KeyboardSurfaceOwner(element);
        if (pickedWindow == null || !BelongsToKeyboardOwner(pickedWindow) || pickedWindow is KeyboardShortcutSettingsDialog) return;
        if (element is MenuItem { HasItems: true }) return;
        KeyboardCommandDefinition command = RegisterKeyboardSurface(element);
        if (element is MenuItem menuItem)
        {
            var parts = new List<string> { KeyboardSurfaceTitle(menuItem) };
            ItemsControl? owner = ItemsControl.ItemsControlFromItemContainer(menuItem);
            while (owner is MenuItem parent) { parts.Insert(0, KeyboardSurfaceTitle(parent)); owner = ItemsControl.ItemsControlFromItemContainer(parent); }
            if (owner is ContextMenu { PlacementTarget: TreeViewItem node })
            {
                string treeName = node.Tag is PageInfo or PageFolderNode or PageTakeoffNode ? "PagesTree" : "TakeoffsTree";
                string id = "surface:context:" + treeName + "/" + string.Join("/", parts);
                command = command with { Id = id, Category = "Context menu / " + treeName,
                    Context = treeName == "PagesTree" ? KeyboardCommandContext.Pages : KeyboardCommandContext.Takeoffs };
                _keyboardCommands[id] = command;
            }
            else if (owner is ContextMenu { PlacementTarget: FrameworkElement { Name.Length: > 0 } anchor })
            {
                command = command with { Id = (IsScopedKeyboardWindow(pickedWindow) ? KeyboardWindowPrefix(pickedWindow) : "surface:") + "menu:" + anchor.Name + "/" + string.Join("/", parts),
                    Category = "Menu / " + KeyboardSurfaceTitle(anchor) };
                _keyboardCommands[command.Id] = command;
            }
            else
            {
                TxtStatus.Text = "Choose the menu's named toolbar command or a tree context command.";
                return;
            }
            if (owner is ContextMenu pickedMenu) pickedMenu.IsOpen = false;
        }
        mouse.Handled = true;
        args.Cancel();
        InputManager.Current.PreProcessInput -= KeyboardCommandPickInput;
        Action<KeyboardCommandDefinition> callback = _keyboardCommandPicker;
        _keyboardCommandPicker = null;
        _keyboardPickedWindow = IsScopedKeyboardWindow(pickedWindow) ? pickedWindow : null;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => callback(command)));
    }
}
