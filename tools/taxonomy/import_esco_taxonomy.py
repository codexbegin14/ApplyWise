#!/usr/bin/env python3
"""Convert an ESCO-style CSV export into ApplyWise's compact taxonomy JSON.

The importer intentionally uses only the Python standard library and performs no
network access. Source exports and their licensing metadata must be supplied by
the operator.
"""

from __future__ import annotations

import argparse
import csv
import json
import os
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable
from urllib.parse import unquote, urlparse


SCHEMA_VERSION = 1
_ID_PART = re.compile(r"^[A-Za-z0-9._-]+$")


class TaxonomyImportError(ValueError):
    """Raised when source data cannot be converted without guessing."""


@dataclass
class ImportedEntry:
    canonical_id: str
    preferred_label: str
    category: str
    first_line: int
    aliases: dict[str, str] = field(default_factory=dict)
    ambiguous_aliases: dict[str, str] = field(default_factory=dict)
    ambiguity_note: str = ""

    def add_aliases(self, values: Iterable[str]) -> None:
        label_key = self.preferred_label.casefold()
        for value in values:
            key = value.casefold()
            if key and key != label_key:
                self.aliases.setdefault(key, value)

    def add_ambiguous_aliases(self, values: Iterable[str]) -> None:
        for value in values:
            key = value.casefold()
            if key:
                self.ambiguous_aliases.setdefault(key, value)


def clean(value: object) -> str:
    """Normalize surrounding and repeated whitespace without changing meaning."""
    return " ".join(str(value or "").strip().split())


def split_values(value: object, separator: str) -> list[str]:
    """Split pipe-style or multiline CSV cells and deduplicate case-insensitively."""
    raw = str(value or "").replace("\r\n", "\n").replace("\r", "\n")
    parts: list[str] = []
    for line in raw.split("\n"):
        parts.extend(line.split(separator))

    unique: dict[str, str] = {}
    for part in parts:
        normalized = clean(part)
        if normalized:
            unique.setdefault(normalized.casefold(), normalized)
    return list(unique.values())


def canonicalize_id(raw_id: object, namespace: str) -> str:
    """Create a stable compact ID from a source ID or concept URI."""
    value = clean(raw_id)
    if not value:
        raise TaxonomyImportError("source concept ID is empty")

    prefix = f"{namespace}:"
    if value.casefold().startswith(prefix.casefold()):
        identifier = value[len(prefix) :]
    elif "://" in value:
        parsed = urlparse(value)
        identifier = unquote(parsed.fragment or parsed.path.rstrip("/").rsplit("/", 1)[-1])
    else:
        identifier = value

    identifier = identifier.strip()
    if not identifier or not _ID_PART.fullmatch(identifier):
        raise TaxonomyImportError(
            f"source concept ID {value!r} does not end in a safe stable identifier"
        )
    return f"{namespace}:{identifier}"


def _row_value(row: dict[str, str], column: str) -> str:
    return clean(row.get(column, ""))


def read_entries(args: argparse.Namespace) -> list[ImportedEntry]:
    entries: dict[str, ImportedEntry] = {}

    try:
        source = args.input.open("r", encoding="utf-8-sig", newline="")
    except OSError as exc:
        raise TaxonomyImportError(f"cannot open input CSV: {exc}") from exc

    with source:
        reader = csv.DictReader(source, delimiter=args.delimiter)
        headers = set(reader.fieldnames or [])
        required = {args.id_column, args.label_column}
        missing = sorted(required - headers)
        if missing:
            raise TaxonomyImportError(
                "input CSV is missing required column(s): " + ", ".join(missing)
            )

        for line_number, row in enumerate(reader, start=2):
            if not any(clean(value) for value in row.values() if value is not None):
                continue

            try:
                canonical_id = canonicalize_id(row.get(args.id_column), args.namespace)
            except TaxonomyImportError as exc:
                raise TaxonomyImportError(f"line {line_number}: {exc}") from exc

            label = _row_value(row, args.label_column)
            if not label:
                raise TaxonomyImportError(f"line {line_number}: preferred label is empty")

            category = _row_value(row, args.category_column) or args.default_category
            aliases = split_values(row.get(args.aliases_column), args.aliases_separator)
            ambiguous = split_values(
                row.get(args.ambiguity_aliases_column), args.aliases_separator
            )
            note = _row_value(row, args.ambiguity_note_column)

            existing = entries.get(canonical_id)
            if existing is None:
                existing = ImportedEntry(canonical_id, label, category, line_number)
                entries[canonical_id] = existing
            elif existing.preferred_label != label or existing.category != category:
                raise TaxonomyImportError(
                    f"line {line_number}: duplicate ID {canonical_id!r} conflicts with "
                    f"line {existing.first_line}"
                )

            existing.add_aliases(aliases)
            existing.add_ambiguous_aliases(ambiguous)
            if note:
                if existing.ambiguity_note and existing.ambiguity_note != note:
                    raise TaxonomyImportError(
                        f"line {line_number}: duplicate ID {canonical_id!r} has conflicting "
                        "ambiguity notes"
                    )
                existing.ambiguity_note = note

    if not entries:
        raise TaxonomyImportError("input CSV contains no skill rows")

    _mark_shared_surfaces_ambiguous(entries.values())
    _validate_explicit_ambiguity(entries.values())
    return sorted(entries.values(), key=lambda item: item.canonical_id.casefold())


def _mark_shared_surfaces_ambiguous(entries: Iterable[ImportedEntry]) -> None:
    """Flag labels/aliases that resolve to more than one canonical skill."""
    entries = list(entries)
    owners: dict[str, set[str]] = {}
    surfaces: dict[tuple[str, str], str] = {}

    for entry in entries:
        values = [entry.preferred_label, *entry.aliases.values()]
        for value in values:
            key = value.casefold()
            owners.setdefault(key, set()).add(entry.canonical_id)
            surfaces[(entry.canonical_id, key)] = value

    by_id = {entry.canonical_id: entry for entry in entries}
    for key, canonical_ids in owners.items():
        if len(canonical_ids) < 2:
            continue
        for canonical_id in canonical_ids:
            entry = by_id[canonical_id]
            entry.ambiguous_aliases.setdefault(key, surfaces[(canonical_id, key)])
            if not entry.ambiguity_note:
                entry.ambiguity_note = "Alias is shared by multiple canonical skills."


def _validate_explicit_ambiguity(entries: Iterable[ImportedEntry]) -> None:
    for entry in entries:
        known = {entry.preferred_label.casefold(), *entry.aliases.keys()}
        unknown = sorted(
            value
            for key, value in entry.ambiguous_aliases.items()
            if key not in known
        )
        if unknown:
            raise TaxonomyImportError(
                f"line {entry.first_line}: ambiguous alias(es) for {entry.canonical_id!r} "
                f"are not its label or aliases: {', '.join(unknown)}"
            )


def build_document(entries: list[ImportedEntry], args: argparse.Namespace) -> dict[str, object]:
    source: dict[str, str] = {
        "name": args.source_name,
        "version": args.source_version,
        "license": args.source_license,
    }
    if args.source_url:
        source["url"] = args.source_url

    output_entries: list[dict[str, object]] = []
    for entry in entries:
        ambiguity: dict[str, object] = {
            "requiresContext": bool(entry.ambiguous_aliases),
            "aliases": sorted(entry.ambiguous_aliases.values(), key=str.casefold),
        }
        if entry.ambiguity_note:
            ambiguity["note"] = entry.ambiguity_note

        output_entries.append(
            {
                "id": entry.canonical_id,
                "preferredLabel": entry.preferred_label,
                "aliases": sorted(entry.aliases.values(), key=str.casefold),
                "category": entry.category,
                "ambiguity": ambiguity,
            }
        )

    return {
        "schemaVersion": SCHEMA_VERSION,
        "taxonomyVersion": args.taxonomy_version,
        "source": source,
        "entries": output_entries,
    }


def serialize(document: dict[str, object]) -> str:
    return json.dumps(document, ensure_ascii=False, indent=2) + "\n"


def write_or_check(output: Path, payload: str, check: bool) -> None:
    if check:
        try:
            existing = output.read_text(encoding="utf-8")
        except OSError as exc:
            raise TaxonomyImportError(f"cannot read check output: {exc}") from exc
        if existing != payload:
            raise TaxonomyImportError(
                f"generated taxonomy differs from {output}; rerun without --check to refresh it"
            )
        print(f"Taxonomy is up to date: {output}")
        return

    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(f".{output.name}.{os.getpid()}.tmp")
    try:
        temporary.write_text(payload, encoding="utf-8", newline="\n")
        os.replace(temporary, output)
    except OSError as exc:
        try:
            temporary.unlink(missing_ok=True)
        except OSError:
            pass
        raise TaxonomyImportError(f"cannot write output JSON: {exc}") from exc
    print(f"Wrote taxonomy: {output}")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", type=Path, help="local ESCO-style CSV export")
    parser.add_argument("output", type=Path, help="taxonomy JSON to write or check")
    parser.add_argument("--taxonomy-version", required=True)
    parser.add_argument("--source-name", default="ESCO")
    parser.add_argument("--source-version", required=True)
    parser.add_argument("--source-license", required=True)
    parser.add_argument("--source-url", default="")
    parser.add_argument("--namespace", default="esco")
    parser.add_argument("--delimiter", default=",")
    parser.add_argument("--aliases-separator", default="|")
    parser.add_argument("--id-column", default="conceptUri")
    parser.add_argument("--label-column", default="preferredLabel")
    parser.add_argument("--aliases-column", default="altLabels")
    parser.add_argument("--category-column", default="category")
    parser.add_argument("--ambiguity-aliases-column", default="ambiguityAliases")
    parser.add_argument("--ambiguity-note-column", default="ambiguityNote")
    parser.add_argument("--default-category", default="Uncategorized")
    parser.add_argument(
        "--check",
        action="store_true",
        help="compare generated content with output instead of writing it",
    )
    args = parser.parse_args(argv)

    if len(args.delimiter) != 1:
        parser.error("--delimiter must be exactly one character")
    if not args.aliases_separator:
        parser.error("--aliases-separator cannot be empty")
    if not _ID_PART.fullmatch(args.namespace):
        parser.error("--namespace may contain only letters, digits, dot, underscore, or hyphen")
    for attribute in (
        "taxonomy_version",
        "source_name",
        "source_version",
        "source_license",
        "default_category",
    ):
        value = clean(getattr(args, attribute))
        if not value:
            parser.error(f"--{attribute.replace('_', '-')} cannot be empty")
        setattr(args, attribute, value)
    args.source_url = clean(args.source_url)
    return args


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    try:
        entries = read_entries(args)
        document = build_document(entries, args)
        write_or_check(args.output, serialize(document), args.check)
    except TaxonomyImportError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
