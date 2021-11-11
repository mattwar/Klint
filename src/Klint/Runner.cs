using Kushy;
using Kusto.Language;

namespace Klint;


public static class Runner
{
    public static async Task RunAsync(string[] args, TextWriter outputWriter, TextReader? inputReader = null)
    {
        var options = OptionsParser.Parse(args);

        if (options.Errors.Count > 0)
        {
            outputWriter.WriteLine("Errors parsing command line");
            foreach (var error in options.Errors)
            {
                outputWriter.WriteLine($"klint: {error}");
            }
            return;
        }
        else if (options.Help == true)
        {
            outputWriter.WriteLine(HelpText);
            return;
        }

        var globals = GlobalState.Default;
        SymbolLoader? loader = null;

        if (!string.IsNullOrEmpty(options.SchemaConnection))
        {
            loader = new SymbolLoader(options.SchemaConnection);

            if (!string.IsNullOrEmpty(options.SchemaDatabase))
            {
                globals = await loader.AddOrUpdateDefaultDatabaseAsync(globals, options.SchemaDatabase);
            }
        }

        var analyzer = new Analyzer(globals, loader);

        if (options.FilePaths.Count > 0)
        {
            foreach (var filePath in options.FilePaths)
            {
                var fileText = await File.ReadAllTextAsync(filePath);
                await AnalyzeAsync(fileText, filePath, analyzer);
            }
        }
        else if (inputReader != null)
        {
            var fileText = await inputReader.ReadToEndAsync();
            await AnalyzeAsync(fileText, "input", analyzer);
        }

        async Task AnalyzeAsync(string text, string source, Analyzer analyzer)
        {
            outputWriter.Write($"{source}: ");

            var analysis = await analyzer.AnalyzeAsync(text, CancellationToken.None);

            if (analysis.Success)
            {
                outputWriter.WriteLine("succeeded");
            }
            else
            {
                outputWriter.WriteLine("failed");

                foreach (var message in analysis.Messages)
                {
                    outputWriter.WriteLine(message);
                }
            }
        }
    }

    public static Task RunAsync(string commandLine, TextWriter outputWriter, TextReader? inputReader = null)
    {
        var args = SplitCommandLine(commandLine).ToArray();

        if (args.Length > 0 
            && (args[0].EndsWith("flint", StringComparison.OrdinalIgnoreCase) || args[0].EndsWith("flint.exe", StringComparison.OrdinalIgnoreCase)))
        {
            args = args.Skip(1).ToArray();
        }

        return RunAsync(args, outputWriter, inputReader);
    }

    public static IEnumerable<string> SplitCommandLine(string commandLine)
    {
        int index = 0;
        for (; index < commandLine.Length; index++)
        {
            var ch = commandLine[index];

            if (ch == '"')
            {
                var start = index;
                do
                {
                    index++;
                }
                while (index < commandLine.Length && commandLine[index] != '"');

                yield return commandLine.Substring(start + 1, index - start - 1); // strip quotes...
            }
            else if (!char.IsWhiteSpace(ch))
            {
                var start = index;
                while (index < commandLine.Length && !char.IsWhiteSpace(commandLine[index]))
                {
                    index++;
                }

                yield return commandLine.Substring(start, index - start);
            }
        }
    }

    public static string HelpText =
@"klint [options] <files>

options:
  -?               display this help
  -c <connection>  specifies the connection string to use to access metadata
  -d <database>    specifies the current database in scope";
}
