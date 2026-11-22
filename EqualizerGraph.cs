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
    private static readonly Brush PlotBackground = Solid(0xff, 0x10, 0x18, 0x23);
    private static readonly Brush AxisLabels = Solid(0xff, 0xa5, 0xb3, 0xc4);
    private static readonly Brush ZeroLabel = Solid(0xff, 0xc8, 0xd9, 0xea);
    private static readonly Brush BandHighlight = Solid(0x10, 0x39, 0xb9, 0xff);
    private static readonly Brush SignalFill = MakeSignalFill();
    private static readonly Brush DotCenter = Solid(0xff, 0x12, 0x21, 0x32);
    private static readonly Brush ActiveDot = Solid(0xff, 0xa7, 0xfb, 0xff);
    private static readonly Brush DotHalo = Solid(0x24, 0x2f, 0xd8, 0xff);
    private static readonly Pen GridLine = MakePen(Solid(0xff, 0x25, 0x33, 0x44), 1);
    private static readonly Pen ZeroLine = MakePen(Solid(0xff, 0x53, 0x6c, 0x84), 1, DashStyles.Dash);
    private static readonly Pen SignalHalo = MakePen(Solid(0x19, 0x24, 0xd0, 0xff), 7);
    private static readonly Pen SignalLine = MakePen(Solid(0xff, 0x47, 0xe2, 0xfb), 2.2);
    private static readonly Pen DotRing = MakePen(Solid(0xff, 0x50, 0xe3, 0xfc), 1.8);

    private readonly float[] gains = new float[EqParameters.Frequencies.Length];
    private int hoveredBand = -1;
    private int draggedBand = -1;

    internal event Action<int, float>? GainChanged;

    internal void SetGains(float[] values)
    {
        if (values.Length != gains.Length)
            throw new ArgumentException("Uma leitura é necessária para cada banda do equalizador.", nameof(values));

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
        drawingContext.DrawGeometry(null, SignalHalo, curve);
        drawingContext.DrawGeometry(null, SignalLine, curve);
        for (int band = 0; band < points.Length; band++)
        {
            bool selected = band == selectedBand;
            if (selected)
                drawingContext.DrawEllipse(DotHalo, null, points[band], 11, 11);
            double radius = selected ? 5.6 : 4.1;
            drawingContext.DrawEllipse(selected ? ActiveDot : DotCenter, DotRing, points[band], radius, radius);
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

    private static Brush MakeSignalFill()
    {
        var brush = new LinearGradientBrush(Color.FromArgb(0x4d, 0x17, 0xb1, 0xf7), Color.FromArgb(0x06, 0x14, 0x74, 0xb5), 90);
        brush.Freeze();
        return brush;
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
