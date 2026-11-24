using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace G321Studio;

internal sealed class EqualizerDial : Slider
{
    private const double SweepStart = -225;
    private const double SweepEnd = 45;
    private const double PixelsPerDecibel = 4;

    private static readonly Brush Face = ColorBrush(0x24, 0x2A, 0x31);
    private static readonly Brush HoverFace = ColorBrush(0x30, 0x38, 0x40);
    private static readonly Brush Indicator = ColorBrush(0xE2, 0xEB, 0xF2);
    private static readonly Brush Neutral = ColorBrush(0x8A, 0x9A, 0xA7);
    private static readonly Pen Rim = RoundPen(ColorBrush(0x42, 0x4C, 0x56), 1);
    private static readonly Pen Rail = RoundPen(ColorBrush(0x39, 0x42, 0x4B), 3.5);
    private static readonly Pen Gain = RoundPen(Neutral, 3.5);
    private static readonly Pen FocusOutline = RoundPen(ColorBrush(0xC5, 0xD6, 0xDF), 1);

    private double dragStartY;
    private double dragStartValue;

    public EqualizerDial()
    {
        // The Slider supplies keyboard and automation behavior. The dial draws its own track.
        Template = null;
        FocusVisualStyle = null;
        Width = Height = 48;
        Cursor = Cursors.SizeNS;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        double radius = Math.Min(RenderSize.Width, RenderSize.Height) / 2 - 6;
        if (radius < 8)
            return;

        var center = new Point(RenderSize.Width / 2, RenderSize.Height / 2);
        double angle = GetAngle(Value);
        double neutralAngle = GetAngle(Math.Clamp(0, Minimum, Maximum));

        drawingContext.PushOpacity(IsEnabled ? 1 : .45);
        if (IsKeyboardFocused || IsMouseCaptured)
            drawingContext.DrawEllipse(null, FocusOutline, center, radius + 4.5, radius + 4.5);

        drawingContext.DrawEllipse(IsMouseOver || IsMouseCaptured ? HoverFace : Face, Rim, center, radius - 5, radius - 5);
        DrawArc(drawingContext, Rail, center, radius, SweepStart, SweepEnd);
        DrawArc(drawingContext, Gain, center, radius,
            Math.Min(neutralAngle, angle), Math.Max(neutralAngle, angle));
        drawingContext.DrawEllipse(Neutral, null, PointAt(center, radius + 3.5, neutralAngle), 1.1, 1.1);
        drawingContext.DrawEllipse(Indicator, null, PointAt(center, radius, angle), 2.8, 2.8);
        drawingContext.Pop();
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ValueProperty || e.Property == MinimumProperty || e.Property == MaximumProperty ||
            e.Property == IsKeyboardFocusedProperty || e.Property == IsEnabledProperty ||
            e.Property == IsMouseOverProperty || e.Property == IsMouseCapturedProperty)
            InvalidateVisual();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Focus explicitly because the dial consumes the mouse-down event.
        Focus();
        if (e.ClickCount == 2)
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();
            SetCurrentValue(ValueProperty, Math.Clamp(0, Minimum, Maximum));
        }
        else
        {
            dragStartY = e.GetPosition(this).Y;
            dragStartValue = Value;
            CaptureMouse();
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
            return;

        UpdateDrag(e);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            base.OnMouseLeftButtonUp(e);
            return;
        }

        UpdateDrag(e);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!IsKeyboardFocused)
        {
            base.OnMouseWheel(e);
            return;
        }

        SetDialValue(Value + Math.Sign(e.Delta) * (IsSnapToTickEnabled ? TickFrequency : SmallChange));
        e.Handled = true;
    }

    private void UpdateDrag(MouseEventArgs e)
    {
        double distance = dragStartY - e.GetPosition(this).Y;
        SetDialValue(dragStartValue + distance / PixelsPerDecibel);
    }

    private void SetDialValue(double value)
    {
        if (IsSnapToTickEnabled && TickFrequency > 0)
            value = Minimum + Math.Round((value - Minimum) / TickFrequency) * TickFrequency;

        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
    }

    private double GetAngle(double value) => Maximum > Minimum
        ? SweepStart + (SweepEnd - SweepStart) * (value - Minimum) / (Maximum - Minimum)
        : SweepStart;

    private static Point PointAt(Point center, double radius, double angle)
    {
        double radians = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }

    private static void DrawArc(DrawingContext context, Pen pen, Point center, double radius, double start, double end)
    {
        if (end <= start)
            return;

        var arc = new StreamGeometry();
        using (var path = arc.Open())
        {
            path.BeginFigure(PointAt(center, radius, start), isFilled: false, isClosed: false);
            path.ArcTo(PointAt(center, radius, end), new Size(radius, radius), 0,
                isLargeArc: end - start > 180, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
        }

        context.DrawGeometry(null, pen, arc);
    }

    private static Brush ColorBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen RoundPen(Brush brush, double width)
    {
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
