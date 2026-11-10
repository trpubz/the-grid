using System.Text.RegularExpressions;

namespace TheGrid.Core
{
    public static class Ticket
    {
        // shortcut: any letters-digits token counts as a ticket (e.g. "utf-8"); add an allowed-prefix setting if false matches show up.
        static readonly Regex Pattern = new Regex(@"(?<![A-Za-z0-9])([A-Za-z][A-Za-z0-9]*-\d+)", RegexOptions.Compiled);

        public static string FromBranch(string branch)
        {
            if (string.IsNullOrEmpty(branch)) return "";
            var m = Pattern.Match(branch);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
        }
    }
}
