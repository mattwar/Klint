
namespace Klint;

public static class FilePatterns
{
    private static bool IsPattern(string path)
    {
        return path.Contains("*") || path.Contains("?");
    }

    public static IEnumerable<string> GetFilePaths(IEnumerable<string> patterns)
    {
        return patterns.SelectMany(pat => GetFilePaths(pat)).Distinct();
    }

    public static IEnumerable<string> GetFilePaths(string pattern)
    {
        if (!IsPattern(pattern))
        {
            yield return pattern;
        }
        else
        {
            var directoryPath = Path.GetDirectoryName(pattern);
            var filePattern = Path.GetFileName(pattern);

            if (!string.IsNullOrEmpty(directoryPath))
            {
                foreach (var dp in GetDirectoryPaths(directoryPath))
                {
                    foreach (var fileName in Directory.GetFiles(dp, filePattern))
                    {
                        yield return NormalizePath(fileName);
                    }
                }
            }
        }
    }

    public static IEnumerable<string> GetDirectoryPaths(string pattern)
    {
        if (!IsPattern(pattern))
        {
            yield return pattern;
        }
        else
        {
            var directoryPath = Path.GetDirectoryName(pattern);
            var directoryPattern = Path.GetFileName(pattern);

            if (!string.IsNullOrEmpty(directoryPath))
            {
                foreach (var dp in GetDirectoryPaths(directoryPath))
                {
                    if (directoryPattern == "**")
                    {
                        foreach (var d in Directory.GetDirectories(dp, "*", SearchOption.AllDirectories))
                        {
                            yield return NormalizePath(d);
                        }
                    }
                    else
                    {
                        foreach (var d in Directory.GetDirectories(dp, directoryPath))
                        {
                            yield return NormalizePath(d);
                        }
                    }
                }
            }
        }
    }

    private static string NormalizePath(string path)
    {
        if (path.Contains("\\"))
        {
            return path.Replace("\\", "/");
        }

        return path;
    }
}