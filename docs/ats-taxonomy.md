# ATS skill taxonomy import and maintenance

ApplyWise keeps its runtime skill extraction deterministic and locally testable. The
offline importer in `tools/taxonomy/import_esco_taxonomy.py` converts an operator-
supplied ESCO-style CSV into a compact, versioned JSON artifact. It never downloads
data and uses only the Python standard library.

The checked-in CSV and JSON under `tools/taxonomy/fixtures/` are synthetic test data.
They are not an ESCO redistribution and must not be treated as an official release.

## Data contract

`tools/taxonomy/taxonomy.schema.json` is the machine-readable v1 contract. Its core
fields are:

- `schemaVersion`: shape of the JSON document. Increment this only for a breaking
  structural change.
- `taxonomyVersion`: ApplyWise's curated dataset release. Use a deliberate release
  identifier such as `2026.07.0`.
- `source`: upstream name, upstream version, exact license identifier, and optional
  release URL.
- `entries[].id`: stable canonical ID, normally `esco:<source-id>`.
- `entries[].preferredLabel`, `aliases`, and `category`: the normalized matching and
  grouping data.
- `entries[].ambiguity`: whether a label or alias needs contextual matching, the
  affected surfaces, and an optional reviewer note.

IDs are derived from the final segment of a concept URI, not from the display label.
Renaming a skill therefore does not silently create a new identity. Output entries and
aliases are sorted, timestamps are omitted, and writes are atomic so identical input
and options produce identical bytes.

## CSV expectations

The defaults expect these headers:

| Header | Required | Meaning |
| --- | --- | --- |
| `conceptUri` | yes | Stable source ID or concept URI |
| `preferredLabel` | yes | Canonical display label |
| `altLabels` | no | Pipe-separated or multiline aliases |
| `category` | no | Curated ApplyWise category |
| `ambiguityAliases` | no | Label/aliases that require context |
| `ambiguityNote` | no | Short reason or matching guidance |

Column names, delimiter, alias separator, namespace, and default category are CLI
options because upstream exports can differ by release and locale. The importer does
not guess how upstream skill families map to ApplyWise categories. Make that mapping
explicit in the prepared CSV or review the default category before publishing.

The importer rejects missing IDs/labels, unsafe IDs, conflicting duplicate IDs,
conflicting ambiguity notes, and ambiguity terms that are not a label or alias. It
also marks a surface ambiguous when the same case-insensitive label or alias belongs
to more than one canonical ID.

## Local use

Run the fixture check from the repository root:

```powershell
python -m unittest tools/taxonomy/test_import_esco_taxonomy.py
```

Or verify the fixture artifact directly without changing it:

```powershell
python tools/taxonomy/import_esco_taxonomy.py `
  tools/taxonomy/fixtures/esco-skills.csv `
  tools/taxonomy/fixtures/expected-taxonomy.json `
  --taxonomy-version demo-1 `
  --source-name "ESCO-style fixture" `
  --source-version demo-1 `
  --source-license CC0-1.0 `
  --source-url https://example.com/esco-style-fixture `
  --check
```

For a real local export, remove `--check` and choose a reviewable output path. Use
`--help` to map release-specific columns. No source CSV needs to be committed.

## Curated fallback and runtime loading

The in-code fallback remains the small, reviewed emergency baseline. ApplyWise can
load a reviewed artifact once at startup by setting `SkillTaxonomy:ArtifactPath` to
the generated JSON path (relative paths are resolved from the web app content root).
No taxonomy file is downloaded or reloaded in the analysis request path. Loading
fails closed to the fallback if the configured artifact is absent, oversized,
malformed, has an unknown `schemaVersion`, contains duplicate IDs, or fails runtime
validation. The loaded taxonomy version is included in the SHA-256 analysis cache
key so a dataset refresh cannot reuse stale scoring results.

Before promoting a generated file into runtime configuration, add regression tests for
canonicalization, phrase boundaries, aliases such as `Go`, `R`, and `JS`, category
behavior, and deterministic scoring. Treat category changes and newly ambiguous terms
as behavior changes, not data-only housekeeping.

## Refresh procedure

1. Obtain the desired upstream release manually from its official publisher and keep
   the raw export outside the repository unless redistribution has been approved.
2. Record the exact release version, download page, locale, and license/notice shipped
   with that release. Do not copy metadata from an older import.
3. Prepare or review the category and ambiguity columns, then run the offline importer
   with a new `taxonomyVersion`.
4. Inspect the JSON diff for ID churn, alias collisions, category drift, unexpectedly
   broad phrases, and removed skills. Large unexplained churn blocks the refresh.
5. Run the importer tests and the ATS analyzer regression suite. Have a reviewer sign
   off on the generated artifact and attribution before local deployment testing.
6. Promote the exact tested artifact deliberately. Keep the previous taxonomy version
   available for rollback; never fetch a taxonomy dynamically in the request path.

Use semantic intent for ApplyWise versions: patch for alias/note corrections with no
intended scoring shift, minor for additive skills or curated category updates, and
major for canonical-ID policy, category semantics, or other broad behavior changes.
The upstream `source.version` remains independent.

## Licensing and provenance

Licensing is release-specific operational input, not an importer default. Confirm the
current publisher terms before storing or redistributing an official export, including
attribution, notice, modification-marking, and share-alike obligations where relevant.
Record the exact license identifier in `source.license` and the authoritative release
page in `source.url`; retain any required notice alongside the deployed artifact.

The importer only transforms local input. It does not grant redistribution rights,
verify license compatibility, or prove provenance. If the applicable terms are
unclear, keep the official dataset out of the repository and obtain legal/licensing
review before publishing it.
