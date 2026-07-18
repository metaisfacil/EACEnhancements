using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace AudioDataPlugIn
{
    internal sealed class GitHubReleaseInfo
    {
        internal string TagName { get; private set; }

        internal string VersionText { get; private set; }

        internal Version Version { get; private set; }

        internal string PageUrl { get; private set; }

        internal GitHubReleaseInfo(string tagName, string versionText, Version version)
        {
            TagName = tagName;
            VersionText = versionText;
            Version = version;
            PageUrl = UpdateChecker.CreateReleasePageUrl(tagName);
        }
    }

    internal static class UpdateChecker
    {
        private const string ReleasesApiUrl =
            "https://api.github.com/repos/metaisfacil/EACEnhancements/releases?per_page=100";
        private const string ReleasePageBaseUrl =
            "https://github.com/metaisfacil/EACEnhancements/releases/tag/";

        internal static GitHubReleaseInfo GetLatestRelease()
        {
            // GitHub requires TLS 1.2. The numeric value preserves compatibility
            // with the .NET 4 reference assemblies used by the plugin build.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ReleasesApiUrl);
            request.Accept = "application/vnd.github+json";
            request.UserAgent = "EACEnhancements/" +
                FormatVersion(typeof(AudioDataTransfer).Assembly.GetName().Version);
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;

            string json;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                json = reader.ReadToEnd();

            object[] releases = new JavaScriptSerializer().DeserializeObject(json) as object[];
            if (releases == null)
                throw new InvalidDataException("GitHub returned an unexpected releases response.");

            GitHubReleaseInfo latest = null;
            foreach (object item in releases)
            {
                Dictionary<string, object> release = item as Dictionary<string, object>;
                if (release == null || GetBoolean(release, "draft"))
                    continue;

                object tagValue;
                string tagName = release.TryGetValue("tag_name", out tagValue)
                    ? tagValue as string
                    : null;
                Version version;
                string versionText;
                if (!TryParseReleaseTag(tagName, out version, out versionText))
                    continue;

                if (latest == null || IsNewer(version, latest.Version))
                    latest = new GitHubReleaseInfo(tagName, versionText, version);
            }

            if (latest == null)
                throw new InvalidDataException("GitHub did not return a versioned release.");
            return latest;
        }

        internal static bool TryParseReleaseTag(
            string tagName,
            out Version version,
            out string versionText)
        {
            version = null;
            versionText = null;
            if (String.IsNullOrWhiteSpace(tagName))
                return false;

            string candidate = tagName.Trim();
            if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                candidate = candidate.Substring(1);
            int suffix = candidate.IndexOfAny(new[] { '-', '+' });
            if (suffix >= 0)
                candidate = candidate.Substring(0, suffix);

            Version parsed;
            if (!Version.TryParse(candidate, out parsed))
                return false;
            version = NormalizeVersion(parsed);
            versionText = FormatVersion(version);
            return true;
        }

        internal static bool IsNewer(Version available, Version installed)
        {
            if (available == null)
                throw new ArgumentNullException("available");
            if (installed == null)
                throw new ArgumentNullException("installed");
            return NormalizeVersion(available).CompareTo(NormalizeVersion(installed)) > 0;
        }

        internal static string CreateReleasePageUrl(string tagName)
        {
            if (String.IsNullOrWhiteSpace(tagName))
                throw new ArgumentException("A release tag is required.", "tagName");
            return ReleasePageBaseUrl + Uri.EscapeDataString(tagName.Trim());
        }

        internal static string FormatVersion(Version version)
        {
            Version normalized = NormalizeVersion(version);
            string text = normalized.Major + "." + normalized.Minor + "." + normalized.Build;
            if (normalized.Revision > 0)
                text += "." + normalized.Revision;
            return text;
        }

        private static Version NormalizeVersion(Version version)
        {
            if (version == null)
                throw new ArgumentNullException("version");
            return new Version(
                version.Major,
                version.Minor,
                Math.Max(0, version.Build),
                Math.Max(0, version.Revision));
        }

        private static bool GetBoolean(Dictionary<string, object> values, string name)
        {
            object value;
            return values.TryGetValue(name, out value) && value is bool && (bool)value;
        }
    }
}
