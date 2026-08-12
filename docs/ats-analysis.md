# ApplyWise resume analysis v3

ApplyWise produces a deterministic **readiness and job-alignment estimate**. It does
not reproduce, query, or represent an employer's applicant tracking system. Real ATS
products parse and search resumes differently, and the score cannot guarantee an
interview or hiring outcome. Suggestions are conditional: users should add a skill,
result, credential, or measurement only when it is true and supportable.

## Scores

- **Readiness estimate (0-100)** is job-independent. It measures extractable text,
  contact fields, conventional sections, recognizable chronology, bullet quality,
  length, clarity, repetition, and available document-format diagnostics.
- **Job Match estimate (0-100)** compares the resume with requirements extracted
  from a supplied job description. It separates must-have/required skills, preferred
  skills, responsibilities, evidence placement, title/domain/seniority, and
  credentials or eligibility.
- **ApplyWise Fit estimate (0-100)** is shown only when Job Match can be assessed:

  `round(Readiness * 0.20 + Job Match * 0.80)`

Coverage is evidence-weighted. A skills-list mention receives half-strength evidence;
experience or project evidence receives full placement strength. Exact and canonical
term strength is also applied. A missing majority of must-have requirements caps Job
Match below the "good" bands. Negated claims such as "no Docker experience" are not
matches. Repeating a keyword never creates additional coverage.

Score bands describe ApplyWise results only: 85-100 **Strong assessed signals**,
70-84 **Good assessed signals**, 50-69 **Needs targeted improvement**, and 0-49
**Significant gaps**. Confidence measures how much reliable input was available; it
does not add score points.

## Document inspection

PDF and DOCX uploads up to 5 MB are supported. PDF inspection returns page count,
selectable text, likely multi-column layout, rotated text, unusually small text, and
repeated top/bottom lines. DOCX inspection reads the Open XML package directly and
reports page metadata when present, columns, tables, text boxes, headers/footers, and
small-font runs. These are conservative heuristics rather than a claim to emulate
every ATS renderer.

If an older resume has cached text but no stored document diagnostics, visual layout
is explicitly **Not assessed**. No penalty is silently invented. Image-only PDFs are
still not OCR'd.

## Explainability and safety

The report exposes:

- the overall estimate, subscores, confidence, and reliability warnings;
- every score component with points, maximum, assessed state, and reasons;
- matched requirements with source section, snippet, match strength, and evidence
  strength;
- missing requirements with priority and the source job-description line;
- section and bullet reviews, including safe placeholder templates;
- the reminder that missing claims must only be added when genuine.

No external model receives resume or job text. Names and protected characteristics do
not participate in Job Match. The feature does not penalize age, gender, nationality,
photographs, or similar characteristics.

## Requirement and taxonomy coverage

The local fallback taxonomy is a reviewed multi-domain baseline covering common
software, cloud, data, AI/ML, cybersecurity, testing, product/project management,
sales, marketing, finance, HR, customer service, healthcare administration,
education, design, operations, supply chain, soft skills, and languages. It includes
modern terms such as Terraform, GCP, Snowflake, Databricks, Kafka, Redis, MongoDB,
PyTorch, Linux, Jenkins, Kotlin, and Swift.

The offline ESCO-style artifact importer remains the preferred production path for a
larger reviewed taxonomy. Configure `SkillTaxonomy:ArtifactPath` with a licensed,
versioned artifact. The runtime never downloads taxonomy data. The taxonomy version,
document diagnostics, score version, and scoring configuration all participate in
the cache key.

The extractor also recognizes multiple degree levels, broader professional
certifications, explicit years-of-experience ranges, work authorization, security
clearance, expanded job titles/seniority, and a wider responsibility-verb set.

## Evaluation

The checked-in eight-role synthetic fixture is a deterministic regression benchmark,
not evidence of market validity. Tests calculate exact-label extraction precision,
recall, false-positive rate, and pairwise ranking accuracy for that fixture. Separate
adversarial tests cover negation, keyword repetition, skills-only evidence, token
boundaries, protected-characteristic invariance, location false positives, calendar
years masquerading as metrics, layout risks, PDF metadata, and DOCX structure.

Do not publish claims such as "90% accurate" from the synthetic fixture. Before
claiming market-level validity, create a held-out, human-labelled corpus with licensed
or consented resumes/job descriptions across seniority, geography, language, file
layout, and occupation; report confidence intervals, slice metrics, and error cases.

Run verification from the repository root:

```powershell
dotnet restore ApplyWise.sln
dotnet build ApplyWise.sln --no-restore
dotnet test ApplyWise.sln --no-build
node --check src/ApplyWise.Web/wwwroot/js/resume-builder.js
node --test tests/resume-builder/resume-builder.test.cjs
python -m unittest tools/taxonomy/test_import_esco_taxonomy.py
dotnet ef migrations has-pending-model-changes --project src/ApplyWise.Web/ApplyWise.Web.csproj --startup-project src/ApplyWise.Web/ApplyWise.Web.csproj --no-build
```

## Versioning and remaining limitations

`ats-v3.0` records remain distinct from v2 and legacy rows. The v3 cache configuration
also includes file diagnostics and page count. Existing history stays readable and is
labelled legacy rather than mixed into current analytics.

Known limitations:

- no OCR for scanned PDFs;
- no semantic embedding or inference of unstated skills;
- no employer-specific ATS emulation;
- DOCX page count depends on document metadata and may be unavailable;
- layout checks are heuristics and cannot prove how every ATS will parse a file;
- language and regional resume conventions are not yet deeply localized;
- the checked-in evaluation fixture is synthetic and small.
