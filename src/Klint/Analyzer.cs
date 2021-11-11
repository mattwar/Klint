using Kushy;
using Kusto.Language;
using Kusto.Language.Editor;

namespace Klint;

public class Analyzer
{
    private readonly SymbolLoader? _loader;
    private GlobalState _globals;

    public Analyzer(GlobalState? globals = null, SymbolLoader? loader = null)
    {
        _globals = globals ?? GlobalState.Default;
        _loader = loader;
    }

    public async Task<AnalysisResult> AnalyzeAsync(string text, CancellationToken ct)
    {
        var script = CodeScript.From(text, _globals);

        if (_loader != null)
        {
            script = await _loader.AddReferencedDatabasesAsync(script, cancellationToken: ct);
            _globals = script.Globals;
        }

        var messages = new List<string>();

        foreach (var block in script.Blocks)
        {
            var dx = block.Service.GetDiagnostics(cancellationToken: ct);

            foreach (var d in dx)
            {
                messages.Add(GetMessage(d, script));
            }

            var adx = block.Service.GetAnalyzerDiagnostics(cancellationToken: ct);

            foreach (var d in adx)
            {
                messages.Add(GetMessage(d, script));
            }
        }

        return new AnalysisResult(messages.Count == 0, messages.AsReadOnly());
    }

    private string GetMessage(Diagnostic d, CodeScript script)
    {
        if (script.TryGetLineAndOffset(d.Start, out var line, out var offset))
        {
            return $"({line}, {offset}): {d.Severity.ToString().ToLower()}: {d.Message}";
        }
        else
        {
            return $"{d.Severity.ToString().ToLower()}: {d.Message}";
        }
    }
}

public record AnalysisResult(bool Success, IReadOnlyList<string> Messages);
