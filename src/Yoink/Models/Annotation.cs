using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace Yoink.Models;

public enum AnnotationTool
{
    Arrow,
    Text,
    Highlighter,
    Rectangle,
    Blur
}

public abstract class AnnotationItem
{
    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeWidth { get; set; } = 2;
    public abstract void Render(DrawingContext dc);
}

public sealed class ArrowAnnotation : AnnotationItem
{
    public Point Start { get; set; }
    public Point End { get; set; }

    public override void Render(DrawingContext dc)
    {
        var pen = new Pen(new SolidColorBrush(StrokeColor), StrokeWidth);
        pen.Freeze();
        dc.DrawLine(pen, Start, End);

        double angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        double headLen = 12;
        double headAngle = Math.PI / 6;
        var p1 = new Point(
            End.X - headLen * Math.Cos(angle - headAngle),
            End.Y - headLen * Math.Sin(angle - headAngle));
        var p2 = new Point(
            End.X - headLen * Math.Cos(angle + headAngle),
            End.Y - headLen * Math.Sin(angle + headAngle));

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(End, true, true);
            ctx.LineTo(p1, true, false);
            ctx.LineTo(p2, true, false);
        }
        geo.Freeze();

        var brush = new SolidColorBrush(StrokeColor);
        brush.Freeze();
        dc.DrawGeometry(brush, pen, geo);
    }
}

public sealed class TextAnnotation : AnnotationItem
{
    public Point Position { get; set; }
    public string Text { get; set; } = "";

    public override void Render(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Text)) return;

        var typeface = new Typeface(new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var brush = new SolidColorBrush(StrokeColor);
        brush.Freeze();

        var formatted = new FormattedText(Text, System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, 16, brush, 96.0);

        var bgBrush = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0));
        bgBrush.Freeze();
        dc.DrawRoundedRectangle(bgBrush, null,
            new Rect(Position.X - 4, Position.Y - 2,
                formatted.Width + 8, formatted.Height + 4), 4, 4);

        dc.DrawText(formatted, Position);
    }
}

public sealed class HighlighterAnnotation : AnnotationItem
{
    public List<Point> Points { get; set; } = new();

    public override void Render(DrawingContext dc)
    {
        if (Points.Count < 2) return;

        var brush = new SolidColorBrush(Color.FromArgb(80, StrokeColor.R, StrokeColor.G, StrokeColor.B));
        brush.Freeze();
        var pen = new Pen(brush, StrokeWidth > 10 ? StrokeWidth : 16)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(Points[0], false, false);
            for (int i = 1; i < Points.Count; i++)
                ctx.LineTo(Points[i], true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }
}

public sealed class RectangleAnnotation : AnnotationItem
{
    public Rect Bounds { get; set; }

    public override void Render(DrawingContext dc)
    {
        var brush = new SolidColorBrush(StrokeColor);
        brush.Freeze();
        var pen = new Pen(brush, StrokeWidth);
        pen.Freeze();
        dc.DrawRectangle(null, pen, Bounds);
    }
}

public sealed class BlurAnnotation : AnnotationItem
{
    public Rect Bounds { get; set; }

    public override void Render(DrawingContext dc)
    {
        var brush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));
        brush.Freeze();
        dc.DrawRectangle(brush, null, Bounds);

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), 0.5);
        pen.Freeze();
        for (double x = Bounds.X; x < Bounds.Right; x += 8)
            dc.DrawLine(pen, new Point(x, Bounds.Y), new Point(x, Bounds.Bottom));
        for (double y = Bounds.Y; y < Bounds.Bottom; y += 8)
            dc.DrawLine(pen, new Point(Bounds.X, y), new Point(Bounds.Right, y));
    }
}
