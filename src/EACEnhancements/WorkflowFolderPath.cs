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
            return Resolve(template, metadata, null);
        }

        internal static string Resolve(
            string template,
            IDictionary<string, string> metadata,
            IDictionary<char, string> characterReplacements)
        {
            string percentTemplate = EnhancementRuntime.ConvertBraceTokens(
                EnhancementRuntime.NormalizeFolderTemplate(template));
            Func<string, bool> includeConditional = delegate(string content)
            {
                return ConditionalTokensHaveValues(content, metadata);
            };
            string conditionalTemplate = FolderTemplateFormatter.ResolveConditionalCurlyBraces(
                FolderTemplateFormatter.ResolveConditionalParentheses(
                    percentTemplate,
                    includeConditional),
                includeConditional);
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
                    return ApplyCharacterReplacements(
                        value ?? String.Empty,
                        characterReplacements);
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
            return ResolveDestination(
                rootFolder,
                template,
                metadata,
                createWorkflowFolder,
                null);
        }

        internal static string ResolveDestination(
            string rootFolder,
            string template,
            IDictionary<string, string> metadata,
            bool createWorkflowFolder,
            IDictionary<char, string> characterReplacements)
        {
            string root = Path.GetFullPath(rootFolder);
            if (!createWorkflowFolder)
                return String.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase)
                    ? root
                    : root.TrimEnd('\\');

            string destination = Path.GetFullPath(Path.Combine(
                root,
                Resolve(template, metadata, characterReplacements)));
            string rootedPrefix = root.EndsWith("\\", StringComparison.Ordinal)
                ? root
                : root + "\\";
            if (!destination.StartsWith(rootedPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The generated rip folder is outside the configured root folder.");
            return destination;
        }

        internal static string ResolveAbsoluteDestinationTemplate(
            string destinationTemplate,
            IDictionary<string, string> metadata,
            IDictionary<char, string> characterReplacements)
        {
            if (!CommandLineInvocation.IsFullyQualifiedDestination(
                    destinationTemplate))
            {
                throw new ArgumentException(
                    "The command-line destination must be fully qualified.",
                    "destinationTemplate");
            }

            string fullTemplate = Path.GetFullPath(destinationTemplate);
            string root = Path.GetPathRoot(fullTemplate);
            string relativeTemplate = fullTemplate.Substring(root.Length);
            if (String.IsNullOrWhiteSpace(
                    relativeTemplate.Trim('\\', '/')))
            {
                throw new ArgumentException(
                    "The command-line destination must name an album folder, not a filesystem root.",
                    "destinationTemplate");
            }
            string relative = Resolve(
                relativeTemplate,
                metadata,
                characterReplacements);
            string destination = Path.GetFullPath(
                Path.Combine(root, relative));
            string rootedPrefix = root.EndsWith("\\", StringComparison.Ordinal)
                ? root
                : root + "\\";
            if (!destination.StartsWith(
                    rootedPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The generated command-line destination is outside its absolute root.");
            }
            return destination.TrimEnd('\\');
        }

        private static bool HasValue(IDictionary<string, string> metadata, string key)
        {
            string value;
            return metadata.TryGetValue(key, out value) && !String.IsNullOrWhiteSpace(value);
        }

        internal static bool ConditionalTokensHaveValues(
            string content,
            IDictionary<string, string> metadata)
        {
            MatchCollection matches = Token.Matches(content ?? String.Empty);
            foreach (Match match in matches)
            {
                if (!HasValue(metadata, match.Groups[1].Value))
                    return false;
            }
            return true;
        }

        internal static string ResolveLiteralFilename(
            string value,
            IDictionary<char, string> characterReplacements)
        {
            string resolved = SanitizeComponent(
                ApplyCharacterReplacements(
                    value,
                    characterReplacements));
            if (String.IsNullOrWhiteSpace(resolved))
                throw new InvalidOperationException(
                    "The HTOA filename is empty after applying EAC's Character Replacements.");
            return resolved;
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

        private static string ApplyCharacterReplacements(
            string value,
            IDictionary<char, string> characterReplacements)
        {
            if (String.IsNullOrEmpty(value) ||
                characterReplacements == null ||
                characterReplacements.Count == 0)
                return value ?? String.Empty;

            StringBuilder result = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                string replacement;
                if (characterReplacements.TryGetValue(character, out replacement))
                    result.Append(replacement);
                else
                    result.Append(character);
            }
            return result.ToString();
        }
    }
}
