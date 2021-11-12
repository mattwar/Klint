using Kushy;
using Kusto.Language;

namespace Klint;


public class Runner
{
    public static readonly string DefaultCachePath = "klint_schema_cache";

    private readonly TextWriter _output;

    public Runner(TextWriter output)
    {
        _output = output;
    }

    public async Task RunAsync(string[] args, string? pipedInput = null)
    {
        var options = OptionsParser.Parse(args);

        if (options.Errors.Count > 0)
        {
            foreach (var error in options.Errors)
            {
                LogMessage(error);
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
            LogMessage($"schema cache generated at: {cachePath}");
            return;
        }

        // pre-load default database schema
        if (defaultDatabaseName != null && loader != null)
        {
            globals = await loader.AddOrUpdateDefaultDatabaseAsync(globals, defaultDatabaseName, defaultClusterName);
        }

        if (pipedInput == null && options.FilePaths.Count == 0)
        {
            LogMessage("no input");
            DisplayHelp();
            return;
        }

        // now do the actual analysis ...

        var resolver = new SymbolResolver(loader);
        var analyzer = new Analyzer(globals, resolver);

        if (pipedInput != null)
        {
            await AnalyzeAsync(pipedInput, "input", analyzer);
        }

        if (options.FilePaths.Count > 0)
        {
            foreach (var filePath in options.FilePaths)
            {
                var fileText = await File.ReadAllTextAsync(filePath);
                await AnalyzeAsync(fileText, filePath, analyzer);
            }
        }
        
        async Task AnalyzeAsync(string text, string source, Analyzer analyzer)
        {
            var analysis = await analyzer.AnalyzeAsync(text, CancellationToken.None);

            if (analysis.Success)
            {
                LogMessage($"{source}: succeeded");
            }
            else
            {
                LogMessage($"{source}: failed");

                foreach (var message in analysis.Messages)
                {
                    _output.WriteLine(message);
                }
            }
        }

        void DisplayHelp()
        {
            _output.WriteLine(OptionsParser.HelpText);
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

    private void LogMessage(string message)
    {
        _output.WriteLine(message);
    }

    public Task RunAsync(string commandLine, string? pipedInput = null)
    {
        var args = SplitCommandLine(commandLine).ToArray();

        if (args.Length > 0 && IsThisProgram(args[0]))
        {
            args = args.Skip(1).ToArray();
        }

        return RunAsync(args, pipedInput);
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
