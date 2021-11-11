using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Klint;

namespace Tests;

[TestClass]
public class RunnerTests
{
    private static readonly string HelpConnection = "https://help.kusto.windows.net;Fed=true";
    private static readonly string HelpClusterArgs = $"-c \"{HelpConnection}\"";
    private static readonly string HelpClusterSamplesDbArgs = $"{HelpClusterArgs} -d Samples";

    [TestMethod]
    public async Task TestHelp()
    {
        await TestRunnerAsync("-?", Runner.HelpText);
    }

    [TestMethod]
    public async Task TestNoSchemaSucceeds()
    {
        await TestRunnerAsync("", "print x=10", "input: succeeded");
    }

    [TestMethod]
    public async Task TestNoSchemaFails()
    {
        await TestRunnerAsync("", "print x=10 | where y > 0", @"input: failed
(1, 20): error: The name 'y' does not refer to any known column, table, variable or function.");
    }

    [TestMethod]
    public async Task TestConnectToHelpCluster()
    {
        await TestRunnerAsync(HelpClusterArgs, "database('Samples').StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestConnectToSamplesDatabase()
    {
        await TestRunnerAsync(HelpClusterSamplesDbArgs, "StormEvents", "input: succeeded");
    }

    [TestMethod]
    public async Task TestOneQueryNoErrorsSamplesDb()
    {
        await TestRunnerAsync($"{HelpClusterSamplesDbArgs} Queries/OneQueryNoErrorsSamplesDb.kql", "Queries/OneQueryNoErrorsSamplesDb.kql: succeeded");
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
