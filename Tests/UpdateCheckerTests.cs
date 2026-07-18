using System;

namespace AudioDataPlugIn
{
    internal static class UpdateCheckerTests
    {
        private static int Main()
        {
            try
            {
                AssertTag("v0.14.0", new Version(0, 14, 0, 0), "0.14.0");
                AssertTag("0.13.2", new Version(0, 13, 2, 0), "0.13.2");
                AssertTag("v1.2.3.4-beta", new Version(1, 2, 3, 4), "1.2.3.4");

                Version installed = new Version(0, 13, 2, 0);
                if (!UpdateChecker.IsNewer(new Version(0, 14, 0), installed))
                    throw new Exception("A newer release was not detected.");
                if (UpdateChecker.IsNewer(new Version(0, 13, 2), installed) ||
                    UpdateChecker.IsNewer(new Version(0, 13, 1), installed))
                {
                    throw new Exception("An equal or older release was reported as newer.");
                }

                string page = UpdateChecker.CreateReleasePageUrl("v0.14.0");
                if (page != "https://github.com/metaisfacil/EACEnhancements/releases/tag/v0.14.0")
                    throw new Exception("The release-specific browser URL is incorrect.");

                if (Environment.GetEnvironmentVariable("EACENHANCEMENTS_LIVE_UPDATE_TEST") == "1")
                {
                    GitHubReleaseInfo release = UpdateChecker.GetLatestRelease();
                    if (release == null || String.IsNullOrEmpty(release.TagName) ||
                        !release.PageUrl.EndsWith("/" + Uri.EscapeDataString(release.TagName)))
                    {
                        throw new Exception("The live GitHub release response was invalid.");
                    }
                    Console.WriteLine(
                        "Live GitHub release: " + release.VersionText + " (" + release.TagName + ")");
                }

                Console.WriteLine("Update checker tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void AssertTag(string tag, Version expected, string expectedText)
        {
            Version actual;
            string actualText;
            if (!UpdateChecker.TryParseReleaseTag(tag, out actual, out actualText) ||
                actual != expected || actualText != expectedText)
            {
                throw new Exception("Release tag parsing failed for " + tag + ".");
            }
        }
    }
}
