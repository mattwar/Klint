using Klint;

await Runner.RunAsync(args, Console.Out, Console.IsInputRedirected ? Console.In : null);