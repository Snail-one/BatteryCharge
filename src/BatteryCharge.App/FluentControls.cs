using System.Drawing.Drawing2D;
using System.ComponentModel;
using BatteryCharge.Core;

namespace BatteryCharge.App;

internal sealed class FluentSurface : Panel, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    internal FluentSurface()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Padding = new Padding(20);
        Margin = new Padding(0, 0, 0, 12);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Dock = DockStyle.Top;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8 * DeviceDpi / 96f);
        using var fill = new SolidBrush(Palette.Surface);
        using var border = new Pen(Palette.Border);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal enum FluentButtonKind { Standard, Accent, Subtle, Navigation, Danger }

internal sealed class FluentButton : Button, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    private bool _hover;
    private bool _pressed;
    private bool _selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal FluentButtonKind Kind { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal FluentGlyph? Glyph { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IconOnly { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected { get => _selected; set { _selected = value; Invalidate(); } }
    internal FluentButton()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(15, 8, 15, 8);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.Hand;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var text = TextRenderer.MeasureText(Text, Font);
        return new Size(IconOnly ? (int)(44 * scale) : text.Width + Padding.Horizontal + (Glyph.HasValue ? (int)(28 * scale) : 0),
            Math.Max(text.Height + Padding.Vertical, (int)(36 * scale)));
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var accent = Kind == FluentButtonKind.Accent;
        var background = accent ? (Enabled ? Palette.Accent : Palette.Border)
            : Selected ? Palette.AccentSoft : _hover || _pressed ? Palette.Hover : Kind is FluentButtonKind.Standard or FluentButtonKind.Danger ? Palette.Surface : Color.Transparent;
        var foreground = !Enabled ? Palette.Secondary : accent ? Palette.OnAccent
            : Selected ? Palette.Accent : Kind == FluentButtonKind.Danger ? Palette.Error : Palette.Text;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 5 * scale);
        using var fill = new SolidBrush(background);
        e.Graphics.FillPath(fill, path);
        if (accent && Enabled && (_hover || _pressed))
        {
            using var feedback = new SolidBrush(Color.FromArgb(_pressed ? 28 : 14,
                Palette.IsDark ? Color.Black : Color.White));
            e.Graphics.FillPath(feedback, path);
        }
        if (Kind is FluentButtonKind.Standard or FluentButtonKind.Danger)
        { using var pen = new Pen(Palette.Border); e.Graphics.DrawPath(pen, path); }
        if (Selected && Kind == FluentButtonKind.Navigation)
        { using var pen = new Pen(Palette.Accent, 3 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round }; e.Graphics.DrawLine(pen, 2 * scale, Height / 2f - 8 * scale, 2 * scale, Height / 2f + 8 * scale); }
        var x = Padding.Left;
        if (Glyph is FluentGlyph glyph)
        {
            var iconX = IconOnly ? (Width - 18 * scale) / 2 : x;
            FluentDrawing.Glyph(e.Graphics, glyph, new RectangleF(iconX, (Height - 18 * scale) / 2, 18 * scale, 18 * scale), foreground);
            x += (int)(28 * scale);
        }
        if (!IconOnly)
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x, 0, Math.Max(1, Width - x - Padding.Right), Height), foreground,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | (Kind == FluentButtonKind.Navigation ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), foreground, background);
    }
}

internal sealed class FluentToggle : CheckBox, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    internal FluentToggle()
    {
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        AutoCheck = false;
        AutoSize = false;
        BackColor = Color.Transparent;
        Size = new Size(104, 36);
        Anchor = AnchorStyles.Right;
        Margin = new Padding(16, 0, 0, 0);
        Cursor = Cursors.Hand;
    }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(2 * scale, (Height - 20 * scale) / 2, 40 * scale, 20 * scale);
        using var path = FluentDrawing.Rounded(rect, 10 * scale);
        using var fill = new SolidBrush(Checked && Enabled ? Palette.Accent : Palette.Surface);
        using var pen = new Pen(Enabled ? (Checked ? Palette.Accent : Palette.Secondary) : Palette.Border, scale);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
        using var knob = new SolidBrush(Checked && Enabled ? Palette.OnAccent : Palette.Secondary);
        e.Graphics.FillEllipse(knob, rect.X + (Checked ? 23 : 4) * scale, rect.Y + 4 * scale, 12 * scale, 12 * scale);
        TextRenderer.DrawText(e.Graphics, UiText.Get(Checked ? "On" : "Off"), Font,
            new Rectangle((int)(50 * scale), 0, Math.Max(1, Width - (int)(50 * scale)), Height), Palette.Secondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1), Palette.Text, Palette.Surface);
    }
}

internal sealed class ModeCard : RadioButton, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    private bool _hover;
    private bool _current;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal ChargeMode Mode { get; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string Description { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string CurrentText { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool IsCurrent { get => _current; set { _current = value; AccessibleDescription = Description + (value ? " " + CurrentText : ""); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    internal ModeCard(ChargeMode mode)
    {
        Mode = mode;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AutoCheck = false;
        AutoSize = true;
        Appearance = Appearance.Button;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 10, 0);
        Padding = new Padding(18);
        Cursor = Cursors.Hand;
    }
    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var width = proposedSize.Width > Padding.Horizontal ? proposedSize.Width : (int)(216 * scale);
        var textWidth = Math.Max(32, width - Padding.Horizontal);
        using var titleFont = new Font(Font, FontStyle.Bold);
        var titleHeight = TextRenderer.MeasureText(Text, titleFont, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;
        var descriptionHeight = TextRenderer.MeasureText(Description, Font, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;
        return new Size(width, Math.Max((int)(184 * scale), Padding.Vertical + (int)(66 * scale) + titleHeight + descriptionHeight));
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        var foreground = Enabled ? Palette.Text : Palette.Secondary;
        var background = Checked ? Palette.AccentSoft : _hover && Enabled ? Palette.Hover : Palette.Surface;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = FluentDrawing.Rounded(new RectangleF(scale, scale, Width - 2 * scale, Height - 2 * scale), 8 * scale);
        using var fill = new SolidBrush(background);
        using var border = new Pen(Checked ? Palette.Accent : Palette.Border, Checked ? 1.5f * scale : scale);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
        var glyph = Mode switch { ChargeMode.Conservation => FluentGlyph.Leaf, ChargeMode.RapidCharge => FluentGlyph.Bolt, _ => FluentGlyph.Plug };
        FluentDrawing.Glyph(e.Graphics, glyph, new RectangleF(Padding.Left, Padding.Top, 26 * scale, 26 * scale), Enabled ? Palette.Accent : Palette.Secondary);
        using var radioPen = new Pen(Checked ? Palette.Accent : Palette.Secondary, scale);
        var radio = new RectangleF(Width - Padding.Right - 16 * scale, Padding.Top + 4 * scale, 16 * scale, 16 * scale);
        e.Graphics.DrawEllipse(radioPen, radio);
        if (Checked) { using var dot = new SolidBrush(Palette.Accent); e.Graphics.FillEllipse(dot, RectangleF.Inflate(radio, -4 * scale, -4 * scale)); }
        using var titleFont = new Font(Font, FontStyle.Bold);
        var textWidth = Math.Max(1, Width - Padding.Horizontal);
        var y = Padding.Top + (int)(42 * scale);
        var titleHeight = TextRenderer.MeasureText(Text, titleFont, new Size(textWidth, 0), TextFormatFlags.WordBreak).Height;
        TextRenderer.DrawText(e.Graphics, Text, titleFont, new Rectangle(Padding.Left, y, textWidth, titleHeight), foreground, TextFormatFlags.WordBreak);
        y += titleHeight + (int)(7 * scale);
        TextRenderer.DrawText(e.Graphics, Description, Font, new Rectangle(Padding.Left, y, textWidth, Math.Max(1, Height - y - (int)(38 * scale))), Palette.Secondary, TextFormatFlags.WordBreak);
        if (IsCurrent)
        {
            FluentDrawing.Glyph(e.Graphics, FluentGlyph.Check, new RectangleF(Padding.Left, Height - 29 * scale, 14 * scale, 14 * scale), Palette.Success);
            TextRenderer.DrawText(e.Graphics, CurrentText, Font, new Rectangle(Padding.Left + (int)(19 * scale), Height - (int)(33 * scale), textWidth - (int)(19 * scale), (int)(23 * scale)), Palette.Success, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        if (Focused && ShowFocusCues)
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5), Palette.Text, background);
    }
}

internal sealed class GlyphView : Control, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal FluentGlyph Glyph { get; set; }
    internal GlyphView(FluentGlyph glyph, int size = 24)
    {
        Glyph = glyph;
        Size = new Size(size, size);
        Margin = new Padding(0, 4, 16, 0);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }
    protected override void OnPaint(PaintEventArgs e) =>
        FluentDrawing.Glyph(e.Graphics, Glyph, new RectangleF(1, 1, Width - 2, Height - 2), Palette.Accent);
}

internal sealed class BatteryMeter : Control, IFluentControl
{
    private FluentPalette _palette = FluentPalette.Light;
    private float? _level;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FluentPalette Palette { get => _palette; set { _palette = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal float? Level { get => _level; set { _level = value; Invalidate(); } }
    internal BatteryMeter()
    {
        Size = new Size(100, 100);
        Margin = new Padding(0, 0, 24, 0);
        Anchor = AnchorStyles.Left;
        TabStop = false;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var scale = DeviceDpi / 96f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var side = Math.Min(Width, Height) - 12 * scale;
        var circle = new RectangleF((Width - side) / 2, (Height - side) / 2, side, side);
        using var track = new Pen(Palette.Border, 5 * scale);
        using var progress = new Pen(Level is < .2f ? Palette.Error : Palette.Accent, 5 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawEllipse(track, circle);
        if (Level is float level && level > 0)
            e.Graphics.DrawArc(progress, circle, -90, Math.Clamp(level, 0, 1) * 360);
        FluentDrawing.Glyph(e.Graphics, FluentGlyph.Battery, new RectangleF(Width / 2f - 18 * scale, Height / 2f - 18 * scale, 36 * scale, 36 * scale), Palette.Accent);
    }
}
