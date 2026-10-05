using System.Drawing.Drawing2D;

namespace BatteryCharge.App;

internal enum FluentGlyph { Battery, Overview, Settings, Plug, Leaf, Bolt, Moon, Language, Refresh, Tray, Exit, Info, Check }

internal static class FluentDrawing
{
    internal static GraphicsPath Rounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        rect.Width = Math.Max(1, rect.Width);
        rect.Height = Math.Max(1, rect.Height);
        var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void Glyph(Graphics graphics, FluentGlyph glyph, RectangleF bounds, Color color)
    {
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (glyph)
        {
            case FluentGlyph.Battery:
                using (var outline = Rounded(new RectangleF(2, 6, 18, 12), 2)) graphics.DrawPath(pen, outline);
                graphics.DrawLine(pen, 22, 10, 22, 14);
                graphics.FillRectangle(brush, 5, 9, 10, 6);
                break;
            case FluentGlyph.Overview:
                foreach (var rect in new[] { new RectangleF(3, 3, 7, 7), new RectangleF(14, 3, 7, 7), new RectangleF(3, 14, 7, 7), new RectangleF(14, 14, 7, 7) })
                { using var square = Rounded(rect, 1.5f); graphics.DrawPath(pen, square); }
                break;
            case FluentGlyph.Settings:
                graphics.DrawEllipse(pen, 8, 8, 8, 8);
                graphics.DrawEllipse(pen, 4, 4, 16, 16);
                for (var angle = 0; angle < 360; angle += 60)
                {
                    var a = angle * Math.PI / 180;
                    graphics.DrawLine(pen, 12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8,
                        12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10);
                }
                break;
            case FluentGlyph.Plug:
                graphics.DrawLine(pen, 8, 3, 8, 8); graphics.DrawLine(pen, 16, 3, 16, 8);
                graphics.DrawLines(pen, [new(5, 8), new(19, 8), new(19, 11), new(15, 16), new(9, 16), new(5, 11), new(5, 8)]);
                graphics.DrawLine(pen, 12, 16, 12, 22);
                break;
            case FluentGlyph.Leaf:
                using (var leaf = new GraphicsPath())
                {
                    leaf.AddBezier(5, 18, 0, 8, 12, 3, 21, 3);
                    leaf.AddBezier(21, 3, 21, 15, 15, 23, 5, 18);
                    graphics.DrawPath(pen, leaf);
                }
                graphics.DrawLine(pen, 3, 21, 15, 9);
                break;
            case FluentGlyph.Bolt:
                graphics.DrawPolygon(pen, [new(14, 2), new(5, 13), new(11, 13), new(10, 22), new(19, 10), new(13, 10)]);
                break;
            case FluentGlyph.Moon:
                using (var moon = new GraphicsPath())
                {
                    moon.AddBezier(19, 17, 8, 22, 1, 12, 8, 4);
                    moon.AddBezier(8, 4, 6, 14, 12, 18, 19, 17);
                    graphics.DrawPath(pen, moon);
                }
                break;
            case FluentGlyph.Language:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.DrawEllipse(pen, 8, 3, 8, 18);
                graphics.DrawLine(pen, 3, 12, 21, 12); graphics.DrawLine(pen, 5, 7, 19, 7);
                graphics.DrawLine(pen, 5, 17, 19, 17);
                break;
            case FluentGlyph.Refresh:
                graphics.DrawArc(pen, 4, 4, 16, 16, 45, 290);
                graphics.DrawLines(pen, [new(20, 4), new(20, 10), new(14, 10)]);
                break;
            case FluentGlyph.Tray:
                graphics.DrawLines(pen, [new(3, 14), new(3, 20), new(21, 20), new(21, 14)]);
                graphics.DrawLine(pen, 12, 3, 12, 15);
                graphics.DrawLines(pen, [new(7, 10), new(12, 15), new(17, 10)]);
                break;
            case FluentGlyph.Exit:
                graphics.DrawArc(pen, 4, 4, 16, 16, -60, 300); graphics.DrawLine(pen, 12, 2, 12, 12);
                break;
            case FluentGlyph.Info:
                graphics.DrawEllipse(pen, 3, 3, 18, 18); graphics.FillEllipse(brush, 11, 6, 2, 2);
                graphics.DrawLine(pen, 12, 11, 12, 17);
                break;
            case FluentGlyph.Check:
                graphics.DrawLines(pen, [new(5, 12), new(10, 17), new(19, 7)]);
                break;
        }
        graphics.Restore(state);
    }
}

internal interface IFluentControl
{
    FluentPalette Palette { get; set; }
}
