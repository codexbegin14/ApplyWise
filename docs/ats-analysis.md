# ApplyWise ATS analysis v2

ApplyWise provides a deterministic compatibility estimate; it does not reproduce or
represent an employer's applicant tracking system. Results cannot guarantee an
interview or hiring outcome. Suggestions are intentionally conditional: a user should
only add a skill, result, credential, or measurement when it is true and verifiable.

## Scores

- **ATS Readiness (0-100)** is job-independent. It checks extractable text, contact
  fields, standard sections, date/structural consistency, bullet quality, length,
  clarity, and repetition. A layout property that cannot be inferred reliably from
  extracted text is marked **Not assessed** instead of being guessed.
- **Job Match (0-100)** compares one resume with meaningful requirements extracted
  from a supplied job description. It separates must-have/required skill coverage,
  preferred coverage, responsibilities, evidence placement, title/domain/seniority,
  and credentials. Repeating a keyword does not create additional contributions.
- **ApplyWise Fit (0-100)** is shown only when Job Match can be assessed:

  `round(ATS Readiness * 0.20 + Job Match * 0.80)`

When no usable job description is supplied, the interface shows ATS Readiness only.
Unreadable, encrypted, invalid, image-only, oversized, or over-limit PDFs do not
receive a normal Job Match score.

Score bands are 85-100 **Strong alignment**, 70-84 **Good alignment**, 50-69
**Needs targeted improvement**, and 0-49 **Significant gaps**. Confidence describes
the amount and reliability of available input; it never adds free score points.

## Explainability and review behavior

Each score component stores its points, maximum, assessed state, and machine-readable
reasons. Matches store the canonical requirement, priority, category, source section,
short encoded snippet, match strength, evidence strength, and weighted contribution.
The review page exposes parsing notes, requirement coverage, matched evidence, missing
requirements, section cards, bullet checks, and at most eight top improvements.

Recommendations prioritize critical parsing failures, missing must-haves, missing
required requirements, weak required evidence, missing important sections, weak
experience/project bullets, preferred requirements, and general writing. Rewrite
examples use placeholders such as `[technology]`, `[number]`, and `[result]`; the
scorer never fabricates facts.

## Architecture and performance

The request path uses focused local services for normalization, section detection,
taxonomy matching, requirement extraction, ATS scoring, Job Match scoring, orchestration,
and persistence. Stateless deterministic services are singletons. The taxonomy is an
immutable token trie loaded once at startup, longest valid phrases win, and no live
taxonomy or AI request occurs during analysis. PDF text is reused from the private
resume record after the first successful extraction.

Identical inputs are cached per user, resume, job context, analysis type, score version,
scoring configuration, and taxonomy version using a SHA-256 key. Cache lookup still
validates ownership and relationships. `ats-v2.0` records remain distinct from legacy
scores in history and analytics.

Measured on the local development environment after warm-up:

- 30 typical analyses: 3.483 ms average, 4.799 ms p95, 5.082 ms maximum.
- Analyze and persist 25 cached-text resumes: 486.899 ms total.
- Repeat cached ranking of those 25 resumes: 35.760 ms total.

These measurements exclude PDF extraction and vary by machine and database. Run the
performance tests again after scoring or taxonomy changes rather than treating the
numbers as a permanent guarantee.

## Privacy and security boundaries

- Resume and job text stay in the existing private database and are never sent to an
  external model by this feature.
- Every resume, analysis, and saved-application lookup is scoped to the authenticated
  user. Analysis POSTs retain antiforgery validation and a per-user rate limit.
- Logs contain timing, cache status, character counts, requirement counts, match
  counts, extraction status, and score version—not resume text, job text, email, or
  phone values.
- Razor encodes snippets and extracted text. Uploaded resumes remain behind private
  storage authorization rather than public static-file hosting.
- Names and protected characteristics do not participate in Job Match. The analyzer
  does not penalize age, gender, nationality, photographs, or similar characteristics.

Browser-local Resume Builder drafts keep their autosave and draft-readiness calculation
in the browser. Opening an analyzer action is explicit; drafts are not silently
uploaded.

## Taxonomy and score versions

The bundled curated fallback covers representative software/IT, data, cybersecurity,
product/project management, sales, marketing, finance, HR, customer service,
healthcare administration, education, design, operations, and supply-chain terms. It
is not presented as the complete ESCO taxonomy. The offline, versioned ESCO-style
import process and licensing checklist are in [ats-taxonomy.md](ats-taxonomy.md).

`ScoreVersion` is stored with every analysis. A taxonomy refresh changes the cache key
without pretending that older scoring output used the new data. Legacy records remain
readable and are labeled rather than mixed into current ApplyWise Fit averages.

## Evaluation and verification

The synthetic file
`tests/ApplyWise.Web.Tests/Fixtures/ats-evaluation-set.json` covers ASP.NET, frontend,
data analysis, product management, digital marketing, accounting, customer support,
and project management. Each scenario defines a job description, an evidence-rich
resume, a comparison resume, expected required/preferred requirements, expected
matches/misses, and the expected pairwise winner. It contains no real personal data.

Use the fixture to track:

- **Skill extraction precision:** correct extracted canonical requirements divided by
  all extracted canonical requirements.
- **Skill extraction recall:** expected canonical requirements detected divided by all
  expected canonical requirements.
- **False-positive rate:** unexpected matches divided by all reported matches.
- **Pairwise ranking accuracy:** scenarios where the evidence-rich expected resume
  ranks first divided by all scenarios.
- **Score stability:** byte-equivalent serialized results for identical inputs and
  versions.
- **Latency:** warm single-analysis, 25-resume first-pass, and 25-resume cache-hit time.

Run the local verification suite from the repository root:

```powershell
dotnet restore ApplyWise.sln
dotnet build ApplyWise.sln --no-restore
dotnet test ApplyWise.sln --no-build
dotnet test tests/ApplyWise.Web.Tests/ApplyWise.Web.Tests.csproj --no-build --filter Category=Performance --logger "console;verbosity=detailed"
node --check src/ApplyWise.Web/wwwroot/js/resume-builder.js
node --test tests/resume-builder/resume-builder.test.cjs
python -m unittest tools/taxonomy/test_import_esco_taxonomy.py
dotnet ef migrations has-pending-model-changes --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj --no-build
```

Known limitations include no OCR, no semantic embedding model, no visual-layout claims
when only cached text exists, and dependence on the reviewed local taxonomy. Future
work can add opt-in OCR and optional rewrite assistance without allowing either to
control the deterministic score.
