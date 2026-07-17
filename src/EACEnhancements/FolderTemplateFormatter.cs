using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static class FolderTemplateFormatter
    {
        private static readonly Regex ConditionalParentheses = new Regex(
            "(?<before>[ \\t]*)\\(\\(\\((?<content>.*?)\\)\\)\\)(?<after>[ \\t]*)",
            RegexOptions.Compiled);

        private static readonly Regex ConditionalCurlyBraces = new Regex(
            "(?<before>[ \\t]*)\\{\\{\\{(?<content>.*?)\\}\\}\\}(?<after>[ \\t]*)",
            RegexOptions.Compiled);

        internal static string ResolveConditionalParentheses(string template, bool include)
        {
            return ConditionalParentheses.Replace(template ?? string.Empty, delegate(Match match)
            {
                string before = match.Groups["before"].Value;
                string after = match.Groups["after"].Value;
                if (include)
                    return before + "(" + match.Groups["content"].Value + ")" + after;

                // Keep a single separator between the components that were on
                // either side of the omitted conditional value.
                return before.Length > 0 && after.Length > 0 ? " " : string.Empty;
            });
        }

        internal static bool HasConditionalParentheses(string template)
        {
            return ConditionalParentheses.IsMatch(template ?? string.Empty);
        }

        internal static string ResolveConditionalCurlyBraces(string template, bool include)
        {
            string source = template ?? string.Empty;
            return ConditionalCurlyBraces.Replace(source, delegate(Match match)
            {
                string before = match.Groups["before"].Value;
                string after = match.Groups["after"].Value;
                if (!include)
                    return before.Length > 0 && after.Length > 0 ? " " : string.Empty;

                if (before.Length == 0 && HasAdjacentCharacterBefore(source, match.Index))
                    before = " ";
                if (after.Length == 0 && HasAdjacentCharacterAfter(source, match.Index + match.Length))
                    after = " ";
                return before + "{" + match.Groups["content"].Value + "}" + after;
            });
        }

        internal static bool HasConditionalCurlyBraces(string template)
        {
            return ConditionalCurlyBraces.IsMatch(template ?? string.Empty);
        }

        private static bool HasAdjacentCharacterBefore(string source, int index)
        {
            if (index <= 0)
                return false;
            char value = source[index - 1];
            return !char.IsWhiteSpace(value) && value != '\\' && value != '/';
        }

        private static bool HasAdjacentCharacterAfter(string source, int index)
        {
            if (index >= source.Length)
                return false;
            char value = source[index];
            return !char.IsWhiteSpace(value) && value != '\\' && value != '/';
        }
    }
}
