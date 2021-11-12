using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Klint;
using Kusto.Language;

namespace Tests;

[TestClass]
public class RunnerTests
{
    private static readonly string HelpCluster = "help.kusto.windows.net";
    private static readonly string HelpConnection = "https://help.kusto.windows.net;Fed=true";
    private static readonly string TestCache = "Schemas";

    [TestMethod]
    public async Task TestHelp()
    {
        await TestRunnerAsync("-?", OptionsParser.HelpText);
    }

    [TestMethod]
    public async Task TestUnknowndOption()
    {
        await TestRunnerAsync("-whatever", "unknown option: -whatever");
    }

    [TestMethod]
    public async Task TestCache_Generate()
    {
        var testCachePath = GetTestCachePath();

        Assert.IsFalse(Directory.Exists(testCachePath), "cache directory already exists?");

        try
        {
            await TestRunnerAsync($"-connection {HelpConnection} -cache {testCachePath} -generate", $"schema cache generated");

            Assert.IsTrue(Directory.Exists(testCachePath), "Cache directory not created");

            var clusterCache = Path.Combine(testCachePath, HelpCluster);
            Assert.IsTrue(Directory.Exists(clusterCache), "Cluster cache directory not created.");

            var samplesFile = Path.Combine(clusterCache, "Samples.json");
            Assert.IsTrue(File.Exists(samplesFile), "Samples.json file not created");
        }
        finally
        {
            if (Directory.Exists(testCachePath))
            {
                Directory.Delete(testCachePath, true);
            }
        }
    }

    [TestMethod]
    public async Task TestCache_Delete()
    {
        var testCachePath = GetTestCachePath();

        Assert.IsFalse(Directory.Exists(testCachePath), "cache directory already exists?");

        try
        {
            Directory.CreateDirectory(testCachePath);
            Assert.IsTrue(Directory.Exists(testCachePath), "cache directory not created");

            await TestRunnerAsync($"-cache {testCachePath} -delete", $"schema cache deleted");

            Assert.IsFalse(Directory.Exists(testCachePath), "cache directory not deleted");
        }
        finally
        {
            if (Directory.Exists(testCachePath))
            {
                Directory.Delete(testCachePath, true);
            }
        }
    }

    [TestMethod]
    public async Task TestNoSchema()
    {
        await TestRunnerNoSchemaAsync("", "print x=10", "input: succeeded");
    }

    [TestMethod]
    public async Task TestNoSchema_Fails()
    {
        await TestRunnerNoSchemaAsync("", "print x=10 | where y > 0", 
@"input: failed
(1, 20): error: KS142 - The name 'y' does not refer to any known column, table, variable or function.");
    }

    [TestMethod]
    public async Task TestNoSchema_Database_Fails()
    {
        await TestRunnerNoSchemaAsync("-database Samples", "StormEvents",
@"input: failed
(1, 1): error: KS142 - The name 'StormEvents' does not refer to any known column, table, variable or function.");
    }

    [TestMethod]
    public async Task TestServerOnly_ConnectionNoDatabase()
    {
        await TestRunnerServerOnlyAsync("", "database('Samples').StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestServerOnly_ConnectionAndDatabase()
    {
        await TestRunnerServerOnlyAsync("-database Samples", "StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestCacheOnly_ClusterAndDatabase()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help -database Samples", "StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestCacheOnly_ClusterNoDatabase()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help", "database('Samples').StormEvents", "input: succeeded");       
    }

    [TestMethod]
    public async Task TestAnalysis_HasStringLength()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help -database Samples", "StormEvents | where Source has 'ABC'",
@"input: failed
(1, 21): suggestion: KS503 - Avoid using short strings (less than 4 characters) for string comparison operations (see: https://aka.ms/kusto.stringterms).");
    }

    [TestMethod]
    public async Task TestFile_OneQuery()
    {
        await TestFileSucceedsAsync("OneQueryNoErrorsSamplesDb.kql");
    }

    [TestMethod]
    public async Task TestFile_TwoQueries()
    {
        await TestFileSucceedsAsync("TwoQueriesNoErrorsSamplesDb.kql");
    }

    [TestMethod]
    public async Task TestFile_UnknownTable_Fails()
    {
        await TestFileFailsAsync("UnknownTable.kql", "(1, 1): error: KS204 - The name 'FlurgEvents' does not refer to any known table, tabular variable or function.");
    }

    public async Task TestFileSucceedsAsync(string filename)
    {
        await TestRunnerCacheOnlyAsync($"-database Samples Queries/{filename}", $"Queries/{filename}: succeeded");
    }

    public async Task TestFileFailsAsync(string filename, string expectedOutput)
    {
        await TestRunnerCacheOnlyAsync($"-database Samples Queries/{filename}", $"Queries/{filename}: failed\n{expectedOutput}");
    }

    #region Helpers

    private Task TestRunnerAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerAsync(string commandLine, string? inputText, string expectedOutput)
    {
        var output = new StringWriter();

        var runner = new Runner(output);
        await runner.RunAsync(commandLine, inputText);

        output.Flush();
        var actualOutput = output.ToString();

        AssertOutputExqual(expectedOutput, actualOutput);
    }

    private Task TestRunnerServerOnlyAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerServerOnlyAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerServerOnlyAsync(string commandLine, string? query, string expectedOutput)
    {
        var tmpCache = GetTestCachePath();
        Assert.IsFalse(Directory.Exists(tmpCache), "temp cache already exists?");

        await TestRunnerAsync(commandLine + $" -connection {HelpConnection} -nocache -cache {tmpCache}", query, expectedOutput);

        // check that execution did not cause creation of a schema cache
        if (Directory.Exists(tmpCache))
        {
            Directory.Delete(tmpCache, true);
            Assert.Fail("temp cache exists after test");
        }
    }

    private Task TestRunnerCacheOnlyAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerCacheOnlyAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerCacheOnlyAsync(string commandLine, string? query, string expectedOutput)
    {
        Assert.IsTrue(Directory.Exists(TestCache), "static cache does not exist");
        await TestRunnerAsync(commandLine + $" -cluster {HelpCluster} -cache {TestCache}", query, expectedOutput);
    }

    private Task TestRunnerNoSchemaAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerNoSchemaAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerNoSchemaAsync(string commandLine, string? query, string expectedOutput)
    {
        await TestRunnerAsync(commandLine + $" -nocache", query, expectedOutput);
    }

    private static string GetTestCachePath()
    {
        return "TestCache_" + System.Guid.NewGuid();
    }

    private void AssertOutputExqual(string expected, string actual, string? message = null)
    {
        expected = FixLineEndings(expected.Trim());
        actual = FixLineEndings(actual.Trim());
        Assert.AreEqual(expected, actual, message);
    }

    private static string FixLineEndings(string text)
    {
        return text.Replace("\r\n", "\n");
    }
    #endregion
}
