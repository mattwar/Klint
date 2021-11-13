namespace Klint;

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
    public bool? DeleteCache { get; init; }
    public bool? HelpRequested { get; init; }

    public static Options Parse(string[] args)
    {
        bool? help = null;
        string? server = null;
        string? cluster = null;
        string? database = null;
        string? cachePath = null;
        bool? noCache = null;
        bool? generateCache = null;
        bool? deleteCache = null;

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
            else if (arg == "-delete")
            {
                deleteCache = true;
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
            DeleteCache = deleteCache,
            HelpRequested = help,
        };
    }
}
