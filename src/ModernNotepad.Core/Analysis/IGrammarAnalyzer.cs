using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ModernNotepad.Core.Analysis;

public interface IGrammarAnalyzer
{
    Task<GrammarAnalysis> AnalyzeAsync(
        string text,
        IReadOnlyList<TextToken>? tokens = null,
        IReadOnlyList<TextSpan>? sentences = null,
        CancellationToken cancellationToken = default);
}
