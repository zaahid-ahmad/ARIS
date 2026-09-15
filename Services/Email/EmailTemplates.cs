using System.Net;
using System.Text;

namespace ARIS1.Services.Email
{
    public record RenderedEmail(string Subject, string Html, string Text);

    // Shared email layout plus the individual templates. Everything dynamic goes through Encode(); the only
    // raw HTML inserted is markup built here. Inline styles only, since most email clients strip <style>.
    public static class EmailTemplates
    {
        public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        // Plain text → HTML paragraphs (blank line = new paragraph, single newline = <br>).
        public static string Paragraphs(string text) =>
            string.Concat((text ?? string.Empty).Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(p => $"<p style=\"margin:0 0 14px\">{Encode(p.Trim()).Replace("\n", "<br>")}</p>"));

        public static string Button(string url, string label) =>
            $"<p style=\"margin:20px 0\"><a href=\"{Encode(url)}\" style=\"background:#1f6feb;color:#ffffff;padding:10px 18px;" +
            $"border-radius:6px;text-decoration:none;font-weight:600;display:inline-block\">{Encode(label)}</a></p>";

        public static string Layout(string heading, string? schoolName, string bodyHtml, bool notificationFooter)
        {
            var footer = notificationFooter
                ? "You are receiving this because you are linked to a learner in ARIS. You can turn off these emails " +
                  "from your ARIS Parent dashboard."
                : "This is an automated account email from ARIS.";

            return $@"<!doctype html>
<html><body style=""margin:0;padding:0;background:#f3f5f8;font-family:Segoe UI,Arial,sans-serif;color:#1d2733"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" style=""background:#f3f5f8;padding:24px 0"">
<tr><td align=""center"">
<table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" style=""max-width:600px;width:100%;background:#ffffff;border-radius:10px;overflow:hidden"">
<tr><td style=""background:#1f3a5f;color:#ffffff;padding:18px 24px"">
<div style=""font-size:18px;font-weight:700"">ARIS</div>
<div style=""font-size:13px;opacity:.85"">{Encode(schoolName ?? "Assessment and Reporting Information System")}</div>
</td></tr>
<tr><td style=""padding:24px;font-size:15px;line-height:1.55"">
<h2 style=""margin:0 0 16px;font-size:19px"">{Encode(heading)}</h2>
{bodyHtml}
</td></tr>
<tr><td style=""padding:14px 24px;background:#f8fafc;color:#5a6e82;font-size:12px"">{Encode(footer)}</td></tr>
</table>
</td></tr></table>
</body></html>";
        }

        // ---- Account ----

        public static RenderedEmail PasswordReset(string fullname, string resetUrl, string? schoolName)
        {
            var html = Layout("Reset your password", schoolName,
                $"<p style=\"margin:0 0 14px\">Hi {Encode(fullname)},</p>" +
                "<p style=\"margin:0 0 14px\">We received a request to reset your ARIS password. Click the button below to choose a new one.</p>" +
                Button(resetUrl, "Reset password") +
                "<p style=\"margin:0 0 14px;color:#5a6e82;font-size:13px\">If you didn't ask for this, you can ignore this email; your password won't change.</p>",
                notificationFooter: false);
            var text = $"Hi {fullname},\n\nReset your ARIS password here:\n{resetUrl}\n\nIf you didn't ask for this, ignore this email.";
            return new RenderedEmail("Reset your ARIS password", html, text);
        }

        public static RenderedEmail Welcome(string fullname, string role, string email, string? schoolCode, string setPasswordUrl, string? schoolName)
        {
            var html = Layout("Welcome to ARIS", schoolName,
                $"<p style=\"margin:0 0 14px\">Hi {Encode(fullname)},</p>" +
                $"<p style=\"margin:0 0 14px\">An ARIS {Encode(role)} account has been created for you" +
                (schoolName != null ? $" at {Encode(schoolName)}" : "") + ".</p>" +
                "<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:0 0 14px;font-size:14px\">" +
                $"<tr><td style=\"color:#5a6e82\">Sign-in email</td><td><strong>{Encode(email)}</strong></td></tr>" +
                (schoolCode != null ? $"<tr><td style=\"color:#5a6e82\">School code</td><td><strong>{Encode(schoolCode)}</strong></td></tr>" : "") +
                "</table>" +
                "<p style=\"margin:0 0 14px\">Click below to choose your password.</p>" +
                Button(setPasswordUrl, "Set my password") +
                "<p style=\"margin:0 0 14px;color:#5a6e82;font-size:13px\">This link expires. If it has, use \"Forgot password?\" on the sign-in page.</p>",
                notificationFooter: false);
            var text = $"Hi {fullname},\n\nAn ARIS {role} account has been created for you{(schoolName != null ? $" at {schoolName}" : "")}.\n" +
                       $"Sign-in email: {email}\n" + (schoolCode != null ? $"School code: {schoolCode}\n" : "") +
                       $"\nSet your password here:\n{setPasswordUrl}";
            return new RenderedEmail("Your ARIS account is ready", html, text);
        }

        public static RenderedEmail ConfirmEmail(string fullname, string confirmUrl, string? schoolName)
        {
            var html = Layout("Confirm your email", schoolName,
                $"<p style=\"margin:0 0 14px\">Hi {Encode(fullname)},</p>" +
                "<p style=\"margin:0 0 14px\">Please confirm your email address for ARIS.</p>" +
                Button(confirmUrl, "Confirm email"),
                notificationFooter: false);
            return new RenderedEmail("Confirm your ARIS email", html, $"Hi {fullname},\n\nConfirm your email:\n{confirmUrl}");
        }

        // ---- Parent notifications ----

        public static RenderedEmail StaffMessage(string parentName, IEnumerable<string> childNames, string senderName,
            string subject, string body, string? schoolName)
        {
            var children = string.Join(", ", childNames);
            var html = Layout(subject, schoolName,
                $"<p style=\"margin:0 0 14px\">Dear {Encode(parentName)},</p>" +
                Paragraphs(body) +
                $"<p style=\"margin:18px 0 0;color:#5a6e82;font-size:13px\">Sent by {Encode(senderName)} regarding {Encode(children)}.</p>",
                notificationFooter: true);
            var text = $"Dear {parentName},\n\n{body}\n\nSent by {senderName} regarding {children}.";
            return new RenderedEmail(subject, html, text);
        }

        public static RenderedEmail RiskAlert(string parentName, string childName, string subjectName, string level,
            decimal score, decimal academicAverage, decimal attendance, IReadOnlyList<string> weakTopics, string overviewUrl, string? schoolName)
        {
            var levelColour = level == "Critical" ? "#d03b3b" : "#ec835a";
            var topicsHtml = weakTopics.Count == 0 ? "" :
                "<p style=\"margin:0 0 6px\">Topics that need attention:</p><ul style=\"margin:0 0 14px;padding-left:20px\">" +
                string.Concat(weakTopics.Select(t => $"<li>{Encode(t)}</li>")) + "</ul>";

            var html = Layout($"{childName}: {subjectName} needs attention", schoolName,
                $"<p style=\"margin:0 0 14px\">Dear {Encode(parentName)},</p>" +
                $"<p style=\"margin:0 0 14px\">After recent marks were captured, {Encode(childName)}'s progress in <strong>{Encode(subjectName)}</strong> " +
                $"has been flagged as <span style=\"background:{levelColour};color:#fff;padding:2px 8px;border-radius:10px;font-weight:600\">{Encode(level)}</span>.</p>" +
                "<table role=\"presentation\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;margin:0 0 14px;font-size:14px\">" +
                $"<tr><td style=\"color:#5a6e82\">Risk score</td><td><strong>{score:F0}</strong> / 100</td></tr>" +
                $"<tr><td style=\"color:#5a6e82\">Current term mark</td><td><strong>{academicAverage:F1}%</strong></td></tr>" +
                $"<tr><td style=\"color:#5a6e82\">Attendance</td><td><strong>{attendance:F1}%</strong></td></tr></table>" +
                topicsHtml +
                "<p style=\"margin:0 0 14px\">We encourage you to discuss this with your child and, if needed, contact their teacher.</p>" +
                Button(overviewUrl, "View in ARIS"),
                notificationFooter: true);

            var text = $"Dear {parentName},\n\n{childName}'s progress in {subjectName} has been flagged as {level}.\n" +
                       $"Risk score: {score:F0}/100\nCurrent term mark: {academicAverage:F1}%\nAttendance: {attendance:F1}%\n" +
                       (weakTopics.Count > 0 ? $"Topics that need attention: {string.Join(", ", weakTopics)}\n" : "") +
                       $"\nView in ARIS: {overviewUrl}";
            return new RenderedEmail($"ARIS alert: {childName} – {subjectName} ({level})", html, text);
        }

        public record SummaryRow(string Subject, decimal? TermMark, decimal? YearMark, decimal? Attendance, string RiskLevel);

        public static RenderedEmail ProgressSummary(string parentName, string childName, int grade, int term,
            IReadOnlyList<SummaryRow> rows, IReadOnlyList<string> concernTopics, string marksUrl, string? schoolName)
        {
            static string Pct(decimal? v) => v.HasValue ? $"{v.Value:F1}%" : "–";
            static string RiskColour(string level) => level switch
            {
                "Critical" => "#d03b3b", "High" => "#ec835a", "Moderate" => "#fab219", "Low" => "#0ca30c", _ => "#9ca3af"
            };

            var sb = new StringBuilder();
            sb.Append("<table role=\"presentation\" cellpadding=\"8\" cellspacing=\"0\" width=\"100%\" style=\"border-collapse:collapse;font-size:14px;margin:0 0 16px\">");
            sb.Append("<tr style=\"background:#f1f5f9;text-align:left\"><th>Subject</th><th>Term " + term + "</th><th>Year</th><th>Attendance</th><th>Risk</th></tr>");
            foreach (var r in rows)
            {
                var riskText = r.RiskLevel == "Moderate" ? "#1d2733" : "#ffffff";
                sb.Append("<tr style=\"border-top:1px solid #e5e7eb\">")
                  .Append($"<td>{Encode(r.Subject)}</td><td>{Pct(r.TermMark)}</td><td>{Pct(r.YearMark)}</td><td>{Pct(r.Attendance)}</td>")
                  .Append($"<td><span style=\"background:{RiskColour(r.RiskLevel)};color:{riskText};padding:2px 8px;border-radius:10px;font-size:12px\">{Encode(r.RiskLevel)}</span></td></tr>");
            }
            sb.Append("</table>");

            var topicsHtml = concernTopics.Count == 0 ? "" :
                "<p style=\"margin:0 0 6px\">Topics flagged for extra practice:</p><ul style=\"margin:0 0 14px;padding-left:20px\">" +
                string.Concat(concernTopics.Select(t => $"<li>{Encode(t)}</li>")) + "</ul>";

            var html = Layout($"Term {term} progress report: {childName}", schoolName,
                $"<p style=\"margin:0 0 14px\">Dear {Encode(parentName)},</p>" +
                $"<p style=\"margin:0 0 14px\">Here is {Encode(childName)}'s (Grade {grade}) progress summary for Term {term}.</p>" +
                sb + topicsHtml + Button(marksUrl, "View full marks in ARIS"),
                notificationFooter: true);

            var text = new StringBuilder($"Dear {parentName},\n\nTerm {term} progress report for {childName} (Grade {grade}):\n\n");
            foreach (var r in rows)
                text.AppendLine($"- {r.Subject}: Term {Pct(r.TermMark)}, Year {Pct(r.YearMark)}, Attendance {Pct(r.Attendance)}, Risk {r.RiskLevel}");
            if (concernTopics.Count > 0) text.AppendLine($"\nTopics flagged for extra practice: {string.Join(", ", concernTopics)}");
            text.AppendLine($"\nView full marks: {marksUrl}");

            return new RenderedEmail($"Term {term} progress report: {childName}", html, text.ToString());
        }
    }
}
