using System.Drawing;

namespace OddSnap.Models;

/// <summary>Base for all annotation types stored in the undo stack.</summary>
public abstract record Annotation;

public sealed record DrawStroke(List<Point> Points, Color Color) : Annotation;
public sealed record BlurRect(Rectangle Rect) : Annotation;
public sealed record ArrowAnnotation(Point From, Point To, Color Color) : Annotation;
public sealed record CurvedArrowAnnotation(List<Point> Points, Color Color) : Annotation;
public sealed record HighlightAnnotation(Rectangle Rect, Color Color) : Annotation;
public sealed record StepNumberAnnotation(Point Pos, int Number, Color Color) : Annotation;
public sealed record EraserFill(Rectangle Rect, Color Color) : Annotation;
public sealed record TextAnnotation(Point Pos, string Text, float FontSize, Color Color, bool Bold, bool Italic, bool Stroke, bool Shadow, bool Background, string FontFamily) : Annotation;
public sealed record MagnifierAnnotation(Point Pos, Rectangle SrcRect) : Annotation;
public sealed record EmojiAnnotation(Point Pos, string Emoji, float Size) : Annotation;
public sealed record LineAnnotation(Point From, Point To, Color Color) : Annotation;
public sealed record RulerAnnotation(Point From, Point To) : Annotation;
public sealed record RectShapeAnnotation(Rectangle Rect, Color Color) : Annotation;
public sealed record CircleShapeAnnotation(Rectangle Rect, Color Color) : Annotation;
