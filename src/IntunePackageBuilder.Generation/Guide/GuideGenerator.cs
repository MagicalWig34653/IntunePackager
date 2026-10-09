using System;
using System.Text;
using IntunePackageBuilder.Core.Storage;
using IntunePackageBuilder.Core.Versions;
using IntunePackageBuilder.Generation.Intune;

namespace IntunePackageBuilder.Generation.Guide
{
    /// <summary>
    /// Renders the Intune setup guide of one build as a standalone HTML page (SPEC section 9): readable offline,
    /// printable, no script and no external resources. All values come from <see cref="IntuneSettings"/>, so the
    /// guide agrees with the JSON and CSV files. It describes settings only and never claims that a package was
    /// uploaded, assigned or installed.
    /// </summary>
    public static class GuideGenerator
    {
        private const string PolicyMeta = "default-src 'none'; style-src 'unsafe-inline'";

        public static string Generate(IntuneSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException("settings");
            }

            var language = GuideText.CultureFor(settings.Language).Name == GuideText.German ? GuideText.German : GuideText.English;
            var page = new Page(language);
            page.Open(settings);
            page.MixWarning(settings);
            AppType(page);
            Package(page, settings);
            Info(page, settings);
            Program(page, settings);
            Requirements(page, settings);
            ReturnCodes(page, settings);
            Detection(page, settings);
            Assignment(page);
            Dependencies(page);
            Troubleshooting(page, settings);
            page.Close(settings);
            return page.ToString();
        }

        public static void WriteTo(string path, string html)
        {
            AtomicFile.WriteAllText(path, html, new UTF8Encoding(false));
        }

        private static void AppType(Page page)
        {
            page.Section("SecAppType");
            page.Table(page.Row("FldAppType", page.T("ValAppType")));
        }

        private static void Package(Page page, IntuneSettings s)
        {
            page.Section("SecPackage");
            page.Table(page.Row("FldPackageFile", s.App.PackageFileName, true));
        }

        private static void Info(Page page, IntuneSettings s)
        {
            page.Section("SecInfo");
            page.Table(
                page.Row("FldName", s.App.Name),
                page.Row("FldPublisher", s.App.Publisher),
                page.Row("FldVersion", s.App.Version),
                page.Row("FldDescription", s.App.Description));
        }

        private static void Program(Page page, IntuneSettings s)
        {
            page.Section("SecProgram");
            page.Table(
                page.Row("FldInstallCmd", s.Program.InstallCommand, true),
                page.Row("FldUninstallCmd", s.Program.UninstallCommand, true),
                page.Row("FldInstallBehavior", page.T("ValSystem")),
                page.Row("FldTimeout", s.Program.TimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                page.Row("FldRestart", page.T("ValRestartByCodes")));
            page.Note("NoteRestart");
            if (s.ProcessesToClose.Count > 0)
            {
                page.Raw("<p><strong>" + page.TE("FldProcessesToClose") + "</strong></p>");
                page.Raw("<ul>");
                foreach (var name in s.ProcessesToClose)
                {
                    page.Raw("<li><code>" + HtmlText.Escape(name) + "</code></li>");
                }

                page.Raw("</ul>");
                page.Note("NoteProcessesToClose");
            }
        }

        private static void Requirements(Page page, IntuneSettings s)
        {
            page.Section("SecRequirements");
            page.Table(
                page.Row("FldArch", page.T("ValArch64")),
                page.Row("FldMinOs", s.Requirements.MinimumWindowsVersion));
            if (s.Requirements.MinimumWindowsVersionIsPlaceholder)
            {
                page.Warning("NotePlaceholder");
            }
        }

        private static void ReturnCodes(Page page, IntuneSettings s)
        {
            page.Section("SecReturnCodes");
            var rows = new StringBuilder();
            rows.Append("<thead><tr><th>").Append(page.TE("ColCode")).Append("</th><th>").Append(page.TE("ColType")).Append("</th></tr></thead><tbody>");
            foreach (var entry in s.ReturnCodes)
            {
                rows.Append("<tr><td><code>")
                    .Append(entry.Code.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append("</code></td><td>")
                    .Append(HtmlText.Escape(page.T(TypeKey(entry.Type))))
                    .Append("</td></tr>");
            }

            rows.Append("</tbody>");
            page.Raw("<table>" + rows + "</table>");
            page.Note("NoteReturnCodes");
            page.Note("NoteReturn1641");
        }

        private static void Detection(Page page, IntuneSettings s)
        {
            var d = s.Detection;
            page.Section("SecDetection");
            page.Table(
                page.Row("FldRuleFormat", page.T("ValRuleFormatScript")),
                page.Row("FldScriptFile", d.ScriptFileName, true),
                page.Row("FldRun32", page.T(d.RunAs32Bit ? "ValYes" : "ValNo")),
                page.Row("FldSignatureCheck", page.T(d.EnforceSignatureCheck ? "ValYes" : "ValNo")),
                page.Row("FldSignatureStatus", page.T(string.Equals(d.SignatureStatus, "Signed", StringComparison.Ordinal) ? "ValSigned" : "ValUnsigned")));
            if (!string.Equals(d.SignatureStatus, "Signed", StringComparison.Ordinal))
            {
                page.Note("NoteSignature");
            }

            var rule = d.Rule;
            var description = rule.Method == DetectionMethod.MsiProductCode
                ? page.F("RuleMsi", rule.ProductCode, rule.MinimumVersion)
                : page.F("RuleFile", rule.Path, rule.MinimumVersion);
            page.Raw("<p><strong>" + page.TE("FldRule") + "</strong></p>");
            page.Raw("<p>" + HtmlText.Escape(description) + "</p>");
            page.Note("NoteContract");
        }

        private static void Assignment(Page page)
        {
            page.Section("SecAssignment");
            page.Note("AssignPilot");
            page.Raw("<ul><li>" + page.TE("AssignRequired") + "</li><li>" + page.TE("AssignAvailable") + "</li></ul>");
        }

        private static void Dependencies(Page page)
        {
            page.Section("SecDependencies");
            page.Note("DepNone");
        }

        private static void Troubleshooting(Page page, IntuneSettings s)
        {
            page.Section("SecTroubleshooting");
            var l = s.Logs;
            var rows = new System.Collections.Generic.List<string>
            {
                page.Row("FldLogDir", l.Directory, true),
                page.Row("FldDeploymentLog", l.DeploymentLog, true)
            };
            if (!string.IsNullOrEmpty(l.MsiInstallLog))
            {
                rows.Add(page.Row("FldMsiInstallLog", l.MsiInstallLog, true));
            }

            if (!string.IsNullOrEmpty(l.MsiUninstallLog))
            {
                rows.Add(page.Row("FldMsiUninstallLog", l.MsiUninstallLog, true));
            }

            rows.Add(page.Row("FldImeLogs", l.IntuneManagementExtensionLogs, true));
            page.Table(rows.ToArray());
            page.Raw("<ol><li>" + page.TE("TroubleStep1") + "</li>"
                + (string.IsNullOrEmpty(l.MsiInstallLog) ? string.Empty : "<li>" + page.TE("TroubleStep2") + "</li>")
                + "<li>" + page.TE("TroubleStep3") + "</li><li>" + page.TE("TroubleStep4") + "</li></ol>");
            page.Note("NoteVendorLogs");
        }

        private static string TypeKey(ReturnCodeType type)
        {
            switch (type)
            {
                case ReturnCodeType.Success:
                    return "TypeSuccess";
                case ReturnCodeType.SoftReboot:
                    return "TypeSoftReboot";
                case ReturnCodeType.HardReboot:
                    return "TypeHardReboot";
                default:
                    return "TypeRetry";
            }
        }

        private sealed class Page
        {
            private readonly StringBuilder _html = new StringBuilder();
            private readonly string _language;

            public Page(string language)
            {
                _language = language;
            }

            public string T(string key)
            {
                return GuideText.Get(_language, key);
            }

            /// <summary>Text of a resource, HTML-escaped.</summary>
            public string TE(string key)
            {
                return HtmlText.Escape(T(key));
            }

            public string F(string key, params object[] args)
            {
                return GuideText.Format(_language, key, args);
            }

            public void Raw(string html)
            {
                _html.Append(html).Append("\r\n");
            }

            public void Open(IntuneSettings s)
            {
                Raw("<!DOCTYPE html>");
                Raw("<html lang=\"" + _language + "\">");
                Raw("<head>");
                Raw("<meta charset=\"utf-8\">");
                Raw("<meta http-equiv=\"Content-Security-Policy\" content=\"" + HtmlText.Escape(PolicyMeta) + "\">");
                Raw("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
                Raw("<title>" + HtmlText.Escape(F("PageTitle", s.App.Name)) + "</title>");
                Raw("<style>" + Style + "</style>");
                Raw("</head>");
                Raw("<body>");
                Raw("<h1>" + TE("Heading") + "</h1>");
                Raw("<p>" + HtmlText.Escape(F("Intro", s.BuildId)) + "</p>");
                Table(
                    Row("LabelSoftware", s.App.Name),
                    Row("LabelTargetVersion", s.App.Version),
                    Row("LabelBuild", s.BuildId, true));
            }

            public void MixWarning(IntuneSettings s)
            {
                Raw("<div class=\"warning\"><strong>" + TE("MixWarningTitle") + "</strong><p>"
                    + HtmlText.Escape(F("MixWarningText", s.App.PackageFileName, s.Detection.ScriptFileName, s.BuildId)) + "</p></div>");
            }

            public void Close(IntuneSettings s)
            {
                Raw("<footer>" + HtmlText.Escape(F("Footer", s.BuildId)) + "</footer>");
                Raw("</body>");
                Raw("</html>");
            }

            public void Section(string key)
            {
                Raw("<h2>" + TE(key) + "</h2>");
            }

            public void Note(string key)
            {
                Raw("<p>" + TE(key) + "</p>");
            }

            public void Warning(string key)
            {
                Raw("<div class=\"warning\">" + TE(key) + "</div>");
            }

            public string Row(string labelKey, string value)
            {
                return Row(labelKey, value, false);
            }

            public string Row(string labelKey, string value, bool code)
            {
                var text = HtmlText.Escape(value);
                return "<tr><th scope=\"row\">" + TE(labelKey) + "</th><td>" + (code ? "<code>" + text + "</code>" : text) + "</td></tr>";
            }

            public void Table(params string[] rows)
            {
                Raw("<table><thead><tr><th>" + TE("ColSetting") + "</th><th>" + TE("ColValue") + "</th></tr></thead><tbody>"
                    + string.Concat(rows) + "</tbody></table>");
            }

            public override string ToString()
            {
                return _html.ToString();
            }

            private const string Style =
                "body{font-family:Segoe UI,Arial,sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem;color:#1b1b1b;line-height:1.5}"
                + "h1{font-size:1.6rem}h2{font-size:1.2rem;margin-top:2rem;border-bottom:1px solid #ccc}"
                + "table{border-collapse:collapse;width:100%;margin:.5rem 0}th,td{border:1px solid #ccc;padding:.4rem .6rem;text-align:left;vertical-align:top}"
                + "thead th{background:#f0f0f0}tbody th{width:40%;font-weight:600;background:#fafafa}"
                + "code{font-family:Consolas,monospace;word-break:break-all}"
                + ".warning{border:2px solid #b36b00;background:#fff4e0;padding:.5rem 1rem;margin:1rem 0}"
                + "footer{margin-top:2rem;font-size:.85rem;color:#555}"
                + "@media print{body{margin:0;max-width:none}h2{page-break-after:avoid}table{page-break-inside:avoid}}";
        }
    }
}
