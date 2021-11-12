using Klint;

string? pipedInput = null;
if (Console.IsInputRedirected)
{
    pipedInput = await Console.In.ReadToEndAsync();
}

var runner = new Runner(Console.Out);

await runner.RunAsync(args, pipedInput);