using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AudioDataPlugIn
{
    internal static class WorkflowFolderPath
    {
        private static readonly Regex Token = new Regex(
            "%([a-zA-Z0-9_]+)%",
            RegexOptions.Compiled);

        internal static string Resolve(
            string template,
            IDictionary<string, string> metadata)
        {
            string percentTemplate = EnhancementRuntime.ConvertBraceTokens(
                EnhancementRuntime.NormalizeFolderTemplate(template));
            string conditionalTemplate = FolderTemplateFormatter.ResolveConditionalCurlyBraces(
                FolderTemplateFormatter.ResolveConditionalParentheses(
                    percentTemplate,
                    HasValue(metadata, "year")),
                HasValue(metadata, "comment"));
            // Split only separators authored in the template. Metadata values
            // are expanded afterward so a slash in an album title is sanitized
            // as part of that folder name rather than becoming another level.
            string[] components = conditionalTemplate.Replace('/', '\\').Split('\\');
            List<string> clean = new List<string>();
            foreach (string component in components)
            {
                string expanded = Token.Replace(component, delegate(Match match)
                {
                    string value;
                    if (!metadata.TryGetValue(match.Groups[1].Value, out value))
                        throw new ArgumentException(
                            "The folder template token " + match.Value +
                            " cannot be resolved before extraction begins.");
                    return value ?? String.Empty;
                });
                string sanitized = SanitizeComponent(expanded);
                if (sanitized.Length == 0)
                    throw new ArgumentException("The folder template produced an empty folder name.");
                clean.Add(sanitized);
            }
            return String.Join("\\", clean.ToArray());
        }

        internal static string ResolveDestination(
            string rootFolder,
            string template,
            IDictionary<string, string> metadata,
            bool createWorkflowFolder)
        {
            string root = Path.GetFullPath(rootFolder);
            if (!createWorkflowFolder)
                return String.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
                    ? root
                    : root.TrimEnd('\\');

            string destination = Path.GetFullPath(Path.Combine(root, Resolve(template, metadata)));
            string rootedPrefix = root.EndsWith("\\", StringComparison.Ordinal)
                ? root
                : root + "\\";
            if (!destination.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The generated rip folder is outside the configured root folder.");
            return destination;
        }

        private static bool HasValue(IDictionary<string, string> metadata, string key)
        {
            string value;
            return metadata.TryGetValue(key, out value) && !String.IsNullOrWhiteSpace(value);
        }

        private static string SanitizeComponent(string value)
        {
            HashSet<char> invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            StringBuilder result = new StringBuilder((value ?? String.Empty).Trim());
            for (int index = 0; index < result.Length; index++)
            {
                if (invalid.Contains(result[index]))
                    result[index] = '_';
            }
            return result.ToString().TrimEnd(' ', '.');
        }
    }
}
