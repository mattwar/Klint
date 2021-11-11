namespace Klint;

public static class OptionsParser
{
    public static Options Parse(string[] args)
    {
        bool? help = null;
        string? schemaConnection = null;
        string? schemaDatabase = null;
        List<string> filePaths = new List<string>();
        List<string> errors = new List<string>();

        int iArg = 0;
        for (; iArg < args.Length; iArg++)
        {
            var arg = args[iArg].Trim();

            if (arg == "-c" && iArg + 1 < args.Length)
            {
                schemaConnection = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-d" && iArg + 1 < args.Length)
            {
                schemaDatabase = args[iArg + 1];
                iArg++;
            }
            else if (arg == "-?")
            {
                help = true;
            }
            else if (arg.StartsWith("-"))
            {
                errors.Add($"Unknown option: {arg}");
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
            SchemaConnection = schemaConnection,
            SchemaDatabase = schemaDatabase,
            Help = help
        };
    }
}

public record Options
{
    public IReadOnlyList<string> FilePaths { get; init; } = null!;
    public IReadOnlyList<string> Errors { get; init; } = null!;
    public string? SchemaConnection { get; init; }
    public string? SchemaDatabase { get; init; }
    public bool? Help { get; init; }
}
