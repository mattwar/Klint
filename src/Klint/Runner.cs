using Kusto.Language;
using Kusto.Language.Symbols;
using Kusto.Language.Editor;
using Kusto.Toolkit;
using Kusto.Data;

namespace Klint;


public class Runner
{
    public static readonly string DefaultCachePath = "%appdata%/klint/schemas";

    private readonly TextWriter _output;

    public Runner(TextWriter output)
    {
        _output = output;
    }

    public async Task RunAsync(string[] args, string? pipedInput = null)
    {
        var options = Options.Parse(args);

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
        CachedSymbolLoader? cachedLoader = null;
        FileSymbolLoader? fileLoader = null;
        var actionTaken = false;

        string cachePath = DefaultCachePath;
        if (!string.IsNullOrEmpty(options.CachePath))
        {
            cachePath = options.CachePath;
        }

        if (!string.IsNullOrEmpty(options.ServerConnection))
        {
            var connection = new KustoConnectionStringBuilder(options.ServerConnection);
            if (options.NoCache == true)
            {
                var serverLoader = new ServerSymbolLoader(connection);
                defaultClusterName = serverLoader.DefaultCluster;
                defaultDatabaseName = serverLoader.DefaultDatabase;
                loader = serverLoader;
            }
            else
            {
                cachedLoader = new CachedSymbolLoader(connection, cachePath);
                defaultClusterName = cachedLoader.ServerLoader.DefaultCluster;
                defaultDatabaseName = cachedLoader.ServerLoader.DefaultDatabase;
                fileLoader = cachedLoader.FileLoader;
                loader = cachedLoader;
            }
        }

        if (!string.IsNullOrEmpty(options.DefaultCluster))
        {
            defaultClusterName = options.DefaultCluster;
        }

        if (loader == null && options.NoCache != true)
        {
            // load symbols just from local cache
            loader = fileLoader = new FileSymbolLoader(cachePath, defaultClusterName ?? "cluster");
        }

        if (!string.IsNullOrEmpty(options.DefaultDatabase))
        {
            defaultDatabaseName = options.DefaultDatabase;
        }

        // always perform delete before generate if both are specified
        if (options.DeleteCache == true && fileLoader != null)
        {
            fileLoader.DeleteCache();
            LogMessage($"schema cache deleted");
            actionTaken = true;
        }

        if (options.GenerateCache == true && cachedLoader != null)
        {
            await GenerateCacheAsync(cachedLoader, CancellationToken.None).ConfigureAwait(false);
            LogMessage($"schema cache generated");
            actionTaken = true;
        }

        var disabledCodes = new List<string>();
        if (options.Disable != null)
        {
            disabledCodes.AddRange(options.Disable.Split(new char[] { ',', ';' }).Select(p => p.Trim()));
        }

        var filePaths = FilePatterns.GetFilePaths(options.FilePaths).ToList();

        // pre-load default database schema if analysis is to occur
        if (defaultDatabaseName != null
            && loader != null
            && (pipedInput != null || filePaths.Count > 0))
        {
            // intialize default cluster and database
            globals = await loader.AddOrUpdateDefaultDatabaseAsync(globals, defaultDatabaseName, defaultClusterName);
        }
        else if (!string.IsNullOrEmpty(defaultClusterName))
        {
            // set default cluster in globals manually
            globals = globals.WithCluster(new ClusterSymbol(defaultClusterName));
        }

        // now do the actual analysis ...
        var resolver = new SymbolResolver(loader);
        var analyzer = new Analyzer(globals, resolver);

        if (pipedInput != null)
        {
            actionTaken = true;
            await AnalyzeAsync(pipedInput, "input", disabledCodes, analyzer);
        }

        if (filePaths.Count > 0)
        {
            actionTaken = true;
            foreach (var filePath in filePaths)
            {
                var fileText = await LoadFileAsync(filePath);
                if (fileText != null)
                {
                    await AnalyzeAsync(fileText, filePath, disabledCodes, analyzer);
                }
            }
        }

        if (actionTaken == false && pipedInput == null && filePaths.Count == 0)
        {
            if (options.FilePaths.Count > 0)
            {
                LogMessage("no matching files");
            }
            else
            {
                LogMessage("no input");
            }
        }

        async Task AnalyzeAsync(string text, string source, IReadOnlyList<string> ignoreList, Analyzer analyzer)
        {
            var analysis = await analyzer.AnalyzeAsync(text, ignoreList, CancellationToken.None);

            if (analysis.Success)
            {
                LogMessage($"{source}: succeeded");
            }
            else
            {
                LogMessage($"{source}: failed");

                foreach (var message in analysis.Messages)
                {
                    _output.Write("    ");
                    _output.WriteLine(message);
                }
            }
        }

        async Task<string?> LoadFileAsync(string filePath)
        {
            try
            {
                return await File.ReadAllTextAsync(filePath);
            }
            catch (Exception e)
            {
                LogMessage($"{filePath}: failed\n{e.Message}");
                return null;
            }
        }

        void DisplayHelp()
        {
            _output.WriteLine(HelpText);
        }
    }

    public static string HelpText =
@"klint [options] <files>

options:
  -? or -help           display this help text
  -connection <string>  the connection to use to access schema from the server
  -cache <path>         overrides the default path to the local schema cache directory
  -nocache              disables use of the local schema cache
  -generate             generates cached schemas for all databases
  -delete               delete all cached schemas
  -cluster <name>       the current cluster in scope (if no connection specified)
  -database <name>      the current database in scope (if not specified by connection)
  -disable <codes>      a comma separated list of diagnositic codes to disable

files:
   one or more file paths or file path patterns

examples:
   # Run analysis on MyQueries.kql using database schemas found in local cache or server
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples MyQueries.kql

   # Run analysis on MyQueries.kql using database schemas found in local cache only
   klint -cluster help.kusto.windows.net -database Samples MyQueries.kql

   # Run analysis on MyQueries.kql using fresh database schemas from the server only
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples -nocache MyQueries.kql

   # Run analysis on MyQueries.kql using no schemas at all (probably not a good idea)
   klint -nocache MyQueries.kql

   # Pre-generate local schema cache
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -generate
";

    private static async Task GenerateCacheAsync(CachedSymbolLoader loader, CancellationToken cancellationToken)
    {
        var databaseNames = await loader.ServerLoader.LoadDatabaseNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (databaseNames != null)
        {
            foreach (var db in databaseNames)
            {
                await GenerateCachedDatabaseSchema(loader, db.Name, cancellationToken);
            }
        }
    }

    private static async Task GenerateCachedDatabaseSchema(CachedSymbolLoader loader, string databaseName, CancellationToken cancellationToken)
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
