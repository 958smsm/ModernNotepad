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
    "Quantifier": 20,
    "Preposition": 30,
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
    if pos in {"DET", "NUM"}:
        return "Quantifier"
    if pos in {"NOUN", "PROPN"}:
        if dep in {"nsubj", "nsubjpass", "csubj", "csubjpass", "attr"}:
            return "SubjectNoun"
        return "ObjectNoun"
    return "Other"


def analyze_spacy(text: str, input_tokens: list[dict[str, Any]]) -> list[str]:
    nlp = _load_spacy()
    doc = nlp(text)
    boundary_map = _utf16_boundary_map(text)

    question_spans: list[tuple[int, int]] = []
    try:
        for sentence in doc.sents:
            if sentence.text.rstrip().endswith("?"):
                question_spans.append((sentence.start_char, sentence.end_char))
    except ValueError:
        pass

    assignments: list[str] = []
    doc_tokens = list(doc)
    cursor = 0
    for input_token in input_tokens:
        start, end = _py_span(input_token, boundary_map)
        while cursor < len(doc_tokens) and doc_tokens[cursor].idx + len(doc_tokens[cursor].text) <= start:
            cursor += 1

        categories: list[str] = []
        index = cursor
        while index < len(doc_tokens) and doc_tokens[index].idx < end:
            candidate = doc_tokens[index]
            candidate_end = candidate.idx + len(candidate.text)
            if candidate_end > start:
                is_question = any(q_start <= candidate.idx < q_end for q_start, q_end in question_spans)
                categories.append(_spacy_category(candidate, is_question))
            index += 1
        assignments.append(_choose(categories))

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
        gap = text[previous_end:start] if current else text[:start]
        if current and any(mark in gap for mark in ".?!"):
            groups.append((current, sentence_question or "?" in gap))
            current = []
            sentence_question = False
        current.append(index)
        previous_end = end

        next_start = len(text)
        if index + 1 < len(tokens):
            next_start, _ = _py_span(tokens[index + 1], boundary_map)
        trailing = text[end:next_start]
        sentence_question = sentence_question or "?" in trailing
        if any(mark in trailing for mark in ".?!"):
            groups.append((current, sentence_question))
            current = []
            sentence_question = False

    if current:
        groups.append((current, sentence_question or "?" in text[previous_end:]))
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
    if tag == "TO":
        return "Preposition"
    if tag in {"DT", "PDT", "WDT", "CD"}:
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

    for indexes, is_question in groups:
        words = [str(input_tokens[index]["text"]) for index in indexes]
        try:
            tagged = nltk.pos_tag(words, lang="eng")
        except LookupError as exc:
            raise RuntimeError(
                "NLTK's English POS tagger data is missing. Run scripts/setup-grammar-providers.ps1 or "
                "python -m nltk.downloader averaged_perceptron_tagger_eng."
            ) from exc

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
