using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace ModernNotepad.Core.Analysis;

public sealed class AIGrammarAnalyzer : IGrammarAnalyzer
{
    private readonly ChatClient _chatClient;

    public AIGrammarAnalyzer()
    {
        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OPENAI_API_KEY environment variable is not set.");
        }
        
        _chatClient = new ChatClient("gpt-5.4-mini", apiKey);
    }

    public async Task<GrammarAnalysis> AnalyzeAsync(
        string text,
        IReadOnlyList<TextToken>? tokens = null,
        IReadOnlyList<TextSpan>? sentences = null,
        CancellationToken cancellationToken = default)
    {
        var counts = Enum.GetValues<GrammarCategory>().ToDictionary(c => c, _ => 0);
        var spans = new List<ColoredSpan>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new GrammarAnalysis(spans, counts);
        }

        tokens ??= TextTokenizer.Tokenize(text, cancellationToken);
        if (tokens.Count == 0)
        {
            return new GrammarAnalysis(spans, counts);
        }

        var prompt = "Analyze the grammar categories of the words in the following text. " +
            "Respond ONLY with a JSON array where each object has 'word' (the exact text token) and 'category' (an integer representing the GrammarCategory enum). " +
            "Categories are: Other=0, SubjectNoun=1, Verb=2, ObjectNoun=3, Adjective=4, Adverb=5, Pronoun=6, Preposition=7, Conjunction=8, Interrogative=9, Quantifier=10.\n\n" +
            "Text:\n" + text;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage("You are a helpful grammar analysis assistant. Always respond with pure JSON array without markdown wrapping. Only include actual words from the text, no punctuation."),
            new UserChatMessage(prompt)
        };

        try
        {
            var response = await _chatClient.CompleteChatAsync(messages, cancellationToken: cancellationToken);
            var json = response.Value.Content[0].Text;
            
            if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            {
                int end = json.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
                if (end > 7)
                {
                    json = json.Substring(7, end - 7).Trim();
                }
            }
            else if (json.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            {
                int end = json.LastIndexOf("```", StringComparison.OrdinalIgnoreCase);
                if (end > 3)
                {
                    json = json.Substring(3, end - 3).Trim();
                }
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsedResult = JsonSerializer.Deserialize<List<AITokenResult>>(json, options);

            if (parsedResult != null)
            {
                int aiIndex = 0;
                foreach (var token in tokens)
                {
                    if (string.IsNullOrWhiteSpace(token.Normalized)) continue;
                    
                    GrammarCategory category = GrammarCategory.Other;
                    
                    if (aiIndex < parsedResult.Count && parsedResult[aiIndex].Word.Equals(token.Text, StringComparison.OrdinalIgnoreCase))
                    {
                        category = parsedResult[aiIndex].Category;
                        aiIndex++;
                    }
                    else
                    {
                        var match = parsedResult.FirstOrDefault(p => p.Word.Equals(token.Text, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            category = match.Category;
                        }
                    }

                    if (!Enum.IsDefined(typeof(GrammarCategory), category))
                    {
                        category = GrammarCategory.Other;
                    }

                    counts[category]++;
                    if (category != GrammarCategory.Other)
                    {
                        spans.Add(new ColoredSpan(token.Span, category));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AIGrammarAnalyzer Error: {ex.Message}");
            // Fallback to traditional or return empty if error
        }

        return new GrammarAnalysis(spans, counts);
    }

    private class AITokenResult
    {
        public string Word { get; set; } = string.Empty;
        public GrammarCategory Category { get; set; } = GrammarCategory.Other;
    }
}
