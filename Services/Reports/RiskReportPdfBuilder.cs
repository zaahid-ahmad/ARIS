using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ARIS1.Services.Reports
{
    // Builds the principal's risk report. Stateless; every dynamic string (including the admin's notes) goes in as
    // plain text, never markup. Charts are the same SVG the pages render (RiskChartSvg).
    public class RiskReportPdfBuilder
    {
        private const string Ink = "#1f2937";
        private const string Muted = "#6b7280";
        private const string Line = "#d1d5db";
        private const string Accent = "#1e3a5f";

        static RiskReportPdfBuilder()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public byte[] Build(SchoolRiskOverview overview, RiskReportOptions options) =>
            BuildDocument(overview, options).GeneratePdf();

        public IDocument BuildDocument(SchoolRiskOverview overview, RiskReportOptions options)
        {
            var total = overview.Total;
            var narrative = RiskReportNarrative.Build(overview, options.Scope);
            var hasNotes = !string.IsNullOrWhiteSpace(options.Notes);
            var generated = options.GeneratedAt.ToString("d MMMM yyyy, HH:mm");

            return Document.Create(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(t => t.FontFamily("Lato").FontSize(10).FontColor(Ink));

                    page.Header().Column(h =>
                    {
                        h.Item().Row(r =>
                        {
                            r.RelativeItem().Text(overview.SchoolName).SemiBold().FontSize(9).FontColor(Accent);
                            r.RelativeItem().AlignRight().Text($"{options.Title}  |  {overview.AcademicYear}").FontSize(9).FontColor(Muted);
                        });
                        h.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Line);
                    });

                    page.Footer().Column(f =>
                    {
                        f.Item().LineHorizontal(0.75f).LineColor(Line);
                        f.Item().PaddingTop(4).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Generated {generated} by {options.AdminName}").FontSize(8).FontColor(Muted);
                                if (options.IncludeNames)
                                    c.Item().Text("CONFIDENTIAL - contains learner information").FontSize(8).SemiBold().FontColor("#b91c1c");
                            });
                            r.AutoItem().Text(t =>
                            {
                                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                                t.Span("Page ");
                                t.CurrentPageNumber();
                                t.Span(" of ");
                                t.TotalPages();
                            });
                        });
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(10);

                        // ---- Title block ----
                        col.Item().Column(t =>
                        {
                            t.Item().Text(options.Title).FontSize(22).Bold().FontColor(Accent);
                            t.Item().Text(overview.SchoolName).FontSize(13).SemiBold();
                            t.Item().PaddingTop(2).Text($"Academic year {overview.AcademicYear}  |  Scope: {options.ScopeLabel}")
                                .FontColor(Muted);
                        });

                        var section = 0;

                        // ---- 1. Overview ----
                        section++;
                        col.Item().Element(c => H1(c, $"{section}. Overview"));
                        col.Item().Element(c => H2(c, $"{section}.1 Summary"));
                        col.Item().Column(b =>
                        {
                            b.Spacing(3);
                            foreach (var line in narrative)
                                b.Item().Row(r =>
                                {
                                    r.AutoItem().PaddingRight(6).Text("-");
                                    r.RelativeItem().Text(line);
                                });
                        });
                        if (hasNotes)
                        {
                            col.Item().Element(c => H2(c, $"{section}.2 Notes from {options.AdminName}"));
                            col.Item().Border(0.75f).BorderColor(Line).Background("#f9fafb").Padding(8).Text(options.Notes.Trim());
                        }

                        // ---- 2. At a glance ----
                        section++;
                        col.Item().Element(c => H1(c, $"{section}. At a glance"));
                        col.Item().Row(r =>
                        {
                            r.Spacing(8);
                            r.RelativeItem().Element(c => Kpi(c, "Learners enrolled", total.Total.ToString(), Ink));
                            r.RelativeItem().Element(c => Kpi(c, "At risk", $"{total.AtRisk} ({total.AtRiskPercent:0.#}%)", RiskPalette.Color("High")));
                            r.RelativeItem().Element(c => Kpi(c, "Critical", total.Count("Critical").ToString(), RiskPalette.Color("Critical")));
                            r.RelativeItem().Element(c => Kpi(c, "High", total.Count("High").ToString(), RiskPalette.Color("High")));
                        });
                        col.Item().ShowEntire().Row(r =>
                        {
                            r.Spacing(16);
                            r.ConstantItem(150).Height(150).Svg(RiskChartSvg.Donut(total.Bands, total.Total, 150));
                            r.RelativeItem().AlignMiddle().Element(c => Legend(c, total));
                        });

                        // ---- 3. Risk by grade (whole school only) ----
                        if (options.Scope == RiskReportScope.School && overview.Grades.Count > 0)
                        {
                            section++;
                            var gradeHeading = $"{section}. Risk by grade";
                            // Heading, donuts and table stay together on one page.
                            col.Item().ShowEntire().Column(gs =>
                            {
                            gs.Spacing(10);
                            gs.Item().Element(c => H1(c, gradeHeading));
                            gs.Item().Row(r =>
                            {
                                r.Spacing(10);
                                foreach (var grade in overview.Grades)
                                {
                                    var agg = overview.ForGrade(grade);
                                    r.RelativeItem().Column(g =>
                                    {
                                        g.Item().AlignCenter().Width(90).Height(90).Svg(RiskChartSvg.Donut(agg.Bands, agg.Total, 90));
                                        g.Item().AlignCenter().Text($"Grade {grade}").SemiBold();
                                        g.Item().AlignCenter().Text($"{agg.AtRisk} of {agg.Total} at risk").FontSize(8).FontColor(Muted);
                                    });
                                }
                            });
                            gs.Item().Element(c => GradeTable(c, overview));
                            });
                        }

                        // ---- 4. Risk by subject / Subject detail ----
                        section++;
                        if (options.Scope == RiskReportScope.Subject && overview.Subjects.Count == 1)
                        {
                            var s = overview.Subjects[0];
                            col.Item().Element(c => H1(c, $"{section}. Subject detail: {s.SubjectName} (Grade {s.Grade})"));
                            col.Item().Text($"Teacher: {s.TeacherName}").FontColor(Muted);
                            col.Item().Element(c => SubjectTable(c, new List<SubjectRiskSummary> { s }));
                            if (s.Risks.Count > 0)
                            {
                                var chartHeading = $"{section}.1 Attendance versus academic average";
                                col.Item().ShowEntire().Column(chart =>
                                {
                                    chart.Spacing(6);
                                    chart.Item().Element(c => H2(c, chartHeading));
                                    chart.Item().AlignCenter().Width(400).Height(272)
                                        .Svg(RiskChartSvg.Scatter(s.Risks.Values));
                                    chart.Item().PaddingTop(4).Text(
                                        "Each dot is one learner. Dots toward the bottom-left combine low attendance with a low academic " +
                                        "average and are the most likely to be flagged. Colours match the risk bands above.")
                                        .FontSize(8).FontColor(Muted);
                                });
                            }
                        }
                        else
                        {
                            col.Item().Element(c => H1(c, $"{section}. Risk by subject"));
                            if (overview.Subjects.Count == 0)
                                col.Item().Text("No subjects in this scope.").FontColor(Muted);
                            else
                            {
                                foreach (var row in overview.Subjects.Chunk(3))
                                    col.Item().ShowEntire().Row(r =>
                                    {
                                        r.Spacing(10);
                                        foreach (var s in row)
                                            r.RelativeItem().Column(g =>
                                            {
                                                g.Item().AlignCenter().Width(80).Height(80).Svg(RiskChartSvg.Donut(s.Bands, s.TotalLearners, 80));
                                                g.Item().AlignCenter().Text($"{s.SubjectName} (Grade {s.Grade})").SemiBold().FontSize(9);
                                                g.Item().AlignCenter().Text(s.TeacherName).FontSize(8).FontColor(Muted);
                                            });
                                        for (var pad = row.Length; pad < 3; pad++) r.RelativeItem();
                                    });
                                col.Item().Element(c => SubjectTable(c, overview.Subjects
                                    .OrderByDescending(s => s.AtRiskPercent).ThenBy(s => s.SubjectName).ToList()));
                            }
                        }

                        // ---- Appendix A: at-risk learners (only when names were asked for) ----
                        var appendix = 'A';
                        if (options.IncludeNames)
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => H1(c, $"Appendix {appendix}. At-risk learners"));
                            appendix++;
                            var any = false;
                            foreach (var s in overview.Subjects)
                            {
                                var flagged = s.Risks.Values
                                    .Where(r => r.Score >= RiskAssessmentService.AtRiskThreshold)
                                    .OrderByDescending(r => r.Score).ToList();
                                if (flagged.Count == 0) continue;
                                any = true;
                                col.Item().Element(c => H2(c, $"{s.SubjectName} (Grade {s.Grade}) - {flagged.Count} at risk"));
                                col.Item().Element(c => LearnerTable(c, s, flagged));
                            }
                            if (!any) col.Item().Text("No learners in this scope are currently at risk.").FontColor(Muted);
                        }

                        // ---- Appendix: how the score works (always) ----
                        col.Item().Element(c => H1(c, $"Appendix {appendix}. How the risk score is calculated"));
                        col.Item().Column(m =>
                        {
                            m.Spacing(3);
                            m.Item().Text("Each learner has one risk score per subject, out of 100 (higher means more at risk):");
                            m.Item().Text("- 60% academic: the learner's weighted mark for their current term.");
                            m.Item().Text("- 30% attendance: their attendance in that subject.");
                            m.Item().Text("- 10% trend: how their recent assessments compare with their earlier ones.");
                            m.Item().PaddingTop(4).Text("Bands: Critical 55 or more; High 45 to 54.9; Moderate 30 to 44.9; Low below 30. " +
                                                        "\"At risk\" means High or Critical. In this report a learner is counted once, by their highest-risk subject.");
                        });
                    });
                });
            });
        }

        // ---- building blocks ----

        private static void H1(IContainer c, string text) =>
            c.PaddingTop(6).BorderBottom(1).BorderColor(Accent).PaddingBottom(2)
                .Text(text).FontSize(14).Bold().FontColor(Accent);

        private static void H2(IContainer c, string text) =>
            c.Text(text).FontSize(11).SemiBold();

        private static void Kpi(IContainer c, string label, string value, string color) =>
            c.Border(0.75f).BorderColor(Line).Padding(8).Column(k =>
            {
                k.Item().Text(label).FontSize(8).FontColor(Muted);
                k.Item().Text(value).FontSize(16).Bold().FontColor(color);
            });

        private static void Legend(IContainer c, RiskAggregate agg) =>
            c.Column(l =>
            {
                l.Spacing(4);
                foreach (var band in agg.Bands)
                {
                    var pct = agg.Total == 0 ? 0m : Math.Round(band.Count * 100m / agg.Total, 1);
                    l.Item().Row(r =>
                    {
                        r.ConstantItem(10).Height(10).Background(band.Color);
                        r.RelativeItem().PaddingLeft(6).Text($"{band.Level}: {band.Count} learner{(band.Count == 1 ? "" : "s")} ({pct:0.#}%)");
                    });
                }
                l.Item().PaddingTop(2).Text("Learners are counted once, by their highest-risk subject.").FontSize(8).FontColor(Muted);
            });

        private static IContainer HeadCell(IContainer c) =>
            c.Background("#eef2f7").BorderBottom(0.75f).BorderColor(Line).Padding(4);

        private static IContainer BodyCell(IContainer c) =>
            c.BorderBottom(0.5f).BorderColor(Line).Padding(4);

        private static void GradeTable(IContainer c, SchoolRiskOverview overview) =>
            c.Table(t =>
            {
                t.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(1.2f);
                    for (var i = 0; i < 6; i++) d.RelativeColumn();
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Grade", "Learners", "Critical", "High", "Moderate", "Low", "At-risk %" })
                        h.Cell().Element(HeadCell).Text(title).SemiBold().FontSize(9);
                });
                foreach (var grade in overview.Grades)
                {
                    var a = overview.ForGrade(grade);
                    foreach (var cell in new[] { $"Grade {grade}", a.Total.ToString(), a.Count("Critical").ToString(), a.Count("High").ToString(),
                                 a.Count("Moderate").ToString(), a.Count("Low").ToString(), $"{a.AtRiskPercent:0.#}%" })
                        t.Cell().Element(BodyCell).Text(cell).FontSize(9);
                }
            });

        private static void SubjectTable(IContainer c, List<SubjectRiskSummary> subjects) =>
            c.Table(t =>
            {
                t.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(2.6f); // subject
                    d.RelativeColumn(0.8f); // grade
                    d.RelativeColumn(2.2f); // teacher
                    d.RelativeColumn(1f);   // learners
                    for (var i = 0; i < 4; i++) d.RelativeColumn(0.8f);
                    d.RelativeColumn(1.1f); // at-risk %
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Subject", "Grade", "Teacher", "Learners", "Crit.", "High", "Mod.", "Low", "At-risk %" })
                        h.Cell().Element(HeadCell).Text(title).SemiBold().FontSize(8);
                });
                foreach (var s in subjects)
                {
                    foreach (var cell in new[] { s.SubjectName, s.Grade.ToString(), s.TeacherName, s.TotalLearners.ToString(),
                                 s.Count("Critical").ToString(), s.Count("High").ToString(), s.Count("Moderate").ToString(),
                                 s.Count("Low").ToString(), s.TotalLearners == 0 ? "-" : $"{s.AtRiskPercent:0.#}%" })
                        t.Cell().Element(BodyCell).Text(cell).FontSize(8);
                }
            });

        private static void LearnerTable(IContainer c, SubjectRiskSummary subject, List<RiskData> flagged) =>
            c.Table(t =>
            {
                t.ColumnsDefinition(d =>
                {
                    d.RelativeColumn(3f);
                    d.RelativeColumn(1f);
                    d.RelativeColumn(1.2f);
                    d.RelativeColumn(1f);
                    d.RelativeColumn(1.4f);
                    d.RelativeColumn(1.6f);
                });
                t.Header(h =>
                {
                    foreach (var title in new[] { "Learner", "Class", "Risk level", "Score", "Attendance %", "Academic avg %" })
                        h.Cell().Element(HeadCell).Text(title).SemiBold().FontSize(8);
                });
                foreach (var r in flagged)
                {
                    subject.Learners.TryGetValue(r.LearnerId, out var label);
                    foreach (var cell in new[] { label?.Name ?? $"Learner #{r.LearnerId}", label?.ClassName ?? "-", r.Level,
                                 r.Score.ToString("F1"), r.AttendancePercentage.ToString("F1"), r.AcademicAverage.ToString("F1") })
                        t.Cell().Element(BodyCell).Text(cell).FontSize(8);
                }
            });
    }
}
