namespace ARIS1.Services.Reports
{
    public enum RiskReportScope { School, Grade, Subject }

    public class RiskReportOptions
    {
        public string Title { get; set; } = "Academic Risk Report";
        public string Notes { get; set; } = string.Empty;
        public bool IncludeNames { get; set; }
        public RiskReportScope Scope { get; set; } = RiskReportScope.School;
        public string ScopeLabel { get; set; } = "Whole school";
        public string AdminName { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }

    // The auto-written Overview bullets. Deterministic: same overview in, same sentences out, every number taken
    // straight from SchoolRiskOverview so it always matches the on-screen page and the tables later in the report.
    public static class RiskReportNarrative
    {
        public static List<string> Build(SchoolRiskOverview overview, RiskReportScope scope)
        {
            var bullets = new List<string>();
            var total = overview.Total;
            var withLearners = overview.Subjects.Where(s => s.TotalLearners > 0).ToList();
            var empty = overview.Subjects.Where(s => s.TotalLearners == 0).ToList();

            if (overview.Subjects.Count == 0)
            {
                bullets.Add("There are no subjects in this scope for the current academic year, so there is no risk data to report.");
                return bullets;
            }

            bullets.Add($"{total.Total} learner{Plural(total.Total)} {(total.Total == 1 ? "is" : "are")} enrolled across " +
                        $"{overview.Subjects.Count} subject{Plural(overview.Subjects.Count)}; {total.AtRisk} ({total.AtRiskPercent:0.#}%) " +
                        $"{(total.AtRisk == 1 ? "is" : "are")} at risk (a risk score of {RiskAssessmentService.AtRiskThreshold:0} or more in at least one subject).");

            bullets.Add($"By highest-risk subject: {total.Count("Critical")} Critical, {total.Count("High")} High, " +
                        $"{total.Count("Moderate")} Moderate and {total.Count("Low")} Low. Each learner is counted once.");

            if (scope == RiskReportScope.School)
            {
                var grades = overview.Grades
                    .Select(g => (Grade: g, Agg: overview.ForGrade(g)))
                    .Where(x => x.Agg.Total > 0)
                    .OrderByDescending(x => x.Agg.AtRiskPercent)
                    .ToList();
                if (grades.Count > 1)
                    bullets.Add($"Grade {grades[0].Grade} has the highest share of at-risk learners ({grades[0].Agg.AtRisk} of " +
                                $"{grades[0].Agg.Total}, {grades[0].Agg.AtRiskPercent:0.#}%).");
            }

            if (scope != RiskReportScope.Subject && withLearners.Count > 1)
            {
                var top = withLearners
                    .OrderByDescending(s => s.AtRiskPercent).ThenByDescending(s => s.AtRisk).ThenBy(s => s.SubjectName)
                    .Take(3)
                    .Select(s => $"{s.SubjectName} (Grade {s.Grade}, {s.AtRiskPercent:0.#}%)");
                bullets.Add($"Subjects with the highest at-risk share: {string.Join("; ", top)}.");
            }

            if (empty.Count > 0)
                bullets.Add($"{empty.Count} subject{Plural(empty.Count)} {(empty.Count == 1 ? "has" : "have")} no enrolled learners: " +
                            $"{string.Join("; ", empty.Take(6).Select(s => $"{s.SubjectName} (Grade {s.Grade})"))}" +
                            $"{(empty.Count > 6 ? $" and {empty.Count - 6} more" : "")}.");

            return bullets;
        }

        private static string Plural(int n) => n == 1 ? "" : "s";
    }
}
