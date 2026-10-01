using System.Globalization;
using System.Net;
using System.Text;

namespace ARIS1.Services
{
    // SVG markup for the risk donut and attendance-vs-average scatter, shared by the Teacher/Admin pages (via
    // MarkupString) and the PDF report (via QuestPDF's .Svg) so screen and print always draw the same chart.
    public static class RiskChartSvg
    {
        private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        // ---- Donut (stroke-dasharray technique, no charting library) ----

        public static string Donut(IEnumerable<(string Level, int Count, string Color)> bands, int total, int size = 96,
            string? ariaLabel = null, bool centerLabel = true)
        {
            var sb = new StringBuilder();
            var c = size / 2.0;
            var r = size * 40.0 / 96.0;
            var stroke = size * 12.0 / 96.0;
            var circumference = 2 * Math.PI * r;

            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 {size} {size}\" role=\"img\"");
            if (ariaLabel != null) sb.Append($" aria-label=\"{WebUtility.HtmlEncode(ariaLabel)}\"");
            sb.Append('>');
            sb.Append($"<circle cx=\"{N(c)}\" cy=\"{N(c)}\" r=\"{N(r)}\" fill=\"none\" stroke=\"#e9ecef\" stroke-width=\"{N(stroke)}\" />");

            if (total > 0)
            {
                double drawn = 0;
                foreach (var band in bands.Where(b => b.Count > 0))
                {
                    var dash = (double)band.Count / total * circumference;
                    sb.Append($"<circle cx=\"{N(c)}\" cy=\"{N(c)}\" r=\"{N(r)}\" fill=\"none\" stroke=\"{band.Color}\" stroke-width=\"{N(stroke)}\" " +
                              $"stroke-dasharray=\"{N(dash)} {N(circumference - dash)}\" stroke-dashoffset=\"{N(-drawn)}\" " +
                              $"transform=\"rotate(-90 {N(c)} {N(c)})\" />");
                    drawn += dash;
                }
                if (centerLabel)
                    sb.Append($"<text x=\"{N(c)}\" y=\"{N(c + size * 0.065)}\" font-family=\"Lato, Arial, Helvetica, sans-serif\" font-weight=\"bold\" " +
                              $"font-size=\"{N(size * 0.19)}\" fill=\"#1f2937\" text-anchor=\"middle\">{total}</text>");
            }

            sb.Append("</svg>");
            return sb.ToString();
        }

        // ---- Scatter: attendance % (x) vs academic average % (y) ----

        public const double ChartWidth = 620;
        public const double ChartHeight = 420;
        public const double ChartLeft = 60;
        public const double ChartTop = 20;
        public const double ChartRight = 20;
        public const double ChartBottomMargin = 50;
        public static double PlotWidth => ChartWidth - ChartLeft - ChartRight;
        public static double PlotHeight => ChartHeight - ChartTop - ChartBottomMargin;
        public static readonly int[] Ticks = { 0, 20, 40, 60, 80, 100 };

        public static double X(decimal attendancePercentage) =>
            ChartLeft + (double)Math.Clamp(attendancePercentage, 0, 100) / 100.0 * PlotWidth;

        public static double Y(decimal academicAverage) =>
            ChartTop + (1 - (double)Math.Clamp(academicAverage, 0, 100) / 100.0) * PlotHeight;

        // Gridlines, axes, tick labels and axis titles (everything except the dots). The Teacher/Admin AtRisk page
        // renders this inside its own <svg> and adds interactive dots on top; the PDF uses Scatter() below.
        public static string ScatterFrame()
        {
            var sb = new StringBuilder();
            const string font = "font-family=\"Lato, Arial, Helvetica, sans-serif\"";
            foreach (var tick in Ticks)
            {
                var gx = X(tick);
                var gy = Y(tick);
                sb.Append($"<line x1=\"{N(gx)}\" y1=\"{N(ChartTop)}\" x2=\"{N(gx)}\" y2=\"{N(ChartTop + PlotHeight)}\" stroke=\"#e1e0d9\" stroke-width=\"1\" />");
                sb.Append($"<line x1=\"{N(ChartLeft)}\" y1=\"{N(gy)}\" x2=\"{N(ChartLeft + PlotWidth)}\" y2=\"{N(gy)}\" stroke=\"#e1e0d9\" stroke-width=\"1\" />");
                sb.Append($"<text x=\"{N(gx)}\" y=\"{N(ChartTop + PlotHeight + 18)}\" {font} font-size=\"11\" fill=\"#898781\" text-anchor=\"middle\">{tick}</text>");
                sb.Append($"<text x=\"{N(ChartLeft - 8)}\" y=\"{N(gy + 4)}\" {font} font-size=\"11\" fill=\"#898781\" text-anchor=\"end\">{tick}</text>");
            }
            sb.Append($"<line x1=\"{N(ChartLeft)}\" y1=\"{N(ChartTop)}\" x2=\"{N(ChartLeft)}\" y2=\"{N(ChartTop + PlotHeight)}\" stroke=\"#c3c2b7\" stroke-width=\"1\" />");
            sb.Append($"<line x1=\"{N(ChartLeft)}\" y1=\"{N(ChartTop + PlotHeight)}\" x2=\"{N(ChartLeft + PlotWidth)}\" y2=\"{N(ChartTop + PlotHeight)}\" stroke=\"#c3c2b7\" stroke-width=\"1\" />");
            sb.Append($"<text x=\"{N(ChartLeft + PlotWidth / 2)}\" y=\"{N(ChartHeight - 8)}\" {font} font-size=\"12\" fill=\"#52514e\" text-anchor=\"middle\">Attendance %</text>");
            sb.Append($"<text x=\"12\" y=\"14\" {font} font-size=\"12\" fill=\"#52514e\">Academic Average %</text>");
            return sb.ToString();
        }

        public static string ScatterDot(decimal attendance, decimal average, string level) =>
            $"<circle cx=\"{N(X(attendance))}\" cy=\"{N(Y(average))}\" r=\"5\" fill=\"{RiskPalette.Color(level)}\" stroke=\"#fcfcfb\" stroke-width=\"2\" />";

        public static string Scatter(IEnumerable<RiskData> points)
        {
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {N(ChartWidth)} {N(ChartHeight)}\" width=\"{N(ChartWidth)}\" height=\"{N(ChartHeight)}\">");
            sb.Append(ScatterFrame());
            // Worst risk drawn last so a Critical dot is never hidden under a Low one.
            foreach (var p in points.OrderByDescending(p => RiskPalette.Rank(p.Level)))
                sb.Append(ScatterDot(p.AttendancePercentage, p.AcademicAverage, p.Level));
            sb.Append("</svg>");
            return sb.ToString();
        }
    }
}
