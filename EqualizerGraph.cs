using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace G321Studio;

public sealed class EqualizerGraph : FrameworkElement
{
    // The dials use the same insets so each column sits below its own point.
    private const double LeftInset = 44;
    private const double RightInset = 8;
    private const double TopInset = 21;
    private const double BottomInset = 17;
    private const int MinimumGain = -12;
    private const int MaximumGain = 12;

    private static readonly int[] Levels = [12, 6, 0, -6, -12];
    private static readonly Typeface AxisTypeface = new("Segoe UI");
    private static readonly Brush PlotBackground = Solid(0xff, 0x14, 0x17, 0x1b);
    private static readonly Brush AxisLabels = Solid(0xff, 0xaa, 0xb4, 0xbf);
    private static readonly Brush ZeroLabel = Solid(0xff, 0xe1, 0xe6, 0xeb);
    private static readonly Brush BandHighlight = Solid(0x12, 0xb0, 0xc0, 0xcb);
    private static readonly Brush SignalFill = Solid(0x10, 0x59, 0xdc, 0xed);
    private static readonly Brush DotCenter = PlotBackground;
    private static readonly Brush Signal = Solid(0xff, 0x59, 0xdc, 0xed);
    private static readonly Pen GridLine = MakePen(Solid(0xff, 0x2a, 0x30, 0x36), 1);
    private static readonly Pen ZeroLine = MakePen(Solid(0xff, 0x6e, 0x7b, 0x86), 1, DashStyles.Dash);
    private static readonly Pen SignalLine = MakePen(Signal, 2);
    private static readonly Pen DotRing = MakePen(Signal, 1.5);

    private readonly float[] gains = new float[EqParameters.Frequencies.Length];
    private int hoveredBand = -1;
    private int draggedBand = -1;

    internal event Action<int, float>? GainChanged;

    internal void SetGains(float[] values)
    {
        if (values.Length != gains.Length)
            throw new ArgumentException(StudioText.Get("RtEqGainCount"), nameof(values));

        for (int band = 0; band < gains.Length; band++)
            gains[band] = float.IsFinite(values[band]) ? Math.Clamp(values[band], MinimumGain, MaximumGain) : 0;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect plot = PlotBounds;
        if (plot.Width <= 0 || plot.Height <= 0)
            return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        drawingContext.DrawRoundedRectangle(PlotBackground, null, plot, 6, 6);
        var points = new Point[gains.Length];
        double columnWidth = plot.Width / points.Length;
        int selectedBand = draggedBand >= 0 ? draggedBand : hoveredBand;
        for (int band = 0; band < points.Length; band++)
        {
            double x = plot.Left + columnWidth * (band + 0.5);
            points[band] = new Point(x, GainToY(plot, gains[band]));
            if (band == selectedBand)
                drawingContext.DrawRectangle(BandHighlight, null, new Rect(x - columnWidth / 2, plot.Top, columnWidth, plot.Height));
            drawingContext.DrawLine(GridLine, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }

        foreach (int level in Levels)
        {
            double y = GainToY(plot, level);
            drawingContext.DrawLine(level == 0 ? ZeroLine : GridLine, new Point(plot.Left, y), new Point(plot.Right, y));
            string label = level.ToString("+0;−0;0", CultureInfo.InvariantCulture);
            DrawAxisText(drawingContext, label, plot.Left - 9, y, pixelsPerDip, level == 0 ? ZeroLabel : AxisLabels);
        }
        DrawAxisText(drawingContext, "dB", plot.Left - 9, 8, pixelsPerDip, AxisLabels);

        drawingContext.DrawGeometry(SignalFill, null, BuildCurve(points, plot.Bottom));
        StreamGeometry curve = BuildCurve(points);
        drawingContext.DrawGeometry(null, SignalLine, curve);
        for (int band = 0; band < points.Length; band++)
        {
            bool selected = band == selectedBand;
            double radius = selected ? 5 : 3.5;
            drawingContext.DrawEllipse(selected ? Signal : DotCenter, DotRing, points[band], radius, radius);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Point position = e.GetPosition(this);
        int band = BandAt(position);
        if (band < 0 || !CaptureMouse())
            return;

        draggedBand = band;
        UpdateGain(position.Y);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point position = e.GetPosition(this);
        if (draggedBand >= 0)
        {
            UpdateGain(position.Y);
            e.Handled = true;
            return;
        }
        SetHoveredBand(BandAt(position));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHoveredBand(-1);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (draggedBand < 0)
            return;

        UpdateGain(e.GetPosition(this).Y);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        draggedBand = -1;
        SetHoveredBand(BandAt(e.GetPosition(this)));
        InvalidateVisual();
    }

    private Rect PlotBounds => new(
        LeftInset, TopInset,
        Math.Max(0, ActualWidth - LeftInset - RightInset),
        Math.Max(0, ActualHeight - TopInset - BottomInset));

    private int BandAt(Point position)
    {
        Rect plot = PlotBounds;
        if (plot.Width <= 0 || plot.Height <= 0 || position.X < plot.Left || position.X > plot.Right ||
            position.Y < plot.Top - 5 || position.Y > plot.Bottom + 5)
            return -1;
        return Math.Min(gains.Length - 1, (int)((position.X - plot.Left) / plot.Width * gains.Length));
    }

    private void UpdateGain(double y)
    {
        Rect plot = PlotBounds;
        if (plot.Height <= 0)
            return;
        float value = (float)Math.Round(MaximumGain - (y - plot.Top) / plot.Height * (MaximumGain - MinimumGain));
        value = Math.Clamp(value, MinimumGain, MaximumGain);
        if (gains[draggedBand] == value)
            return;

        gains[draggedBand] = value;
        InvalidateVisual();
        GainChanged?.Invoke(draggedBand, value);
    }

    private void SetHoveredBand(int band)
    {
        if (hoveredBand == band)
            return;
        hoveredBand = band;
        Cursor = band >= 0 ? Cursors.SizeNS : null;
        InvalidateVisual();
    }

    private static double GainToY(Rect plot, double gain) =>
        plot.Top + (MaximumGain - gain) / (MaximumGain - MinimumGain) * plot.Height;

    private static void DrawAxisText(DrawingContext context, string label, double right, double centerY, double pixelsPerDip, Brush brush)
    {
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            AxisTypeface, 11, brush, pixelsPerDip);
        context.DrawText(text, new Point(right - text.Width, centerY - text.Height / 2));
    }

    private static StreamGeometry BuildCurve(Point[] points, double? bottom = null)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(points[0], bottom.HasValue, bottom.HasValue);
            for (int band = 1; band < points.Length; band++)
            {
                Point left = points[band - 1], right = points[band];
                double offset = (right.X - left.X) / 3;
                path.BezierTo(new Point(left.X + offset, left.Y), new Point(right.X - offset, right.Y), right, true, false);
            }

            if (bottom is double y)
            {
                path.LineTo(new Point(points[^1].X, y), false, false);
                path.LineTo(new Point(points[0].X, y), false, false);
            }
        }
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Solid(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen(Brush brush, double thickness, DashStyle? dashes = null)
    {
        var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
        if (dashes is not null)
            pen.DashStyle = dashes;
        pen.Freeze();
        return pen;
    }
}
