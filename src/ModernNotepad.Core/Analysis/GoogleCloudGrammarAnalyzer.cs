using System.Net.Http.Headers;
using System.Text.Json;

namespace ModernNotepad.Core.Analysis;

/// <summary>
/// Uses Google Cloud Natural Language syntax analysis and maps its UTF-16 token
/// offsets/part-of-speech/dependency data into Modern Notepad grammar categories.
/// </summary>
public sealed class GoogleCloudGrammarAnalyzer
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "who", "whom", "what", "which", "whose", "where", "when", "why", "how"
    };

    private readonly HttpClient _httpClient;

    public GoogleCloudGrammarAnalyzer(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? SharedHttpClient;
    }

    public const string DisplayName = "Google Cloud Natural Language";

    public async Task<GrammarAnalysis> AnalyzeAsync(
        string text,
        IReadOnlyList<TextToken>? tokens = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        if (tokens.Count == 0)
        {
            return ProviderGrammarAnalysis.Empty();
        }

        var apiKey = ResolveApiKey(
            ReadEnvironmentVariable("GOOGLE_CLOUD_NL_API_KEY", EnvironmentVariableTarget.Process),
            ReadEnvironmentVariable("GOOGLE_CLOUD_NL_API_KEY", EnvironmentVariableTarget.User),
            ReadEnvironmentVariable("GOOGLE_CLOUD_NL_API_KEY", EnvironmentVariableTarget.Machine),
            ReadEnvironmentVariable("GOOGLE_API_KEY", EnvironmentVariableTarget.Process),
            ReadEnvironmentVariable("GOOGLE_API_KEY", EnvironmentVariableTarget.User),
            ReadEnvironmentVariable("GOOGLE_API_KEY", EnvironmentVariableTarget.Machine));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Google Cloud Natural Language requires GOOGLE_CLOUD_NL_API_KEY or GOOGLE_API_KEY.");
        }

        var requestPayload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            document = new
            {
                type = "PLAIN_TEXT",
                content = text
            },
            encodingType = "UTF16"
        });

        const string endpoint = "https://language.googleapis.com/v1/documents:analyzeSyntax";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(requestPayload)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-goog-api-key", apiKey);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = responseBytes.Length == 0
                ? response.ReasonPhrase
                : System.Text.Encoding.UTF8.GetString(responseBytes);
            if (error?.Length > 2_000)
            {
                error = error[..2_000] + "…";
            }

            throw new HttpRequestException(
                $"Google Cloud Natural Language returned HTTP {(int)response.StatusCode}: {error}",
                inner: null,
                response.StatusCode);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ParseSyntaxResponse(responseBytes, tokens, cancellationToken);
    }

    internal static string? ResolveApiKey(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    internal static GrammarAnalysis ParseSyntaxResponse(
        ReadOnlyMemory<byte> responseBytes,
        IReadOnlyList<TextToken> tokens,
        CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(responseBytes);
        if (!document.RootElement.TryGetProperty("tokens", out var tokensElement)
            || tokensElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Google Cloud syntax response did not contain tokens.");
        }

        var cloudTokens = new List<CloudToken>(tokensElement.GetArrayLength());
        foreach (var tokenElement in tokensElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tokenElement.TryGetProperty("text", out var textElement)
                || !textElement.TryGetProperty("content", out var contentElement)
                || !textElement.TryGetProperty("beginOffset", out var offsetElement))
            {
                continue;
            }

            var content = contentElement.GetString() ?? string.Empty;
            var start = offsetElement.GetInt32();
            var tag = tokenElement.TryGetProperty("partOfSpeech", out var partOfSpeech)
                && partOfSpeech.TryGetProperty("tag", out var tagElement)
                    ? tagElement.GetString() ?? string.Empty
                    : string.Empty;
            var dependency = tokenElement.TryGetProperty("dependencyEdge", out var edge)
                && edge.TryGetProperty("label", out var labelElement)
                    ? labelElement.GetString() ?? string.Empty
                    : string.Empty;

            if (start >= 0 && content.Length > 0)
            {
                cloudTokens.Add(new CloudToken(start, content.Length, content, tag, dependency));
            }
        }

        if (cloudTokens.Count == 0 && tokens.Count > 0)
        {
            throw new InvalidDataException("Google Cloud syntax response contained no usable token offsets.");
        }

        var assignments = MapCloudTokens(tokens, cloudTokens, cancellationToken);
        return ProviderGrammarAnalysis.Create(tokens, assignments, cancellationToken);
    }

    private static IReadOnlyList<GrammarCategory> MapCloudTokens(
        IReadOnlyList<TextToken> localTokens,
        IReadOnlyList<CloudToken> cloudTokens,
        CancellationToken cancellationToken)
    {
        var assignments = new List<GrammarCategory>(localTokens.Count);
        var cloudIndex = 0;
        foreach (var localToken in localTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (cloudIndex < cloudTokens.Count
                   && cloudTokens[cloudIndex].End <= localToken.Span.Start)
            {
                cloudIndex++;
            }

            var categories = new List<GrammarCategory>(2);
            var index = cloudIndex;
            while (index < cloudTokens.Count && cloudTokens[index].Start < localToken.Span.End)
            {
                if (cloudTokens[index].End > localToken.Span.Start)
                {
                    categories.Add(Classify(cloudTokens[index]));
                }
                index++;
            }

            assignments.Add(ChooseCategory(categories));
        }

        return assignments;
    }

    private static GrammarCategory Classify(CloudToken token)
    {
        if (Interrogatives.Contains(token.Content))
        {
            return GrammarCategory.Interrogative;
        }

        return token.PartOfSpeech.ToUpperInvariant() switch
        {
            "VERB" => GrammarCategory.Verb,
            "ADJ" => GrammarCategory.Adjective,
            "ADV" => GrammarCategory.Adverb,
            "PRON" => GrammarCategory.Pronoun,
            "ADP" => GrammarCategory.Preposition,
            "CONJ" => GrammarCategory.Conjunction,
            "DET" or "NUM" => GrammarCategory.Quantifier,
            "NOUN" => IsSubjectDependency(token.Dependency)
                ? GrammarCategory.SubjectNoun
                : GrammarCategory.ObjectNoun,
            _ => GrammarCategory.Other
        };
    }

    private static bool IsSubjectDependency(string dependency) =>
        dependency.Equals("NSUBJ", StringComparison.OrdinalIgnoreCase)
        || dependency.Equals("NSUBJPASS", StringComparison.OrdinalIgnoreCase)
        || dependency.Equals("CSUBJ", StringComparison.OrdinalIgnoreCase)
        || dependency.Equals("CSUBJPASS", StringComparison.OrdinalIgnoreCase)
        || dependency.Equals("ATTR", StringComparison.OrdinalIgnoreCase);

    private static GrammarCategory ChooseCategory(IReadOnlyList<GrammarCategory> categories)
    {
        if (categories.Count == 0)
        {
            return GrammarCategory.Other;
        }

        return categories
            .OrderByDescending(CategoryPriority)
            .First();
    }

    private static int CategoryPriority(GrammarCategory category) => category switch
    {
        GrammarCategory.Interrogative => 100,
        GrammarCategory.Verb => 90,
        GrammarCategory.SubjectNoun => 80,
        GrammarCategory.ObjectNoun => 70,
        GrammarCategory.Pronoun => 60,
        GrammarCategory.Adjective => 55,
        GrammarCategory.Adverb => 50,
        GrammarCategory.Conjunction => 45,
        GrammarCategory.Preposition => 40,
        GrammarCategory.Quantifier => 35,
        _ => 0
    };

    private static string? ReadEnvironmentVariable(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch
        {
            return null;
        }
    }

    private sealed record CloudToken(
        int Start,
        int Length,
        string Content,
        string PartOfSpeech,
        string Dependency)
    {
        public int End => Start + Length;
    }
}
