using System;
using System.Collections.Generic;

namespace TheGrid.Core
{
    // Which of the Mac's fonts the app's text is set in.
    public static class Fonts
    {
        // The family to use: the one named in the settings when the Mac has it, spelled as the Mac spells it; else the
        // first family, by name, that says it is a Nerd Font, in full or by the short "NF", which puts a family before its
        // own bold and italic cuts. "" when there is none: the text then stays as it is without this.
        public static string Pick(string named, IEnumerable<string> installed)
        {
            string wanted = (named ?? "").Trim(), nerd = "";
            foreach (var name in installed ?? new string[0])
            {
                if (wanted != "" && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)) return name;
                bool isNerd = name.IndexOf("Nerd Font", StringComparison.OrdinalIgnoreCase) >= 0 || Array.IndexOf(name.Split(' '), "NF") >= 0;
                if (isNerd && (nerd == "" || string.CompareOrdinal(name, nerd) < 0)) nerd = name;
            }
            return nerd;
        }
    }
}
