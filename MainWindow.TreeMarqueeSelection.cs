using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private TreeMarqueeSelectionState? _pagesTreeMarqueeSelection;
    private TreeMarqueeSelectionState? _takeoffsTreeMarqueeSelection;

    private bool TryBeginPagesTreeMarqueeSelection(MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ToggleButton>(source) != null ||
            FindAncestor<ScrollBar>(source) != null ||
            IsPageMeasurementVisibilityToggleSource(source) ||
            IsPageOverlayVisibilityToggleSource(source))
        {
            return false;
        }

        bool forceFromRow = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        if (!forceFromRow && FindPagesTreeItemFromSource(source) != null)
            return false;

        _pagesTreeMarqueeSelection = BeginTreeMarqueeSelection(
            PagesTree,
            e,
            _pagesMultiSelection);
        ResetPagesDragState();
        e.Handled = true;
        return true;
    }

    private bool TryBeginTakeoffsTreeMarqueeSelection(MouseButtonEventArgs e)
    {
        DependencyObject? source = e.OriginalSource as DependencyObject;
        if (FindAncestor<ToggleButton>(source) != null ||
            FindAncestor<ScrollBar>(source) != null)
            return false;

        bool forceFromRow = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
        if (!forceFromRow && FindAncestor<TreeViewItem>(source) != null)
            return false;

        _takeoffsTreeMarqueeSelection = BeginTreeMarqueeSelection(
            TakeoffsTree,
            e,
            _takeoffsMultiSelection);
        CancelPendingTakeoffSelectionSync();
        ResetTakeoffsDragState();
        e.Handled = true;
        return true;
    }

    private static TreeMarqueeSelectionState BeginTreeMarqueeSelection(
        TreeView tree,
        MouseButtonEventArgs e,
        IEnumerable<string> baseSelection)
    {
        Point start = e.GetPosition(tree);
        tree.Focus();
        tree.CaptureMouse();
        return new TreeMarqueeSelectionState(
            start,
            additive: (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control,
            baseSelection);
    }

    private void PagesTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CompletePagesTreeMarqueeSelection(cancel: false))
            e.Handled = true;
    }

    private void TakeoffsTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (CompleteTakeoffsTreeMarqueeSelection(cancel: false))
            e.Handled = true;
    }

    private void PagesTree_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_pagesTreeMarqueeSelection != null)
            CancelPagesTreeMarqueeSelection();
    }

    private void TakeoffsTree_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_takeoffsTreeMarqueeSelection != null)
            CancelTakeoffsTreeMarqueeSelection();
    }

    private bool UpdatePagesTreeMarqueeSelection(MouseEventArgs e)
    {
        if (_pagesTreeMarqueeSelection == null)
            return false;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CompletePagesTreeMarqueeSelection(cancel: false);
            return true;
        }

        if (!UpdateTreeMarqueeSelection(PagesTree, _pagesTreeMarqueeSelection, e.GetPosition(PagesTree)))
            return true;

        ApplyPagesTreeMarqueeSelection(_pagesTreeMarqueeSelection);
        return true;
    }

    private bool UpdateTakeoffsTreeMarqueeSelection(MouseEventArgs e)
    {
        if (_takeoffsTreeMarqueeSelection == null)
            return false;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CompleteTakeoffsTreeMarqueeSelection(cancel: false);
            return true;
        }

        if (!UpdateTreeMarqueeSelection(TakeoffsTree, _takeoffsTreeMarqueeSelection, e.GetPosition(TakeoffsTree)))
            return true;

        ApplyTakeoffsTreeMarqueeSelection(_takeoffsTreeMarqueeSelection);
        return true;
    }

    private bool CompletePagesTreeMarqueeSelection(bool cancel)
    {
        TreeMarqueeSelectionState? state = _pagesTreeMarqueeSelection;
        if (state == null)
            return false;

        _pagesTreeMarqueeSelection = null;
        try
        {
            if (state.Active && !cancel)
            {
                SelectPagesTreeMarqueeAnchorSilently();
                TxtStatus.Text = _pagesMultiSelection.Count == 1
                    ? "Selected 1 page tree item."
                    : $"Selected {_pagesMultiSelection.Count} page tree items.";
            }
        }
        finally
        {
            EndTreeMarqueeSelection(PagesTree, state);
        }

        return true;
    }

    private bool CompleteTakeoffsTreeMarqueeSelection(bool cancel)
    {
        TreeMarqueeSelectionState? state = _takeoffsTreeMarqueeSelection;
        if (state == null)
            return false;

        _takeoffsTreeMarqueeSelection = null;
        try
        {
            if (state.Active && !cancel)
            {
                SelectTakeoffsTreeMarqueeAnchorSilently();
                TxtStatus.Text = _takeoffsMultiSelection.Count == 1
                    ? "Selected 1 takeoff tree item."
                    : $"Selected {_takeoffsMultiSelection.Count} takeoff tree items.";
            }
        }
        finally
        {
            EndTreeMarqueeSelection(TakeoffsTree, state);
        }

        return true;
    }

    private void CancelPagesTreeMarqueeSelection() =>
        CompletePagesTreeMarqueeSelection(cancel: true);

    private void CancelTakeoffsTreeMarqueeSelection() =>
        CompleteTakeoffsTreeMarqueeSelection(cancel: true);

    private static bool UpdateTreeMarqueeSelection(
        TreeView tree,
        TreeMarqueeSelectionState state,
        Point current)
    {
        state.Current = current;
        if (!state.Active)
        {
            if (!MovementExceedsDragDistance(state.Start, current))
                return false;

            state.Active = true;
            EnsureTreeMarqueeAdorner(tree, state);
        }

        state.Adorner?.Update(state.Start, state.Current);
        return true;
    }

    private void ApplyPagesTreeMarqueeSelection(TreeMarqueeSelectionState state)
    {
        Rect selectionRect = state.SelectionRect;
        HashSet<string> next = state.Additive
            ? new HashSet<string>(state.BaseSelection, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastHit = null;

        foreach (TreeViewItem item in EnumerateVisibleTreeItems(PagesTree))
        {
            if (IsRootPagesNode(item) || GetPagesNodePath(item) is not { } path)
                continue;
            if (!TreeItemIntersectsSelection(PagesTree, item, selectionRect))
                continue;

            next.Add(path);
            lastHit = path;
        }

        _pagesMultiSelection.Clear();
        foreach (string path in next)
            _pagesMultiSelection.Add(path);
        _pageTakeoffMultiSelection.Clear();
        if (!string.IsNullOrWhiteSpace(lastHit))
            _pagesRangeAnchorPath = lastHit;
        ApplyPagesMultiSelectionVisuals();
    }

    private void ApplyTakeoffsTreeMarqueeSelection(TreeMarqueeSelectionState state)
    {
        Rect selectionRect = state.SelectionRect;
        HashSet<string> next = state.Additive
            ? new HashSet<string>(state.BaseSelection, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? lastHit = null;

        foreach (TreeViewItem item in EnumerateVisibleTreeItems(TakeoffsTree))
        {
            if (!IsTakeoffsTreeMarqueeSelectable(item) || GetTakeoffNodePath(item) is not { } path)
                continue;
            if (!TreeItemIntersectsSelection(TakeoffsTree, item, selectionRect))
                continue;

            next.Add(path);
            lastHit = path;
        }

        _takeoffsMultiSelection.Clear();
        foreach (string path in next)
            _takeoffsMultiSelection.Add(path);
        _takeoffSectionMultiSelection.Clear();
        if (!string.IsNullOrWhiteSpace(lastHit))
            _takeoffsRangeAnchorPath = lastHit;
        ApplyTakeoffPageHighlights();
    }

    private static bool IsTakeoffsTreeMarqueeSelectable(TreeViewItem item) =>
        item.Tag is TakeoffItem ||
        item.Tag is TakeoffFolderNode { IsRoot: false };

    private void SelectPagesTreeMarqueeAnchorSilently()
    {
        TreeViewItem? anchor = EnumerateVisibleTreeItems(PagesTree)
            .FirstOrDefault(item =>
                GetPagesNodePath(item) is { } path &&
                _pagesMultiSelection.Contains(path));
        if (anchor == null)
            return;

        SelectPagesTreeItemSilently(anchor);
    }

    private void SelectTakeoffsTreeMarqueeAnchorSilently()
    {
        TreeViewItem? anchor = EnumerateVisibleTreeItems(TakeoffsTree)
            .FirstOrDefault(item =>
                GetTakeoffNodePath(item) is { } path &&
                _takeoffsMultiSelection.Contains(path));
        if (anchor == null)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            anchor.Focus();
            anchor.IsSelected = true;
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private static bool TreeItemIntersectsSelection(TreeView tree, TreeViewItem item, Rect selectionRect)
    {
        if (item.ActualHeight <= 0 || !item.IsVisible)
            return false;

        Rect itemRect = item
            .TransformToAncestor(tree)
            .TransformBounds(new Rect(0, 0, Math.Max(tree.ActualWidth, item.ActualWidth), item.ActualHeight));
        return selectionRect.IntersectsWith(itemRect);
    }

    private static bool MovementExceedsDragDistance(Point start, Point current) =>
        Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance ||
        Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;

    private static void EnsureTreeMarqueeAdorner(TreeView tree, TreeMarqueeSelectionState state)
    {
        if (state.Adorner != null)
            return;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(tree);
        if (layer == null)
            return;

        state.Adorner = new TreeSelectionMarqueeAdorner(tree);
        layer.Add(state.Adorner);
    }

    private static void EndTreeMarqueeSelection(TreeView tree, TreeMarqueeSelectionState state)
    {
        if (tree.IsMouseCaptured)
            tree.ReleaseMouseCapture();

        if (state.Adorner == null)
            return;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(tree);
        layer?.Remove(state.Adorner);
        state.Adorner = null;
    }

    private sealed class TreeMarqueeSelectionState
    {
        public TreeMarqueeSelectionState(
            Point start,
            bool additive,
            IEnumerable<string> baseSelection)
        {
            Start = start;
            Current = start;
            Additive = additive;
            BaseSelection = new HashSet<string>(baseSelection, StringComparer.OrdinalIgnoreCase);
        }

        public Point Start { get; }
        public Point Current { get; set; }
        public bool Active { get; set; }
        public bool Additive { get; }
        public HashSet<string> BaseSelection { get; }
        public TreeSelectionMarqueeAdorner? Adorner { get; set; }

        public Rect SelectionRect => new(
            Math.Min(Start.X, Current.X),
            Math.Min(Start.Y, Current.Y),
            Math.Abs(Current.X - Start.X),
            Math.Abs(Current.Y - Start.Y));
    }

    private sealed class TreeSelectionMarqueeAdorner : Adorner
    {
        private Point _start;
        private Point _current;

        public TreeSelectionMarqueeAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
        }

        public void Update(Point start, Point current)
        {
            _start = start;
            _current = current;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            Rect rect = new(
                Math.Min(_start.X, _current.X),
                Math.Min(_start.Y, _current.Y),
                Math.Abs(_current.X - _start.X),
                Math.Abs(_current.Y - _start.Y));
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            var fill = new SolidColorBrush(Color.FromArgb(48, 78, 161, 255));
            fill.Freeze();
            var stroke = new Pen(new SolidColorBrush(Color.FromArgb(220, 78, 161, 255)), 1);
            stroke.Freeze();
            drawingContext.DrawRectangle(fill, stroke, rect);
        }
    }
}
