namespace Klint;

public static class OptionsParser
{
    public static Options Parse(string[] args)
    {
        bool? help = null;
        string? server = null;
        string? cluster = null;
        string? database = null;
        string? cachePath = null;
        bool? noCache = null;
        bool? generateCache = null;

        List<string> filePaths = new List<string>();
        List<string> errors = new List<string>();

        int iArg = 0;
        for (; iArg < args.Length; iArg++)
        {
            var arg = args[iArg].Trim();

            if (arg == "-connection" && iArg + 1 < args.Length)
            {
                server = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-cluster" && iArg + 1 < args.Length)
            {
                cluster = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-database" && iArg + 1 < args.Length)
            {
                database = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-cache" && iArg + 1 < args.Length)
            {
                cachePath = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-nocache")
            {
                noCache = true;
            }
            else if (arg == "-generate")
            {
                generateCache = true;
            }
            else if (arg == "-?" || arg == "-help")
            {
                help = true;
            }
            else if (arg.StartsWith("-"))
            {
                errors.Add($"unknown option: {arg}");
            }
            else
            {
                filePaths.Add(arg);
            }
        }

        return new Options
        {
            FilePaths = filePaths,
            Errors = errors,
            ServerConnection = server,
            DefaultCluster = cluster,
            DefaultDatabase = database,
            CachePath = cachePath,
            NoCache = noCache,
            GenerateCache = generateCache,
            HelpRequested = help,
        };
    }

    public static string HelpText =
@"klint [options] <files>

options:
  -? or -help           display this help text
  -connection <string>  the connection to use to access schema from the server
  -cache <path>         overrides the default path to the local schema cache directory
  -nocache              disables use of the local schema cache
  -generate             generates cached schemas for all databases (does not do analysis)
  -cluster <name>       the current cluster in scope (if no connection specified)
  -database <name>      the current database in scope (if not specified by connection)

examples:
   # Run analysis on MyQueries.kql using database schemas found in local cache or server
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples MyQueries.kql

   # Run analysis on MyQueries.kql using database schemas found in local cache only
   klint -cluster help.kusto.windows.net -database Samples MyQueries.kql

   # Run analysis on MyQueries.kql using fresh database schemas from the server only
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -database Samples -nocache MyQueries.kql

   # Run analysis on MyQueries.kql using no schemas at all (probably not a good idea)
   klint -nocache MyQueries.kql

   # Pre-generate local schema cache (does not run analysis)
   klint -connection ""https://help.kusto.windows.net;Fed=true"" -generate
";
}

public record Options
{
    public IReadOnlyList<string> FilePaths { get; init; } = null!;
    public IReadOnlyList<string> Errors { get; init; } = null!;
    public string? ServerConnection { get; init; }
    public string? DefaultCluster { get; init; }
    public string? DefaultDatabase { get; init; }
    public string? CachePath { get; init; }
    public bool? NoCache { get; init; }
    public bool? GenerateCache { get; init; }
    public bool? HelpRequested { get; init; }
}
