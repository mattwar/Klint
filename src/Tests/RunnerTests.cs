using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Klint;

namespace Tests;

[TestClass]
public class RunnerTests
{
    private static readonly string HelpCluster = "help.kusto.windows.net";
    private static readonly string HelpConnection = $"https://{HelpCluster};Fed=true";
    private static readonly string TestCache = "Schemas";

    [TestMethod]
    public async Task TestHelp()
    {
        await TestRunnerAsync("-?", OptionsParser.HelpText);
    }

    [TestMethod]
    public async Task TestUnknowndOption()
    {
        await TestRunnerAsync("-whatever", "klint: unknown option: -whatever");
    }

    [TestMethod]
    public async Task TestConnection_GenerateSchema()
    {
        var cacheDirectory = "GeneratedSchemas";

        if (Directory.Exists(cacheDirectory))
        {
            Directory.Delete(cacheDirectory, true);
        }

        Assert.IsFalse(Directory.Exists(cacheDirectory), "cache directory not deleted");

        await TestRunnerAsync($"-connection {HelpConnection} -cache {cacheDirectory} -generate", $"klint: schema cache generated at: {cacheDirectory}");

        Assert.IsTrue(Directory.Exists(cacheDirectory), "Cache directory not created");

        var clusterCache = Path.Combine(cacheDirectory, HelpCluster);
        Assert.IsTrue(Directory.Exists(clusterCache), "Cluster cache directory not created.");

        var samplesFile = Path.Combine(clusterCache, "Samples.json");
        Assert.IsTrue(File.Exists(samplesFile), "Samples.json file not created");
    }

    [TestMethod]
    public async Task TestNoSchemaSucceeds()
    {
        await TestRunnerNoSchemaAsync("", "print x=10", "input: succeeded");
    }

    [TestMethod]
    public async Task TestNoSchemaFails()
    {
        await TestRunnerNoSchemaAsync("", "print x=10 | where y > 0", 
@"input: failed
(1, 20): error: The name 'y' does not refer to any known column, table, variable or function.");
    }

    [TestMethod]
    public async Task TestDatabaseNoSchemaFails()
    {
        await TestRunnerNoSchemaAsync("-database Samples", "StormEvents",
@"input: failed
(1, 1): error: The name 'StormEvents' does not refer to any known column, table, variable or function.");
    }

    [TestMethod]
    public async Task TestConnectionNoDatabase_NoCache()
    {
        await TestRunnerNoCacheAsync("", "database('Samples').StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestConnectionAndDatabase_NoCache()
    {
        await TestRunnerNoCacheAsync("-database Samples", "StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestClusterAndDatabase_CacheOnly()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help -database Samples", "StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestClusterNoDatabase_CacheOnly()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help", "database('Samples').StormEvents", "input: succeeded");       
    }

    [TestMethod]
    public async Task TestAnalysis_HasStringLength()
    {
        await TestRunnerCacheOnlyAsync($"-cluster help -database Samples", "StormEvents | where Source has 'ABC'",
@"input: failed
(1, 21): suggestion: Avoid using short strings (less than 4 characters) for string comparison operations (see: https://aka.ms/kusto.stringterms).");
    }

    [TestMethod]
    public async Task TestFile_OneQuery_Succeeds()
    {
        await TestFileSucceedsAsync("OneQueryNoErrorsSamplesDb.kql");
    }

    [TestMethod]
    public async Task TestFile_TwoQueries_Succeeds()
    {
        await TestFileSucceedsAsync("TwoQueriesNoErrorsSamplesDb.kql");
    }

    [TestMethod]
    public async Task TestFile_UnknownTable_Fails()
    {
        await TestFileFailsAsync("UnknownTable.kql", "(1, 1): error: The name 'FlurgEvents' does not refer to any known table, tabular variable or function.");
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
        TextReader? input = null;
        if (inputText != null)
        {
            input = new StringReader(inputText);
        }

        var output = new StringWriter();

        await Runner.RunAsync(commandLine, output, input);

        output.Flush();
        var actualOutput = output.ToString();

        AssertOutputExqual(expectedOutput, actualOutput);
    }

    private Task TestRunnerNoCacheAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerNoCacheAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerNoCacheAsync(string commandLine, string query, string expectedOutput)
    {
        var tmpCache = "TestCache" + System.Guid.NewGuid();
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

    private async Task TestRunnerCacheOnlyAsync(string commandLine, string query, string expectedOutput)
    {
        Assert.IsTrue(Directory.Exists(TestCache), "static cache does not exist");
        await TestRunnerAsync(commandLine + $" -cluster {HelpCluster} -cache {TestCache}", query, expectedOutput);
    }

    private Task TestRunnerNoSchemaAsync(string commandLine, string expectedOutput)
    {
        return TestRunnerNoSchemaAsync(commandLine, null, expectedOutput);
    }

    private async Task TestRunnerNoSchemaAsync(string commandLine, string query, string expectedOutput)
    {
        await TestRunnerAsync(commandLine + $" -nocache", query, expectedOutput);
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