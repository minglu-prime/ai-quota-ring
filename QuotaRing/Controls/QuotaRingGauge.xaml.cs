using System.Windows;
using System.Windows.Media;
using QuotaRing.Models;
using QuotaRing.Services;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using UserControl = System.Windows.Controls.UserControl;

namespace QuotaRing.Controls;

public partial class QuotaRingGauge : UserControl
{
    private const double Center = 27.0;
    private const double Radius = 23.0;

    public QuotaRingGauge()
    {
        InitializeComponent();
    }

    public void SetData(QuotaRingItem item, bool darkTheme, DateTimeOffset now)
    {
        var remaining = item.ClampedRemaining;
        var used = item.UsedPercent;
        var colorHex = QuotaParsers.GetRingColorHex(remaining);
        var ringBrush = CreateBrush(colorHex);

        ApplyTheme(darkTheme);
        PercentText.Text = $"{Math.Round(remaining):0}%";
        TitleText.Text = string.IsNullOrEmpty(item.SubLabel)
            ? item.Label
            : $"{item.SubLabel} {item.Label}";
        SubText.Text = QuotaParsers.FormatCountdown(item.ResetsAt, now);

        var resetDetail = item.ResetsAt is not null
            ? $" · 重置于 {item.ResetsAt.Value.ToLocalTime():MM-dd HH:mm}"
            : "";
        ToolTip = $"剩余 {remaining:0.0}% · 已用 {used:0.0}%{resetDetail}";

        RenderArc(remaining, ringBrush);
    }

    public void SetEmpty(string label, string hint, bool darkTheme)
    {
        ApplyTheme(darkTheme);
        PercentText.Text = "--";
        TitleText.Text = label;
        SubText.Text = hint;
        ToolTip = $"{label}: {hint}";
        FullRingEllipse.Visibility = Visibility.Collapsed;
        ArcPath.Data = null;
    }

    private void ApplyTheme(bool darkTheme)
    {
        TrackEllipse.Stroke = CreateBrush(darkTheme ? "#26FFFFFF" : "#1C0F172A");
        PercentText.Foreground = CreateBrush(darkTheme ? "#F8FAFC" : "#0F172A");
        TitleText.Foreground = CreateBrush(darkTheme ? "#CBD5E1" : "#334155");
        SubText.Foreground = CreateBrush(darkTheme ? "#94A3B8" : "#64748B");
    }

    private void RenderArc(double remainingPercent, Brush brush)
    {
        if (remainingPercent <= 0.1)
        {
            FullRingEllipse.Visibility = Visibility.Collapsed;
            ArcPath.Data = null;
            return;
        }

        if (remainingPercent >= 99.9)
        {
            FullRingEllipse.Stroke = brush;
            FullRingEllipse.Visibility = Visibility.Visible;
            ArcPath.Data = null;
            return;
        }

        FullRingEllipse.Visibility = Visibility.Collapsed;
        ArcPath.Stroke = brush;

        var sweepAngle = remainingPercent / 100.0 * 360.0;
        var startPoint = new Point(Center, Center - Radius);
        var endRad = (sweepAngle - 90.0) * Math.PI / 180.0;
        var endPoint = new Point(
            Center + Radius * Math.Cos(endRad),
            Center + Radius * Math.Sin(endRad));

        var figure = new PathFigure
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };
        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(Radius, Radius),
            IsLargeArc = sweepAngle > 180.0,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        ArcPath.Data = geometry;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
