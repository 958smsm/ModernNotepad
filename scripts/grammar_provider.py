#!/usr/bin/env python3
"""Modern Notepad Python grammar worker.

The worker is intentionally a small, local bridge. It receives the editor's
word tokens from the .NET process and returns one GrammarCategory per token.
It supports Windows named pipes and named shared memory without third-party IPC
packages; spaCy or NLTK is loaded lazily only after the IPC channel is open.
"""

from __future__ import annotations

import argparse
import json
import mmap
import os
import struct
import sys
import time
import traceback
from typing import Any, Callable

INTERROGATIVES = {"who", "whom", "what", "which", "whose", "where", "when", "why", "how"}
CONJUNCTIONS = {
    "and", "but", "or", "nor", "for", "yet", "so", "although", "because", "since", "unless",
    "while", "whereas", "if", "when", "whenever", "where", "wherever", "whether", "than", "though",
}
BE_FORMS = {"am", "is", "are", "was", "were", "be", "being", "been"}

CATEGORY_PRIORITY = {
    "Other": 0,
    "Determiner": 20,
    "Quantifier": 22,
    "Preposition": 30,
    "Particle": 32,
    "Conjunction": 35,
    "Adverb": 40,
    "Adjective": 45,
    "Pronoun": 50,
    "ObjectNoun": 60,
    "SubjectNoun": 70,
    "Verb": 80,
    "Interrogative": 90,
}

_spacy_nlp = None
_nltk_module = None


def _utf16_boundary_map(text: str) -> list[int]:
    """Map UTF-16 code-unit boundaries (used by .NET) to Python string indexes."""
    total_units = len(text.encode("utf-16-le")) // 2
    mapping = [0] * (total_units + 1)
    unit = 0
    for index, char in enumerate(text):
        width = 2 if ord(char) > 0xFFFF else 1
        mapping[unit] = index
        if width == 2:
            mapping[unit + 1] = index
        unit += width
        mapping[unit] = index + 1
    return mapping


def _py_span(token: dict[str, Any], boundary_map: list[int]) -> tuple[int, int]:
    start16 = max(0, int(token["start"]))
    end16 = max(start16, start16 + int(token["length"]))
    max_boundary = len(boundary_map) - 1
    start16 = min(start16, max_boundary)
    end16 = min(end16, max_boundary)
    return boundary_map[start16], boundary_map[end16]


def _choose(categories: list[str]) -> str:
    if not categories:
        return "Other"
    return max(categories, key=lambda item: CATEGORY_PRIORITY.get(item, 0))


def _load_spacy():
    global _spacy_nlp
    if _spacy_nlp is not None:
        return _spacy_nlp

    try:
        import spacy
    except ImportError as exc:
        raise RuntimeError(
            "spaCy is not installed. Run scripts/setup-grammar-providers.ps1 or install spaCy in the configured Python environment."
        ) from exc

    model_name = os.environ.get("MODERNNOTEPAD_SPACY_MODEL", "en_core_web_sm").strip() or "en_core_web_sm"
    try:
        _spacy_nlp = spacy.load(model_name)
    except OSError as exc:
        raise RuntimeError(
            f"spaCy model '{model_name}' is not installed. Run: python -m spacy download {model_name}"
        ) from exc
    return _spacy_nlp


def _spacy_noun_is_subject(token) -> bool:
    subject_deps = {"nsubj", "nsubjpass", "csubj", "csubjpass", "attr"}
    dep = token.dep_.lower()
    if dep in subject_deps:
        return True
    if dep != "conj":
        return False

    # Coordinated subjects usually attach with dep=conj to the first subject.
    head = token.head
    for _ in range(4):
        if head is token:
            break
        head_dep = head.dep_.lower()
        if head_dep in subject_deps:
            return True
        if head_dep != "conj" or head.head is head:
            break
        head = head.head
    return False


def _spacy_category(token, is_question: bool) -> str:
    word = token.text.casefold()
    if is_question and word in INTERROGATIVES:
        return "Interrogative"

    pos = token.pos_
    dep = token.dep_.lower()
    if pos in {"VERB", "AUX"}:
        return "Verb"
    if pos == "ADJ":
        return "Adjective"
    if pos == "ADV":
        return "Adverb"
    if pos == "PRON":
        return "Pronoun"
    if pos == "ADP":
        return "Preposition"
    if pos in {"CCONJ", "SCONJ"}:
        return "Conjunction"
    if pos == "DET":
        return "Determiner"
    if pos == "NUM":
        return "Quantifier"
    if pos == "PART":
        return "Particle"
    if pos in {"NOUN", "PROPN"}:
        if _spacy_noun_is_subject(token):
            return "SubjectNoun"
        return "ObjectNoun"
    return "Other"


def _looks_like_sentence_boundary(text: str, index: int) -> bool:
    if index < 0 or index >= len(text) or text[index] not in ".!?":
        return False
    if index + 1 < len(text) and not text[index + 1].isspace():
        return False
    if text[index] == ".":
        if index > 0 and index + 1 < len(text) and text[index - 1].isdigit() and text[index + 1].isdigit():
            return False
        word_start = index
        while word_start > 0 and text[word_start - 1].isalpha():
            word_start -= 1
        abbreviation = text[word_start:index].casefold()
        if abbreviation in {
            "mr", "mrs", "ms", "dr", "prof", "rev", "gov", "sen", "rep", "gen", "sgt", "lt", "col",
            "capt", "sr", "jr", "st", "mt", "jan", "feb", "mar", "apr", "jun", "jul", "aug", "sep",
            "sept", "oct", "nov", "dec",
        }:
            return False
    return True


def _iter_safe_text_chunks(text: str, max_chars: int):
    """Yield (global_start, text_chunk), preferring paragraph/sentence boundaries."""
    if not text:
        return

    max_chars = max(1_000, max_chars)
    start = 0
    length = len(text)
    while start < length:
        target = min(length, start + max_chars)
        if target >= length:
            yield start, text[start:]
            return

        floor = start + max_chars // 2
        cut = -1

        # Paragraph boundaries are safest and cheapest to identify.
        paragraph = text.rfind("\n\n", floor, target)
        if paragraph >= floor:
            cut = paragraph + 2

        if cut < 0:
            cursor = target - 1
            while cursor >= floor:
                if _looks_like_sentence_boundary(text, cursor):
                    cut = cursor + 1
                    while cut < length and text[cut].isspace():
                        cut += 1
                    break
                cursor -= 1

        # A single pathological sentence can exceed the chunk target. Split only
        # as a last resort, and only at whitespace so no lexical token is bisected.
        if cut <= start:
            cut = target
            while cut > floor and not text[cut - 1].isspace():
                cut -= 1
            if cut <= floor:
                cut = target
                while cut < length and not text[cut].isspace():
                    cut += 1

        yield start, text[start:cut]
        start = cut


def analyze_spacy(text: str, input_tokens: list[dict[str, Any]]) -> list[str]:
    nlp = _load_spacy()
    boundary_map = _utf16_boundary_map(text)
    input_spans = [_py_span(token, boundary_map) for token in input_tokens]
    assignments = ["Other"] * len(input_tokens)

    configured_chunk = os.environ.get("MODERNNOTEPAD_SPACY_CHUNK_CHARS", "200000").strip()
    try:
        chunk_chars = int(configured_chunk)
    except ValueError as exc:
        raise RuntimeError("MODERNNOTEPAD_SPACY_CHUNK_CHARS must be an integer.") from exc

    # Stay below Language.max_length. The default is 1,000,000 characters, but
    # custom pipelines can set a lower value.
    max_length = max(1_001, int(getattr(nlp, "max_length", 1_000_000)))
    chunk_chars = min(max(1_000, chunk_chars), max_length - 1)
    chunks = list(_iter_safe_text_chunks(text, chunk_chars))
    if not chunks:
        return assignments

    batch_size_text = os.environ.get("MODERNNOTEPAD_SPACY_PIPE_BATCH", "4").strip()
    try:
        batch_size = max(1, int(batch_size_text))
    except ValueError as exc:
        raise RuntimeError("MODERNNOTEPAD_SPACY_PIPE_BATCH must be an integer.") from exc

    input_cursor = 0
    docs = nlp.pipe((chunk_text for _, chunk_text in chunks), batch_size=batch_size)
    for (chunk_start, chunk_text), doc in zip(chunks, docs):
        chunk_end = chunk_start + len(chunk_text)
        doc_tokens = list(doc)
        question_flags = [False] * len(doc_tokens)
        try:
            for sentence in doc.sents:
                if not sentence.text.rstrip().endswith("?"):
                    continue
                for sentence_token in sentence:
                    if 0 <= sentence_token.i < len(question_flags):
                        question_flags[sentence_token.i] = True
        except ValueError:
            pass

        doc_cursor = 0
        while input_cursor < len(input_tokens) and input_spans[input_cursor][1] <= chunk_start:
            input_cursor += 1

        while input_cursor < len(input_tokens):
            global_start, global_end = input_spans[input_cursor]
            if global_start >= chunk_end:
                break

            local_start = max(0, global_start - chunk_start)
            local_end = min(len(chunk_text), global_end - chunk_start)
            while doc_cursor < len(doc_tokens) and doc_tokens[doc_cursor].idx + len(doc_tokens[doc_cursor].text) <= local_start:
                doc_cursor += 1

            categories: list[str] = []
            candidate_index = doc_cursor
            while candidate_index < len(doc_tokens) and doc_tokens[candidate_index].idx < local_end:
                candidate = doc_tokens[candidate_index]
                candidate_end = candidate.idx + len(candidate.text)
                if candidate_end > local_start:
                    is_question = question_flags[candidate.i] if candidate.i < len(question_flags) else False
                    categories.append(_spacy_category(candidate, is_question))
                candidate_index += 1

            assignments[input_cursor] = _choose(categories)
            input_cursor += 1

    return assignments


def _load_nltk():
    global _nltk_module
    if _nltk_module is not None:
        return _nltk_module
    try:
        import nltk
    except ImportError as exc:
        raise RuntimeError(
            "NLTK is not installed. Run scripts/setup-grammar-providers.ps1 or install nltk in the configured Python environment."
        ) from exc
    _nltk_module = nltk
    return nltk


def _boundary_marks_in_range(text: str, start: int, end: int) -> tuple[bool, bool]:
    has_boundary = False
    has_question = False
    for index in range(max(0, start), min(len(text), end)):
        if text[index] == "?" and _looks_like_sentence_boundary(text, index):
            has_question = True
            has_boundary = True
        elif text[index] in ".!" and _looks_like_sentence_boundary(text, index):
            has_boundary = True
    return has_boundary, has_question


def _split_input_sentences(text: str, tokens: list[dict[str, Any]]) -> list[tuple[list[int], bool]]:
    if not tokens:
        return []
    boundary_map = _utf16_boundary_map(text)
    groups: list[tuple[list[int], bool]] = []
    current: list[int] = []
    previous_end = 0
    sentence_question = False

    for index, token in enumerate(tokens):
        start, end = _py_span(token, boundary_map)
        gap_start = previous_end if current else 0
        gap_has_boundary, gap_has_question = _boundary_marks_in_range(text, gap_start, start)
        if current and gap_has_boundary:
            groups.append((current, sentence_question or gap_has_question))
            current = []
            sentence_question = False
        current.append(index)
        previous_end = end

        next_start = len(text)
        if index + 1 < len(tokens):
            next_start, _ = _py_span(tokens[index + 1], boundary_map)
        trailing_has_boundary, trailing_has_question = _boundary_marks_in_range(text, end, next_start)
        sentence_question = sentence_question or trailing_has_question
        if trailing_has_boundary:
            groups.append((current, sentence_question))
            current = []
            sentence_question = False

    if current:
        _, final_question = _boundary_marks_in_range(text, previous_end, len(text))
        groups.append((current, sentence_question or final_question))
    return groups


def _nltk_category(
    word: str,
    tag: str,
    position: int,
    first_verb_position: int | None,
    tagged_sentence: list[tuple[str, str]],
    is_question: bool,
) -> str:
    lower = word.casefold()
    if is_question and lower in INTERROGATIVES and tag in {"WP", "WP$", "WDT", "WRB"}:
        return "Interrogative"
    if tag.startswith("VB") or tag == "MD":
        return "Verb"
    if tag.startswith("JJ"):
        return "Adjective"
    if tag.startswith("RB") or tag == "WRB":
        return "Adverb"
    if tag.startswith("PRP") or tag in {"WP", "WP$"}:
        return "Pronoun"
    if tag == "CC":
        return "Conjunction"
    if tag == "IN":
        return "Conjunction" if lower in CONJUNCTIONS else "Preposition"
    if tag in {"TO", "RP"}:
        return "Particle"
    if tag in {"DT", "PDT", "WDT"}:
        return "Determiner"
    if tag == "CD":
        return "Quantifier"
    if tag.startswith("NN"):
        if first_verb_position is None or position < first_verb_position:
            return "SubjectNoun"
        previous_word = tagged_sentence[position - 1][0].casefold() if position > 0 else ""
        if previous_word in BE_FORMS:
            return "SubjectNoun"
        return "ObjectNoun"
    return "Other"


def analyze_nltk(text: str, input_tokens: list[dict[str, Any]]) -> list[str]:
    nltk = _load_nltk()
    assignments = ["Other"] * len(input_tokens)
    groups = _split_input_sentences(text, input_tokens)

    configured_batch = os.environ.get("MODERNNOTEPAD_NLTK_SENTENCE_BATCH", "256").strip()
    try:
        sentence_batch = max(1, int(configured_batch))
    except ValueError as exc:
        raise RuntimeError("MODERNNOTEPAD_NLTK_SENTENCE_BATCH must be an integer.") from exc

    for batch_start in range(0, len(groups), sentence_batch):
        batch = groups[batch_start:batch_start + sentence_batch]
        sentence_words = [
            [str(input_tokens[index]["text"]) for index in indexes]
            for indexes, _ in batch
        ]
        try:
            tagged_batch = nltk.pos_tag_sents(sentence_words, lang="eng")
        except LookupError as exc:
            raise RuntimeError(
                "NLTK's English POS tagger data is missing. Run scripts/setup-grammar-providers.ps1 or "
                "python -m nltk.downloader averaged_perceptron_tagger_eng."
            ) from exc

        for (indexes, is_question), tagged in zip(batch, tagged_batch):
            first_verb = next(
                (position for position, (_, tag) in enumerate(tagged) if tag.startswith("VB") or tag == "MD"),
                None,
            )
            for position, token_index in enumerate(indexes):
                word, tag = tagged[position]
                assignments[token_index] = _nltk_category(
                    word, tag, position, first_verb, tagged, is_question
                )

    return assignments


def process_request(engine: str, payload: bytes) -> bytes:
    try:
        request = json.loads(payload.decode("utf-8"))
        text = request.get("text")
        tokens = request.get("tokens")
        if not isinstance(text, str) or not isinstance(tokens, list):
            raise ValueError("Request must contain text and tokens.")

        if engine == "spacy":
            assignments = analyze_spacy(text, tokens)
        elif engine == "nltk":
            assignments = analyze_nltk(text, tokens)
        else:
            raise ValueError(f"Unknown grammar engine: {engine}")

        if len(assignments) != len(tokens):
            raise RuntimeError(
                f"Provider classified {len(assignments)} of {len(tokens)} tokens."
            )
        response = {"ok": True, "assignments": assignments}
    except Exception as exc:  # Keep the worker alive so the .NET fallback can report the failure.
        details = f"{type(exc).__name__}: {exc}\n{traceback.format_exc(limit=8)}"
        response = {"ok": False, "error": details[:16000]}

    return json.dumps(response, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def _open_windows_named_pipe(pipe_name: str):
    if os.name != "nt":
        raise RuntimeError("Named-pipe grammar transport requires Windows.")

    import ctypes
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateFileW.argtypes = [
        wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
        wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE,
    ]
    kernel32.CreateFileW.restype = wintypes.HANDLE
    kernel32.WaitNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD]
    kernel32.WaitNamedPipeW.restype = wintypes.BOOL
    kernel32.ReadFile.argtypes = [
        wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID,
    ]
    kernel32.ReadFile.restype = wintypes.BOOL
    kernel32.WriteFile.argtypes = [
        wintypes.HANDLE, wintypes.LPCVOID, wintypes.DWORD,
        ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID,
    ]
    kernel32.WriteFile.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    path = rf"\\.\pipe\{pipe_name}"
    GENERIC_READ = 0x80000000
    GENERIC_WRITE = 0x40000000
    OPEN_EXISTING = 3
    ERROR_PIPE_BUSY = 231
    INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

    deadline = time.monotonic() + 15.0
    while True:
        handle = kernel32.CreateFileW(
            path, GENERIC_READ | GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None
        )
        if handle != INVALID_HANDLE_VALUE:
            break
        error = ctypes.get_last_error()
        if error != ERROR_PIPE_BUSY or time.monotonic() >= deadline:
            raise OSError(error, f"Could not connect to named pipe {path}")
        kernel32.WaitNamedPipeW(path, 500)

    def read_exact(size: int) -> bytes:
        chunks: list[bytes] = []
        remaining = size
        while remaining:
            buffer = ctypes.create_string_buffer(remaining)
            read = wintypes.DWORD()
            if not kernel32.ReadFile(handle, buffer, remaining, ctypes.byref(read), None):
                error = ctypes.get_last_error()
                raise OSError(error, "Named pipe read failed")
            if read.value == 0:
                raise EOFError("Named pipe closed")
            chunks.append(buffer.raw[: read.value])
            remaining -= read.value
        return b"".join(chunks)

    def write_all(data: bytes) -> None:
        offset = 0
        while offset < len(data):
            chunk = data[offset:]
            buffer = ctypes.create_string_buffer(chunk)
            written = wintypes.DWORD()
            if not kernel32.WriteFile(handle, buffer, len(chunk), ctypes.byref(written), None):
                error = ctypes.get_last_error()
                raise OSError(error, "Named pipe write failed")
            if written.value == 0:
                raise EOFError("Named pipe closed")
            offset += written.value

    def close() -> None:
        kernel32.CloseHandle(handle)

    return read_exact, write_all, close


def run_named_pipe(engine: str, pipe_name: str) -> None:
    read_exact, write_all, close = _open_windows_named_pipe(pipe_name)
    try:
        while True:
            try:
                length_bytes = read_exact(4)
            except EOFError:
                return
            length = struct.unpack("<i", length_bytes)[0]
            if length <= 0 or length > 64 * 1024 * 1024:
                raise ValueError(f"Invalid named-pipe frame length: {length}")
            request = read_exact(length)
            response = process_request(engine, request)
            write_all(struct.pack("<i", len(response)))
            write_all(response)
    finally:
        close()


def run_shared_memory(engine: str, mapping_name: str, size: int) -> None:
    if os.name != "nt":
        raise RuntimeError("Named shared-memory grammar transport requires Windows.")
    if size <= 16:
        raise ValueError("Shared-memory mapping is too small.")

    STATE_OFFSET = 0
    REQUEST_LENGTH_OFFSET = 4
    RESPONSE_LENGTH_OFFSET = 8
    PAYLOAD_OFFSET = 16
    STATE_REQUEST = 1
    STATE_RESPONSE = 2

    with mmap.mmap(-1, size, tagname=mapping_name, access=mmap.ACCESS_WRITE) as memory:
        while True:
            state = struct.unpack_from("<i", memory, STATE_OFFSET)[0]
            if state == 3:
                return
            if state != STATE_REQUEST:
                time.sleep(0.01)
                continue

            request_length = struct.unpack_from("<i", memory, REQUEST_LENGTH_OFFSET)[0]
            if request_length <= 0 or request_length > size - PAYLOAD_OFFSET:
                response = json.dumps(
                    {"ok": False, "error": f"Invalid shared-memory request length: {request_length}"},
                    separators=(",", ":"),
                ).encode("utf-8")
            else:
                request = bytes(memory[PAYLOAD_OFFSET : PAYLOAD_OFFSET + request_length])
                response = process_request(engine, request)

            if len(response) > size - PAYLOAD_OFFSET:
                response = json.dumps(
                    {"ok": False, "error": "Grammar response exceeds shared-memory capacity."},
                    separators=(",", ":"),
                ).encode("utf-8")
            memory[PAYLOAD_OFFSET : PAYLOAD_OFFSET + len(response)] = response
            struct.pack_into("<i", memory, RESPONSE_LENGTH_OFFSET, len(response))
            struct.pack_into("<i", memory, STATE_OFFSET, STATE_RESPONSE)
            memory.flush()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Modern Notepad Python grammar worker")
    parser.add_argument("--engine", choices=("spacy", "nltk"), required=True)
    parser.add_argument("--transport", choices=("named-pipe", "shared-memory"), required=True)
    parser.add_argument("--pipe")
    parser.add_argument("--mapping")
    parser.add_argument("--size", type=int)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.transport == "named-pipe":
        if not args.pipe:
            raise ValueError("--pipe is required for named-pipe transport")
        run_named_pipe(args.engine, args.pipe)
    else:
        if not args.mapping or not args.size:
            raise ValueError("--mapping and --size are required for shared-memory transport")
        run_shared_memory(args.engine, args.mapping, args.size)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(130)
