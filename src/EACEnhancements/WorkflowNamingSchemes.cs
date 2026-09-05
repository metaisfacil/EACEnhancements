using System;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AudioDataPlugIn
{
    // EAC shares all four naming conventions between ordinary extraction and
    // the enhanced workflow. Only borrow their filename portions during the
    // workflow; restore the original values, including missing registry values.
    internal sealed class WorkflowNamingSchemes
    {
        private static readonly string[] Names = {
            "FileNamingConvention", "FileNamingConvention2nd",
            "VariousFileNamingConvention", "VariousFileNamingConvention2nd"
        };
        private static readonly string[] Defaults = {
            "%tracknr2% - %title%", "%tracknr2% - %title%",
            "%artist% - %title%", "%artist% - %title%"
        };
        private readonly string[] values = new string[Names.Length];

        internal static WorkflowNamingSchemes Capture(RegistryKey key)
        {
            WorkflowNamingSchemes snapshot = new WorkflowNamingSchemes();
            for (int i = 0; i < Names.Length; i++)
                snapshot.values[i] = key.GetValue(Names[i]) as string;
            return snapshot;
        }

        internal void ApplyTrackOnly(RegistryKey key)
        {
            for (int i = 0; i < Names.Length; i++)
                key.SetValue(Names[i], EnhancementRuntime.NamingSchemeTail(
                    values[i], Defaults[i]), RegistryValueKind.String);
        }

        internal void Restore(RegistryKey key)
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (values[i] == null)
                    key.DeleteValue(Names[i], false);
                else
                    key.SetValue(Names[i], values[i], RegistryValueKind.String);
            }
        }

        internal static bool RemoveLegacyFolderPrefixes(RegistryKey key, string template)
        {
            bool changed = false;
            for (int i = 0; i < Names.Length; i++)
            {
                string current = key.GetValue(Names[i]) as string;
                string updated = RemoveLegacyFolderPrefix(current, template);
                if (String.Equals(current, updated, StringComparison.Ordinal))
                    continue;
                key.SetValue(Names[i], updated, RegistryValueKind.String);
                changed = true;
            }
            return changed;
        }

        internal static string RemoveLegacyFolderPrefix(string scheme, string template)
        {
            if (String.IsNullOrEmpty(scheme) || String.IsNullOrWhiteSpace(template))
                return scheme;
            string normalized = scheme.Replace('/', '\\');
            int separator = normalized.LastIndexOf('\\');
            if (separator < 0 || separator == normalized.Length - 1)
                return scheme;

            // Older releases stored percent tokens verbatim, with conditional
            // delimiters either present or omitted. Match the complete parent
            // against that template, never an arbitrary user-created directory.
            string source = template.Replace('/', '\\').Trim('\\');
            StringBuilder pattern = new StringBuilder("\\A");
            int offset = 0;
            foreach (Match match in Regex.Matches(source,
                @"\(\(\((?<paren>.*?)\)\)\)|\{\{\{(?<curly>.*?)\}\}\}"))
            {
                pattern.Append(LiteralPattern(source.Substring(offset, match.Index - offset)));
                bool parentheses = match.Groups["paren"].Success;
                string content = match.Groups[parentheses ? "paren" : "curly"].Value;
                pattern.Append("(?:[ \\t]*");
                pattern.Append(Regex.Escape(parentheses ? "(" : "{"));
                pattern.Append(LiteralPattern(content));
                pattern.Append(Regex.Escape(parentheses ? ")" : "}"));
                pattern.Append("[ \\t]*)?");
                offset = match.Index + match.Length;
            }
            pattern.Append(LiteralPattern(source.Substring(offset)));
            pattern.Append("\\z");
            return Regex.IsMatch(normalized.Substring(0, separator), pattern.ToString())
                ? scheme.Substring(separator + 1)
                : scheme;
        }

        private static string LiteralPattern(string value)
        {
            return Regex.Replace(Regex.Escape(value), @"(?:\\ |\\t)+", "[ \\t]*");
        }
    }
}
