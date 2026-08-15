#!/usr/bin/env python3
"""Build Modern Notepad's deterministic English lexical database.

Combines Princeton WordNet 3.0 for broad lexical coverage with the tagged
Brown corpus for attested inflections and part-of-speech frequency priors.
Output is deterministic.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import math
import re
from collections import defaultdict
from pathlib import Path

import nltk


SCRIPT_DIR = Path(__file__).resolve().parent
REPOSITORY_ROOT = SCRIPT_DIR.parent
ANALYSIS_DIR = REPOSITORY_ROOT / "src" / "ModernNotepad.Core" / "Analysis"
OUTPUT_SOURCE = ANALYSIS_DIR / "GrammarLexicon.g.cs"
OUTPUT_ASSET = ANALYSIS_DIR / "Data" / "GrammarLexicon.tsv.gz"
MINIMUM_WORD_COUNT = 100_000
WORD_PATTERN = re.compile(r"[A-Za-z]+(?:[-'\N{RIGHT SINGLE QUOTATION MARK}][A-Za-z]+)*")

# Order must match LexiconTag and LexiconProfile.GetWeight in generated C#.
TAG_ORDER = (
    "NOUN", "VERB", "ADJ", "ADV", "PRON",
    "ADP", "CONJ", "DET", "PRT", "NUM",
)
TAG_INDEX = {tag: index for index, tag in enumerate(TAG_ORDER)}
GRAMMAR_CATEGORY_VALUE = {
    "NOUN": 3, "VERB": 2, "ADJ": 4, "ADV": 5, "PRON": 6,
    "ADP": 7, "CONJ": 8, "DET": 11, "PRT": 12, "NUM": 10,
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--no-download", action="store_true")
    return parser.parse_args()


def ensure_resource(resource: str, download_name: str, no_download: bool) -> None:
    try:
        nltk.data.find(resource)
    except LookupError:
        if no_download:
            raise RuntimeError(
                f"Missing NLTK resource '{download_name}'. "
                "Run once without --no-download."
            ) from None
        if not nltk.download(download_name, quiet=False):
            raise RuntimeError(f"Unable to download '{download_name}'.")


def normalize(word: str) -> str | None:
    value = word.replace("\N{RIGHT SINGLE QUOTATION MARK}", "'").lower()
    return value if WORD_PATTERN.fullmatch(value) else None


def load_counts(no_download: bool) -> tuple[dict[str, dict[str, int]], dict[str, int]]:
    ensure_resource("corpora/brown", "brown", no_download)
    ensure_resource("taggers/universal_tagset", "universal_tagset", no_download)
    try:
        nltk.data.find("corpora/wordnet.zip")
    except LookupError:
        ensure_resource("corpora/wordnet", "wordnet", no_download)

    from nltk.corpus import brown, wordnet

    counts: dict[str, dict[str, int]] = defaultdict(lambda: defaultdict(int))
    brown_words: set[str] = set()
    for word, tag in brown.tagged_words(tagset="universal"):
        value = normalize(word)
        if value is not None and tag in TAG_INDEX:
            counts[value][tag] += 1
            brown_words.add(value)

    wordnet_words: set[str] = set()
    wordnet_tags = {
        wordnet.NOUN: "NOUN", wordnet.VERB: "VERB",
        wordnet.ADJ: "ADJ", wordnet.ADV: "ADV",
    }
    for part_of_speech, tag in wordnet_tags.items():
        for lemma in wordnet.all_lemma_names(part_of_speech):
            value = normalize(lemma)
            if value is not None:
                counts[value][tag] += 1
                wordnet_words.add(value)

    # Exception lists add irregular surfaces such as children, went, and better.
    wordnet.ensure_loaded()
    for part_of_speech, exceptions in getattr(wordnet, "_exception_map", {}).items():
        tag = wordnet_tags.get(part_of_speech)
        if tag is None:
            continue
        for surface in exceptions:
            value = normalize(surface)
            if value is not None:
                counts[value][tag] += 2
                wordnet_words.add(value)

    return counts, {"brown": len(brown_words), "wordnet": len(wordnet_words)}


def pack_entry(tag_counts: dict[str, int]) -> tuple[int, int, int]:
    maximum = max(tag_counts.values())
    flags = 0
    weights = 0
    for tag, count in tag_counts.items():
        index = TAG_INDEX[tag]
        flags |= 1 << index
        weight = max(1, round(15 * math.log1p(count) / math.log1p(maximum)))
        weights |= min(15, weight) << (index * 4)
    dominant = max(tag_counts, key=lambda tag: (tag_counts[tag], -TAG_INDEX[tag]))
    return GRAMMAR_CATEGORY_VALUE[dominant], flags, weights


def build_asset(counts: dict[str, dict[str, int]]) -> tuple[bytes, int]:
    lines: list[str] = []
    ambiguous = 0
    for word in sorted(counts):
        category, flags, weights = pack_entry(counts[word])
        ambiguous += int(bool(flags & (flags - 1)))
        lines.append(f"{word}\t{category}\t{flags:x}\t{weights:x}\n")
    raw = "".join(lines).encode("utf-8")
    output = io.BytesIO()
    with gzip.GzipFile(fileobj=output, mode="wb", compresslevel=9, mtime=0) as stream:
        stream.write(raw)
    return output.getvalue(), ambiguous


def build_source(word_count: int, ambiguous_count: int, asset_sha256: str) -> bytes:
    source = f'''// <auto-generated>
// Generated by scripts/generate_lexicon.py from WordNet 3.0 and Brown.
// Do not edit by hand. See THIRD_PARTY_NOTICES.md for data attribution.
// </auto-generated>

using System.Globalization;
using System.IO.Compression;
using System.Numerics;

namespace ModernNotepad.Core.Analysis;

[Flags]
internal enum LexiconTag : ushort
{{
    None = 0,
    Noun = 1 << 0,
    Verb = 1 << 1,
    Adjective = 1 << 2,
    Adverb = 1 << 3,
    Pronoun = 1 << 4,
    Preposition = 1 << 5,
    Conjunction = 1 << 6,
    Determiner = 1 << 7,
    Particle = 1 << 8,
    Quantifier = 1 << 9
}}

internal readonly record struct LexiconProfile(LexiconTag Tags, ulong PackedWeights)
{{
    public int GetWeight(LexiconTag tag)
    {{
        if (tag == LexiconTag.None || (Tags & tag) == 0)
        {{
            return 0;
        }}
        var index = BitOperations.TrailingZeroCount((uint)tag);
        return (int)((PackedWeights >> (index * 4)) & 0xFUL);
    }}
}}

/// <summary>
/// Offline English lexicon generated from WordNet and the Brown corpus.
/// The legacy <see cref="Lexicon"/> field remains source-compatible.
/// </summary>
public static class GrammarLexicon
{{
    private const string ResourceName =
        "ModernNotepad.Core.Analysis.Data.GrammarLexicon.tsv.gz";

    public const int GeneratedWordCount = {word_count:_};
    public const int GeneratedAmbiguousWordCount = {ambiguous_count:_};
    public const string GeneratedAssetSha256 = "{asset_sha256}";
    public static readonly Dictionary<string, GrammarCategory> Lexicon;
    private static readonly Dictionary<string, LexiconProfile> AmbiguousProfiles;

    static GrammarLexicon()
    {{
        Lexicon = new Dictionary<string, GrammarCategory>(
            GeneratedWordCount, StringComparer.OrdinalIgnoreCase);
        AmbiguousProfiles = new Dictionary<string, LexiconProfile>(
            GeneratedAmbiguousWordCount, StringComparer.OrdinalIgnoreCase);

        using var compressed = typeof(GrammarLexicon).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded grammar lexicon '{{ResourceName}}' was not found.");
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        while (reader.ReadLine() is {{ }} line)
        {{
            var firstTab = line.IndexOf('\t');
            var secondTab = line.IndexOf('\t', firstTab + 1);
            var thirdTab = line.IndexOf('\t', secondTab + 1);
            if (firstTab <= 0 || secondTab <= firstTab || thirdTab <= secondTab)
            {{
                throw new InvalidDataException("The embedded grammar lexicon is malformed.");
            }}
            var word = line[..firstTab];
            var category = (GrammarCategory)int.Parse(
                line.AsSpan(firstTab + 1, secondTab - firstTab - 1),
                NumberStyles.None, CultureInfo.InvariantCulture);
            var tags = (LexiconTag)ushort.Parse(
                line.AsSpan(secondTab + 1, thirdTab - secondTab - 1),
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            var weights = ulong.Parse(
                line.AsSpan(thirdTab + 1),
                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

            Lexicon.Add(word, category);
            var bits = (ushort)tags;
            if ((bits & (bits - 1)) != 0)
            {{
                AmbiguousProfiles.Add(word, new LexiconProfile(tags, weights));
            }}
        }}
        if (Lexicon.Count != GeneratedWordCount)
        {{
            throw new InvalidDataException(
                $"Expected {{GeneratedWordCount:N0}} grammar words but loaded {{Lexicon.Count:N0}}.");
        }}
    }}

    internal static bool TryGetProfile(string word, out LexiconProfile profile)
    {{
        if (AmbiguousProfiles.TryGetValue(word, out profile))
        {{
            return true;
        }}
        if (!Lexicon.TryGetValue(word, out var category))
        {{
            profile = default;
            return false;
        }}
        var tag = category switch
        {{
            GrammarCategory.SubjectNoun or GrammarCategory.ObjectNoun => LexiconTag.Noun,
            GrammarCategory.Verb => LexiconTag.Verb,
            GrammarCategory.Adjective => LexiconTag.Adjective,
            GrammarCategory.Adverb => LexiconTag.Adverb,
            GrammarCategory.Pronoun => LexiconTag.Pronoun,
            GrammarCategory.Preposition => LexiconTag.Preposition,
            GrammarCategory.Conjunction => LexiconTag.Conjunction,
            GrammarCategory.Determiner => LexiconTag.Determiner,
            GrammarCategory.Particle => LexiconTag.Particle,
            GrammarCategory.Quantifier => LexiconTag.Quantifier,
            _ => LexiconTag.None
        }};
        profile = new LexiconProfile(
            tag,
            tag == LexiconTag.None
                ? 0
                : 15UL << (BitOperations.TrailingZeroCount((uint)tag) * 4));
        return tag != LexiconTag.None;
    }}
}}
'''
    return source.encode("utf-8")


def write_or_check(path: Path, content: bytes, check: bool) -> bool:
    if path.exists() and path.read_bytes() == content:
        return False
    if check:
        print(f"stale: {path.relative_to(REPOSITORY_ROOT)}")
        return True
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(content)
    print(f"wrote: {path.relative_to(REPOSITORY_ROOT)}")
    return False


def main() -> int:
    args = parse_args()
    counts, sources = load_counts(args.no_download)
    if len(counts) < MINIMUM_WORD_COUNT:
        raise RuntimeError(
            f"Generated {len(counts):,} words; expected at least {MINIMUM_WORD_COUNT:,}."
        )
    asset, ambiguous = build_asset(counts)
    digest = hashlib.sha256(asset).hexdigest()
    source = build_source(len(counts), ambiguous, digest)
    stale = write_or_check(OUTPUT_ASSET, asset, args.check)
    stale |= write_or_check(OUTPUT_SOURCE, source, args.check)
    print(
        f"lexicon: {len(counts):,} words, {ambiguous:,} ambiguous, "
        f"{len(asset):,} compressed bytes, sha256={digest}"
    )
    print("sources: " + ", ".join(f"{key}={value:,}" for key, value in sources.items()))
    return 1 if stale else 0


if __name__ == "__main__":
    raise SystemExit(main())
