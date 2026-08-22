using NUnit.Framework;

namespace ChangeSharp.Tests;

public class PublicApiBaselineTests
{
    private const string BaselineRelativePath = "tests/public-api/public-api.txt";
    private const string UpdateEnvVar = "CHANGESHARP_UPDATE_API_BASELINE";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ChangeSharp.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root (ChangeSharp.sln not found).");
    }

    private static string GeneratePublicApi()
    {
        string api = PublicApiGenerator.ApiGenerator.GeneratePublicApi(typeof(WorkspaceManager).Assembly);
        return api.Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    [Test]
    public void LibraryPublicApi_MatchesCommittedBaseline()
    {
        string api = GeneratePublicApi();
        string baselinePath = Path.Combine(FindRepoRoot(), BaselineRelativePath);

        if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
            File.WriteAllText(baselinePath, api);
            return;
        }

        if (!File.Exists(baselinePath))
        {
            Assert.Fail(
                $"Public API baseline missing at '{baselinePath}'. " +
                "Regenerate it with CHANGESHARP_UPDATE_API_BASELINE=1 (or scripts/update-public-api.sh) and commit the result.");
        }

        string baseline = File.ReadAllText(baselinePath).Replace("\r\n", "\n");

        if (api != baseline)
        {
            string? diff = null;
            try
            {
                diff = Diff(api, baseline);
            }
            catch
            {
                // best-effort diff display
            }
            Assert.Fail(
                "The library public API changed but the committed baseline is not up to date.\n" +
                "If the change is intentional, regenerate the baseline with CHANGESHARP_UPDATE_API_BASELINE=1 " +
                "(or scripts/update-public-api.sh) and commit it.\n" +
                (diff != null ? $"Diff:\n{diff}" : ""));
        }
    }

    private static string Diff(string current, string baseline)
    {
        string currentPath = Path.GetTempFileName();
        string baselinePath = Path.GetTempFileName();
        File.WriteAllText(currentPath, current);
        File.WriteAllText(baselinePath, baseline);
        var psi = new System.Diagnostics.ProcessStartInfo("git", $"diff --no-index --unified=3 {baselinePath} {currentPath}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        File.Delete(currentPath);
        File.Delete(baselinePath);
        return output;
    }
}