using Kushy;
using Kusto.Language;

namespace Klint;


public static class Runner
{
    public static readonly string DefaultCachePath = "klint_schema_cache";

    public static async Task RunAsync(string[] args, TextWriter outputWriter, TextReader? inputReader = null)
    {
        var options = OptionsParser.Parse(args);

        if (options.Errors.Count > 0)
        {
            foreach (var error in options.Errors)
            {
                outputWriter.WriteLine($"klint: {error}");
            }

            return;
        }
        else if (options.HelpRequested == true)
        {
            DisplayHelp();
            return;
        }

        var globals = GlobalState.Default;
        string? defaultClusterName = null;
        string? defaultDatabaseName = null;
        SymbolLoader? loader = null;
        CachedServerSymbolLoader? cachedLoader = null;

        string cachePath = DefaultCachePath;
        if (!string.IsNullOrEmpty(options.CachePath))
        {
            cachePath = options.CachePath;
        }

        if (!string.IsNullOrEmpty(options.ServerConnection))
        {
            if (options.NoCache == true)
            {
                var serverLoader = new ServerSymbolLoader(options.ServerConnection);
                defaultClusterName = serverLoader.DefaultCluster;
                defaultDatabaseName = serverLoader.DefaultDatabase;
                loader = serverLoader;
            }
            else
            {
                cachedLoader = new CachedServerSymbolLoader(options.ServerConnection, cachePath);
                defaultClusterName = cachedLoader.ServerLoader.DefaultCluster;
                defaultDatabaseName = cachedLoader.ServerLoader.DefaultDatabase;
                loader = cachedLoader;
            }
        }

        if (!string.IsNullOrEmpty(options.DefaultCluster))
        {
            defaultClusterName = options.DefaultCluster;
        }

        if (loader == null
            && options.NoCache != true
            && !string.IsNullOrEmpty(defaultClusterName))
        {
            // load symbols just from local cache
            loader = new FileSymbolLoader(cachePath, defaultClusterName);
        }

        if (!string.IsNullOrEmpty(options.DefaultDatabase))
        {
            defaultDatabaseName = options.DefaultDatabase;
        }

        if (options.GenerateCache == true && cachedLoader != null)
        {
            await GenerateCacheAsync(cachedLoader, CancellationToken.None).ConfigureAwait(false);
            outputWriter.WriteLine($"klint: schema cache generated at: {cachePath}");
            return;
        }

        // pre-load default database schema
        if (defaultDatabaseName != null && loader != null)
        {
            globals = await loader.AddOrUpdateDefaultDatabaseAsync(globals, defaultDatabaseName, defaultClusterName);
        }

        // now do the actual analysis ...
        var resolver = new SymbolResolver(loader);
        var analyzer = new Analyzer(globals, resolver);

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
        else
        {
            outputWriter.WriteLine("klint: no input");
            outputWriter.WriteLine();
            DisplayHelp();
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

        void DisplayHelp()
        {
            outputWriter.WriteLine(OptionsParser.HelpText);
        }
    }

    private static async Task GenerateCacheAsync(CachedServerSymbolLoader loader, CancellationToken cancellationToken)
    {
        var databaseNames = await loader.ServerLoader.GetDatabaseNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (databaseNames != null)
        {
            foreach (var db in databaseNames)
            {
                await GenerateCachedDatabaseSchema(loader, db, cancellationToken);
            }
        }
    }

    private static async Task GenerateCachedDatabaseSchema(CachedServerSymbolLoader loader, string databaseName, CancellationToken cancellationToken)
    {
        var db = await loader.ServerLoader.LoadDatabaseAsync(databaseName, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (db != null)
        {
            await loader.FileLoader.SaveDatabaseAsync(db, loader.DefaultCluster, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    public static Task RunAsync(string commandLine, TextWriter outputWriter, TextReader? inputReader = null)
    {
        var args = SplitCommandLine(commandLine).ToArray();

        if (args.Length > 0 && IsThisProgram(args[0]))
        {
            args = args.Skip(1).ToArray();
        }

        return RunAsync(args, outputWriter, inputReader);
    }

    private static bool IsThisProgram(string path)
    {
        var pathFileName = Path.GetFileName(path);
        var thisFileName = Path.GetFileName(System.AppDomain.CurrentDomain.FriendlyName);
        if (string.Compare(pathFileName, thisFileName, StringComparison.OrdinalIgnoreCase) == 0)
            return true;

        pathFileName = Path.GetFileNameWithoutExtension(pathFileName);
        thisFileName = Path.GetFileNameWithoutExtension(thisFileName);

        return string.Compare(pathFileName, thisFileName, StringComparison.OrdinalIgnoreCase) == 0;
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
}
