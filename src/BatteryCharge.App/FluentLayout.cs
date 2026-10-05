using System.Windows.Forms.Layout;

namespace BatteryCharge.App;

internal abstract class FluentContentPanel : Panel
{
    private static readonly LayoutEngine ContentLayout = new ContentLayoutEngine();
    public override LayoutEngine LayoutEngine => ContentLayout;

    private sealed class ContentLayoutEngine : LayoutEngine
    {
        // The derived panels arrange their children explicitly. Running the default
        // engine first would resize AutoSize controls to their unconstrained size,
        // then resize them again, invalidating layout on every pass.
        public override bool Layout(object container, LayoutEventArgs layoutEventArgs) =>
            ((Control)container).AutoSize;
    }
}

// Content is measured at the available width, then grows vertically. In particular,
// no percentage/AutoSize table may enlarge a page beyond its scrolling viewport.
internal class FluentStackPanel : FluentContentPanel
{
    internal FluentStackPanel()
    {
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : Width);
        var height = Padding.Vertical;
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            height += Preferred(child, width - Padding.Horizontal).Height + child.Margin.Vertical;
        }
        return new Size(width, height);
    }

    private static Size Preferred(Control child, int width) => child.AutoSize || child is Label
        ? child.GetPreferredSize(new Size(Math.Max(1, width - child.Margin.Horizontal), 0)) : child.Size;

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var y = Padding.Top;
        var width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        foreach (Control child in Controls)
        {
            if (!child.Visible) continue;
            var size = Preferred(child, width);
            var stretch = child is Label or Panel || child.Dock != DockStyle.None;
            child.Bounds = new Rectangle(Padding.Left + child.Margin.Left, y + child.Margin.Top,
                stretch ? Math.Max(1, width - child.Margin.Horizontal) : Math.Min(size.Width, width), size.Height);
            y = child.Bottom + child.Margin.Bottom;
        }
    }
}

internal sealed class FluentRowPanel : FluentContentPanel
{
    private readonly Control? _leading;
    private readonly Control _description;
    private readonly Control? _action;
    private readonly bool _flexibleAction;

    internal FluentRowPanel(Control? leading, Control description, Control? action, bool flexibleAction = false)
    {
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = Padding.Empty;
        _leading = leading;
        _description = description;
        _action = action;
        _flexibleAction = flexibleAction;
        if (leading is not null) Controls.Add(leading);
        Controls.Add(description);
        if (action is not null) Controls.Add(action);
        foreach (Control child in Controls) child.Dock = DockStyle.None;
    }

    private (Rectangle Leading, Rectangle Description, Rectangle Action, int Height) Measure(int width)
    {
        var scale = DeviceDpi / 96f;
        var gap = (int)Math.Ceiling(12 * scale);
        var leadingWidth = _leading is null ? 0 : _leading.Width + _leading.Margin.Horizontal;
        var available = Math.Max(1, width - Padding.Horizontal - leadingWidth);
        var actionSize = _action is null ? Size.Empty : _action.AutoSize
            ? _action.GetPreferredSize(new Size(_flexibleAction ? Math.Max(1, available / 2) : 0, 0)) : _action.Size;
        var stacked = _action is not null && available < actionSize.Width + _action.Margin.Horizontal + 220 * scale;
        var actionWidth = _action is null ? 0 : _flexibleAction && !stacked
            ? available / 2 : Math.Min(actionSize.Width, available);
        var descriptionWidth = Math.Max(1, available - (stacked || _action is null ? 0 : actionWidth + gap));
        var descriptionSize = _description.GetPreferredSize(new Size(descriptionWidth, 0));
        var x = Padding.Left + leadingWidth;
        var description = new Rectangle(x, Padding.Top, descriptionWidth, descriptionSize.Height);
        var height = Math.Max(description.Height, _leading?.Height + _leading?.Margin.Vertical ?? 0);
        var action = Rectangle.Empty;
        if (_action is not null)
        {
            if (_flexibleAction) actionSize = _action.GetPreferredSize(new Size(stacked ? available : actionWidth, 0));
            action = new Rectangle(stacked ? x : x + descriptionWidth + gap,
                stacked ? Padding.Top + height + gap : Padding.Top + Math.Max(0, (description.Height - actionSize.Height) / 2),
                _flexibleAction && stacked ? available : actionWidth, actionSize.Height);
            height = Math.Max(height, action.Bottom - Padding.Top);
        }
        var leading = _leading is null ? Rectangle.Empty : new Rectangle(Padding.Left + _leading.Margin.Left,
            Padding.Top + _leading.Margin.Top, _leading.Width, _leading.Height);
        return (leading, description, action, height + Padding.Vertical);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : Width);
        return new Size(width, Measure(width).Height);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        // Adding the first child can raise Layout before construction has finished.
        if (_description is null) return;
        var bounds = Measure(ClientSize.Width);
        if (_leading is not null) _leading.Bounds = bounds.Leading;
        _description.Bounds = bounds.Description;
        if (_action is not null) _action.Bounds = bounds.Action;
    }
}

internal sealed class FluentModePanel : FluentContentPanel
{
    internal bool Stacked => Width < 580 * DeviceDpi / 96f;

    internal FluentModePanel()
    {
        DoubleBuffered = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
        Margin = new Padding(0, 12, 0, 12);
    }

    private Size Measure(int width)
    {
        var gap = (int)Math.Ceiling(10 * DeviceDpi / 96f);
        var stacked = width < 580 * DeviceDpi / 96f;
        var cardWidth = Math.Max(1, stacked ? width : (width - gap * (Controls.Count - 1)) / Math.Max(1, Controls.Count));
        var height = 0;
        foreach (Control card in Controls)
        {
            var cardHeight = card.GetPreferredSize(new Size(cardWidth, 0)).Height;
            height = stacked ? height + cardHeight + gap : Math.Max(height, cardHeight);
        }
        return new Size(cardWidth, stacked ? Math.Max(0, height - gap) : height);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = Math.Max(1, proposedSize.Width > 1 ? proposedSize.Width : Width);
        return new Size(width, Measure(width).Height);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        var size = Measure(ClientSize.Width);
        var gap = (int)Math.Ceiling(10 * DeviceDpi / 96f);
        var offset = 0;
        foreach (Control card in Controls)
        {
            var height = Stacked ? card.GetPreferredSize(new Size(size.Width, 0)).Height : size.Height;
            card.Bounds = new Rectangle(Stacked ? 0 : offset, Stacked ? offset : 0, size.Width, height);
            offset += (Stacked ? height : size.Width) + gap;
        }
    }
}

internal sealed class FluentViewport : Panel
{
    private bool _layingOut;
    private int _scrollPreservationDepth;

    internal FluentViewport() { DoubleBuffered = true; AutoScroll = true; }

    internal IDisposable PreserveScroll() => new ScrollPositionScope(this);

    protected override Point ScrollToControl(Control activeControl) => _scrollPreservationDepth > 0
        ? AutoScrollPosition : base.ScrollToControl(activeControl);

    private sealed class ScrollPositionScope : IDisposable
    {
        private readonly FluentViewport _viewport;
        private readonly Point _position;
        private bool _disposed;

        internal ScrollPositionScope(FluentViewport viewport)
        {
            _viewport = viewport;
            _position = viewport.AutoScrollPosition;
            viewport._scrollPreservationDepth++;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _viewport._scrollPreservationDepth--;
            if (!_viewport.IsDisposed && _viewport.AutoScrollPosition != _position)
                // The getter returns negative offsets; the setter expects positive ones.
                _viewport.AutoScrollPosition = new Point(-_position.X, -_position.Y);
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) PerformLayout();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is not null) e.Control.Layout += PageLayoutChanged;
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        if (e.Control is not null) e.Control.Layout -= PageLayoutChanged;
        base.OnControlRemoved(e);
    }

    private void PageLayoutChanged(object? sender, LayoutEventArgs e)
    {
        if (!_layingOut && !Disposing) PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (_layingOut) return;
        _layingOut = true;
        try
        {
            // Reserve the scrollbar gutter even before it appears, so wrapping does
            // not oscillate between two widths when the content is near the limit.
            var width = Math.Max(1, ClientSize.Width - (VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth));
            foreach (Control page in Controls)
            {
                if (!page.Visible) continue;
                var height = page.GetPreferredSize(new Size(width, 0)).Height;
                page.Bounds = new Rectangle(AutoScrollPosition, new Size(width, height));
            }
            base.OnLayout(e);
        }
        finally { _layingOut = false; }
    }
}

internal sealed class FluentLabel : Label
{
    internal FluentLabel() { AutoSize = false; DoubleBuffered = true; }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout(this, nameof(Text));
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout(this, nameof(Font));
    }
}

internal sealed class FluentTableLayoutPanel : TableLayoutPanel
{
    internal FluentTableLayoutPanel() { DoubleBuffered = true; }
}

internal sealed class FluentLayoutBatch : IDisposable
{
    private readonly List<Control> _controls = [];

    internal FluentLayoutBatch(Control root) => Suspend(root);

    private void Suspend(Control control)
    {
        control.SuspendLayout();
        _controls.Add(control);
        foreach (Control child in control.Controls) Suspend(child);
    }

    public void Dispose()
    {
        // Lay out each container while its ancestors are still suspended. Merely
        // resuming the Form leaves unchanged-size child containers with stale bounds.
        for (var index = _controls.Count - 1; index >= 0; index--)
            _controls[index].ResumeLayout(performLayout: true);
    }
}
