using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class SkillTaxonomyService : ISkillTaxonomyService
{
    private sealed class MutableNode
    {
        public Dictionary<string, MutableNode> Children { get; } = new(StringComparer.Ordinal);
        public List<(SkillTaxonomyEntry Entry, string Alias)> Matches { get; } = [];
    }

    private sealed record TrieNode(FrozenDictionary<string, TrieNode> Children, IReadOnlyList<(SkillTaxonomyEntry Entry, string Alias)> Matches);

    private readonly IResumeTextNormalizer _normalizer;
    private readonly TrieNode _root;
    public IReadOnlyList<SkillTaxonomyEntry> Entries { get; }
    public string Version { get; }

    public SkillTaxonomyService(IResumeTextNormalizer normalizer)
        : this(normalizer, null, null, null)
    {
    }

    public SkillTaxonomyService(
        IResumeTextNormalizer normalizer,
        IOptions<SkillTaxonomyOptions>? options,
        IHostEnvironment? environment,
        ILogger<SkillTaxonomyService>? logger)
    {
        _normalizer = normalizer;
        var loaded = TryLoadArtifact(options?.Value.ArtifactPath, environment?.ContentRootPath, logger);
        Entries = loaded?.Entries ?? BuildFallback();
        Version = loaded?.Version ?? "curated-fallback-2026.08-v3";
        var root = new MutableNode();
        foreach (var entry in Entries)
        {
            foreach (var alias in entry.Aliases.Append(entry.PreferredLabel).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var tokens = normalizer.Tokenize(alias).Select(token => token.Value).ToArray();
                if (tokens.Length == 0) continue;
                var node = root;
                foreach (var token in tokens)
                {
                    if (!node.Children.TryGetValue(token, out var child)) node.Children[token] = child = new MutableNode();
                    node = child;
                }
                node.Matches.Add((entry, alias));
            }
        }
        _root = Freeze(root);
    }

    public IReadOnlyList<SkillMatch> FindMatches(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var tokens = _normalizer.Tokenize(text);
        var results = new Dictionary<string, SkillMatch>(StringComparer.OrdinalIgnoreCase);

        for (var start = 0; start < tokens.Count; start++)
        {
            var node = _root;
            (SkillTaxonomyEntry Entry, string Alias, int End)? longest = null;
            for (var cursor = start; cursor < tokens.Count; cursor++)
            {
                if (!node.Children.TryGetValue(tokens[cursor].Value, out var next)) break;
                node = next;
                foreach (var match in node.Matches)
                {
                    if (IsValidAmbiguousAlias(match.Entry, match.Alias, tokens, start, cursor)) longest = (match.Entry, match.Alias, cursor);
                }
            }
            if (longest is null) continue;

            var selected = longest.Value;
            var first = tokens[start];
            var last = tokens[selected.End];
            var length = last.StartIndex + last.Length - first.StartIndex;
            var strength = string.Equals(selected.Alias, selected.Entry.PreferredLabel, StringComparison.OrdinalIgnoreCase) ? 1d : .95d;
            var candidate = new SkillMatch(selected.Entry.Id, selected.Entry.PreferredLabel, selected.Alias, first.StartIndex, length, strength);
            if (!results.TryGetValue(candidate.SkillId, out var existing) || candidate.MatchStrength > existing.MatchStrength)
                results[candidate.SkillId] = candidate;
            start = selected.End;
        }

        return results.Values.OrderBy(match => match.StartIndex).ToArray();
    }

    private static bool IsValidAmbiguousAlias(SkillTaxonomyEntry entry, string alias, IReadOnlyList<NormalizedToken> tokens, int start, int end)
    {
        var lower = alias.ToLowerInvariant();
        var configuredAmbiguous = entry.AmbiguousAliases?.Contains(alias, StringComparer.OrdinalIgnoreCase) == true;
        if (!configuredAmbiguous && lower is not ("go" or "r" or "js")) return true;
        var windowStart = Math.Max(0, start - 4);
        var windowEnd = Math.Min(tokens.Count - 1, end + 4);
        var context = string.Join(' ', tokens.Skip(windowStart).Take(windowEnd - windowStart + 1).Select(token => token.Value));
        if (lower == "go")
            return tokens[start].Original == "Go" && !context.Contains("go to market", StringComparison.Ordinal) &&
                (context.Contains("developer", StringComparison.Ordinal) || context.Contains("programming", StringComparison.Ordinal) ||
                 context.Contains("language", StringComparison.Ordinal) || context.Contains("skill", StringComparison.Ordinal) ||
                 context.Contains("experience with", StringComparison.Ordinal));
        if (lower == "r")
            return tokens[start].Original == "R" && (context.Contains("data", StringComparison.Ordinal) || context.Contains("statistic", StringComparison.Ordinal) ||
                context.Contains("programming", StringComparison.Ordinal) || context.Contains("skill", StringComparison.Ordinal));
        if (lower == "js")
            return context.Contains("developer", StringComparison.Ordinal) || context.Contains("react", StringComparison.Ordinal) ||
            context.Contains("node", StringComparison.Ordinal) || context.Contains("javascript", StringComparison.Ordinal) || context.Contains("skill", StringComparison.Ordinal);
        return context.Contains("skill", StringComparison.Ordinal) || context.Contains("required", StringComparison.Ordinal) ||
            context.Contains("experience", StringComparison.Ordinal) || context.Contains("proficient", StringComparison.Ordinal) || context.Contains("knowledge", StringComparison.Ordinal);
    }

    private static TrieNode Freeze(MutableNode node) => new(
        node.Children.ToFrozenDictionary(pair => pair.Key, pair => Freeze(pair.Value), StringComparer.Ordinal),
        node.Matches.ToArray());

    private static SkillTaxonomyEntry E(string id, string label, string category, params string[] aliases) =>
        new(id, label, category, aliases);

    private static SkillTaxonomyEntry EA(string id, string label, string category, string[] aliases, params string[] ambiguousAliases) =>
        new(id, label, category, aliases, ambiguousAliases);

    private static LoadedTaxonomy? TryLoadArtifact(
        string? configuredPath,
        string? contentRoot,
        ILogger<SkillTaxonomyService>? logger)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;
        try
        {
            var path = Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(contentRoot ?? AppContext.BaseDirectory, configuredPath);
            var file = new FileInfo(Path.GetFullPath(path));
            if (!file.Exists) throw new FileNotFoundException("The configured taxonomy artifact was not found.", file.FullName);
            if (file.Length is <= 0 or > 50 * 1024 * 1024) throw new InvalidDataException("The taxonomy artifact size is invalid.");

            var artifact = JsonSerializer.Deserialize<TaxonomyArtifact>(
                File.ReadAllText(file.FullName),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (artifact is null || artifact.SchemaVersion != 1 || string.IsNullOrWhiteSpace(artifact.TaxonomyVersion))
                throw new InvalidDataException("The taxonomy artifact schema or version is unsupported.");
            if (artifact.Entries.Length is 0 or > 100_000)
                throw new InvalidDataException("The taxonomy artifact entry count is invalid.");

            var entries = artifact.Entries.Select(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.PreferredLabel) || item.PreferredLabel.Length > 160)
                    throw new InvalidDataException("A taxonomy entry has an invalid ID or label.");
                var aliases = item.Aliases
                    .Where(alias => !string.IsNullOrWhiteSpace(alias) && alias.Length <= 160)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new SkillTaxonomyEntry(
                    item.Id,
                    item.PreferredLabel,
                    string.IsNullOrWhiteSpace(item.Category) ? "Technical" : item.Category,
                    aliases,
                    item.Ambiguity?.RequiresContext == true ? item.Ambiguity.Aliases : []);
            }).ToArray();
            if (entries.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length)
                throw new InvalidDataException("The taxonomy artifact contains duplicate canonical IDs.");

            logger?.LogInformation(
                "Loaded local skill taxonomy artifact version {TaxonomyVersion} with {EntryCount} entries.",
                artifact.TaxonomyVersion,
                entries.Length);
            return new LoadedTaxonomy(entries, "artifact:" + artifact.TaxonomyVersion);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            logger?.LogWarning(exception, "The configured local taxonomy artifact was rejected; the curated fallback will be used.");
            return null;
        }
    }

    private sealed record LoadedTaxonomy(IReadOnlyList<SkillTaxonomyEntry> Entries, string Version);
    private sealed class TaxonomyArtifact
    {
        public int SchemaVersion { get; init; }
        public string TaxonomyVersion { get; init; } = string.Empty;
        public TaxonomyArtifactEntry[] Entries { get; init; } = [];
    }

    private sealed class TaxonomyArtifactEntry
    {
        public string Id { get; init; } = string.Empty;
        public string PreferredLabel { get; init; } = string.Empty;
        public string Category { get; init; } = string.Empty;
        public string[] Aliases { get; init; } = [];
        public TaxonomyAmbiguity? Ambiguity { get; init; }
    }

    private sealed class TaxonomyAmbiguity
    {
        public bool RequiresContext { get; init; }
        public string[] Aliases { get; init; } = [];
    }

    private static IReadOnlyList<SkillTaxonomyEntry> BuildFallback() =>
    [
        E("it.csharp", "C#", "Technical", "c sharp"), E("it.cpp", "C++", "Technical", "c plus plus"),
        E("it.dotnet", ".NET", "Technical", "dotnet", "dot net"), E("it.aspnet", "ASP.NET Core", "Technical", "asp net core", "aspnet core"),
        E("it.java", "Java", "Technical"), E("it.javascript", "JavaScript", "Technical", "javascript", "js"), E("it.typescript", "TypeScript", "Technical"),
        E("it.react", "React", "Technical", "react.js", "reactjs"), E("it.reactnative", "React Native", "Technical", "react-native"),
        E("it.node", "Node.js", "Technical", "nodejs", "node js"), E("it.angular", "Angular", "Technical", "angularjs"),
        E("it.python", "Python", "Technical"), EA("it.go", "Go", "Technical", ["golang"], "Go"), EA("data.r", "R", "Technical", ["r language"], "R"),
        E("it.html", "HTML", "Technical", "html5"), E("it.css", "CSS", "Technical", "css3"), E("it.sql", "SQL", "Technical", "structured query language"),
        E("data.sqlserver", "SQL Server", "Tool", "mssql", "microsoft sql server"), E("data.postgresql", "PostgreSQL", "Tool", "postgres"),
        E("data.mysql", "MySQL", "Tool"), E("data.powerbi", "Power BI", "Tool", "powerbi"), E("data.tableau", "Tableau", "Tool"),
        E("data.excel", "Microsoft Excel", "Tool", "excel", "ms excel"), E("data.analytics", "Data Analysis", "Domain", "data analytics"),
        E("data.visualization", "Data Visualization", "Domain", "data visualisation"), E("data.statistics", "Statistics", "Domain", "statistical analysis"),
        E("it.aws", "AWS", "Tool", "amazon web services"), E("it.azure", "Microsoft Azure", "Tool", "azure"), E("it.docker", "Docker", "Tool"),
        E("it.kubernetes", "Kubernetes", "Tool", "k8s"), E("it.git", "Git", "Tool"), E("it.github", "GitHub", "Tool"),
        E("it.cicd", "CI/CD", "Technical", "ci cd", "continuous integration", "continuous delivery"), E("it.rest", "REST API", "Technical", "restful api", "web api"),
        E("it.efcore", "Entity Framework Core", "Technical", "ef core", "efcore"), E("it.microservices", "Microservices", "Technical", "microservice architecture"),
        E("cyber.infosec", "Information Security", "Domain", "cybersecurity", "cyber security"), E("cyber.siem", "SIEM", "Tool", "security information and event management"),
        E("cyber.risk", "Risk Assessment", "Domain", "security risk assessment"), E("product.management", "Product Management", "Domain"),
        E("project.management", "Project Management", "Domain", "programme management"), E("project.agile", "Agile", "Domain"), E("project.scrum", "Scrum", "Domain"),
        E("project.jira", "Jira", "Tool"), E("sales.crm", "CRM", "Tool", "customer relationship management"), E("sales.salesforce", "Salesforce", "Tool"),
        E("sales.development", "Business Development", "Domain"), E("sales.negotiation", "Negotiation", "Soft"), E("marketing.seo", "SEO", "Domain", "search engine optimization"),
        E("marketing.sem", "SEM", "Domain", "search engine marketing"), E("marketing.content", "Content Marketing", "Domain"), E("marketing.analytics", "Marketing Analytics", "Domain"),
        E("marketing.ads", "Google Ads", "Tool", "google adwords"), E("finance.accounting", "Accounting", "Domain"), E("finance.reporting", "Financial Reporting", "Domain"),
        E("finance.analysis", "Financial Analysis", "Domain"), E("finance.quickbooks", "QuickBooks", "Tool"), E("finance.sap", "SAP", "Tool"),
        E("hr.recruiting", "Recruitment", "Domain", "talent acquisition"), E("hr.onboarding", "Employee Onboarding", "Domain"), E("hr.payroll", "Payroll", "Domain"),
        E("service.support", "Customer Support", "Domain", "customer service"), E("service.zendesk", "Zendesk", "Tool"), E("service.resolution", "Conflict Resolution", "Soft"),
        E("health.admin", "Healthcare Administration", "Domain", "health administration"), E("health.records", "Electronic Health Records", "Tool", "ehr", "emr"),
        E("education.teaching", "Teaching", "Domain", "instruction"), E("education.curriculum", "Curriculum Development", "Domain"), E("education.lms", "Learning Management System", "Tool", "lms"),
        E("design.ux", "UX Design", "Domain", "user experience design"), E("design.ui", "UI Design", "Domain", "user interface design"), E("design.figma", "Figma", "Tool"),
        E("design.adobe", "Adobe Creative Suite", "Tool", "photoshop", "illustrator", "indesign"), E("ops.process", "Process Improvement", "Domain", "continuous improvement"),
        E("ops.quality", "Quality Assurance", "Domain", "quality control"), E("supply.inventory", "Inventory Management", "Domain"),
        E("supply.procurement", "Procurement", "Domain", "sourcing"), E("supply.logistics", "Logistics", "Domain"), E("supply.chain", "Supply Chain Management", "Domain"),
        E("soft.communication", "Communication", "Soft", "written communication", "verbal communication"), E("soft.leadership", "Leadership", "Soft", "team leadership"),
        E("soft.collaboration", "Collaboration", "Soft", "cross-functional collaboration"), E("soft.problem", "Problem Solving", "Soft", "problem-solving"),
        E("soft.english", "English", "Language", "written english", "spoken english"),

        E("it.php", "PHP", "Technical"), E("it.ruby", "Ruby", "Technical"), E("it.rust", "Rust", "Technical"),
        E("it.kotlin", "Kotlin", "Technical"), E("it.swift", "Swift", "Technical"), E("it.scala", "Scala", "Technical"),
        E("it.dart", "Dart", "Technical"), E("data.matlab", "MATLAB", "Tool"), E("it.bash", "Bash", "Technical", "shell scripting"),
        E("it.powershell", "PowerShell", "Technical"), E("it.vue", "Vue.js", "Technical", "vue", "vuejs"),
        E("it.svelte", "Svelte", "Technical"), E("it.nextjs", "Next.js", "Technical", "nextjs", "next js"),
        E("it.django", "Django", "Technical"), E("it.flask", "Flask", "Technical"), E("it.fastapi", "FastAPI", "Technical", "fast api"),
        E("it.springboot", "Spring Boot", "Technical", "spring framework"), E("it.laravel", "Laravel", "Technical"),
        E("it.rails", "Ruby on Rails", "Technical", "rails"), E("it.flutter", "Flutter", "Technical"),
        E("it.graphql", "GraphQL", "Technical"), E("it.grpc", "gRPC", "Technical"), E("it.oauth", "OAuth", "Technical", "oauth2", "openid connect", "oidc"),
        E("it.linux", "Linux", "Tool", "unix"), E("it.windowsserver", "Windows Server", "Tool"), E("it.nginx", "Nginx", "Tool"),
        E("it.kafka", "Apache Kafka", "Tool", "kafka"), E("it.rabbitmq", "RabbitMQ", "Tool"),
        E("it.redis", "Redis", "Tool"), E("data.mongodb", "MongoDB", "Tool", "mongo db"), E("data.oracle", "Oracle Database", "Tool", "oracle db"),
        E("data.sqlite", "SQLite", "Tool"), E("data.elasticsearch", "Elasticsearch", "Tool", "elastic search"),
        E("data.dynamodb", "Amazon DynamoDB", "Tool", "dynamodb"), E("data.snowflake", "Snowflake", "Tool"),
        E("data.databricks", "Databricks", "Tool"), E("data.spark", "Apache Spark", "Tool", "spark", "pyspark"),
        E("data.airflow", "Apache Airflow", "Tool", "airflow"), E("data.dbt", "dbt", "Tool", "data build tool"),
        E("data.pandas", "pandas", "Tool"), E("data.numpy", "NumPy", "Tool"), E("data.scikitlearn", "scikit-learn", "Tool", "sklearn"),
        E("data.tensorflow", "TensorFlow", "Tool"), E("data.pytorch", "PyTorch", "Tool"), E("data.ml", "Machine Learning", "Domain", "ml"),
        E("data.ai", "Artificial Intelligence", "Domain", "generative ai", "genai"), E("data.nlp", "Natural Language Processing", "Domain", "nlp"),
        E("data.etl", "ETL", "Technical", "extract transform load"), E("data.warehouse", "Data Warehousing", "Domain", "data warehouse"),
        E("it.gcp", "Google Cloud Platform", "Tool", "google cloud", "gcp"), E("it.terraform", "Terraform", "Tool", "infrastructure as code", "iac"),
        E("it.ansible", "Ansible", "Tool"), E("it.helm", "Helm", "Tool", "helm charts"), E("it.jenkins", "Jenkins", "Tool"),
        E("it.githubactions", "GitHub Actions", "Tool"), E("it.gitlabci", "GitLab CI", "Tool", "gitlab ci/cd"),
        E("it.azuredevops", "Azure DevOps", "Tool"), E("it.serverless", "Serverless", "Technical", "serverless architecture"),
        E("it.eventdriven", "Event-driven Architecture", "Technical", "event driven architecture"), E("it.ddd", "Domain-driven Design", "Technical", "domain driven design", "ddd"),
        E("test.selenium", "Selenium", "Tool"), E("test.playwright", "Playwright", "Tool"), E("test.cypress", "Cypress", "Tool"),
        E("test.jest", "Jest", "Tool"), E("test.pytest", "pytest", "Tool"), E("test.junit", "JUnit", "Tool"),
        E("test.automation", "Test Automation", "Domain", "automated testing"), E("test.unit", "Unit Testing", "Domain"),
        E("cyber.splunk", "Splunk", "Tool"), E("cyber.sentinel", "Microsoft Sentinel", "Tool", "azure sentinel"),
        E("cyber.crowdstrike", "CrowdStrike", "Tool"), E("cyber.nist", "NIST Cybersecurity Framework", "Domain", "nist csf"),
        E("cyber.iso27001", "ISO 27001", "Domain", "iso/iec 27001"), E("cyber.soc2", "SOC 2", "Domain", "soc2"),
        E("cyber.iam", "Identity and Access Management", "Domain", "iam"), E("cyber.pentest", "Penetration Testing", "Domain", "pen testing"),
        E("cyber.vulnerability", "Vulnerability Management", "Domain"), E("cyber.incident", "Incident Response", "Domain"),
        E("product.roadmap", "Product Roadmapping", "Domain", "product roadmap"), E("product.research", "User Research", "Domain"),
        E("product.okr", "OKRs", "Domain", "objectives and key results"), E("project.confluence", "Confluence", "Tool"),
        E("project.asana", "Asana", "Tool"), E("project.trello", "Trello", "Tool"), E("project.notion", "Notion", "Tool"),
        E("project.waterfall", "Waterfall", "Domain", "waterfall methodology"), E("project.risk", "Project Risk Management", "Domain"),
        E("sales.hubspot", "HubSpot", "Tool"), E("sales.account", "Account Management", "Domain"),
        E("sales.lead", "Lead Generation", "Domain"), E("sales.coldcalling", "Cold Calling", "Domain"),
        E("sales.operations", "Sales Operations", "Domain"), E("sales.pipeline", "Pipeline Management", "Domain"),
        E("marketing.email", "Email Marketing", "Domain"), E("marketing.social", "Social Media Marketing", "Domain"),
        E("marketing.ga", "Google Analytics", "Tool", "ga4"), E("marketing.metaads", "Meta Ads", "Tool", "facebook ads"),
        E("marketing.copywriting", "Copywriting", "Domain"), E("marketing.automation", "Marketing Automation", "Domain"),
        E("finance.gaap", "GAAP", "Domain"), E("finance.ifrs", "IFRS", "Domain"), E("finance.netsuite", "NetSuite", "Tool"),
        E("finance.xero", "Xero", "Tool"), E("finance.fpa", "Financial Planning and Analysis", "Domain", "fp&a"),
        E("finance.audit", "Auditing", "Domain", "audit"), E("finance.tax", "Tax Preparation", "Domain", "tax compliance"),
        E("hr.hris", "HRIS", "Tool", "human resources information system"), E("hr.workday", "Workday", "Tool"),
        E("hr.bamboohr", "BambooHR", "Tool"), E("hr.relations", "Employee Relations", "Domain"),
        E("hr.compensation", "Compensation and Benefits", "Domain", "total rewards"), E("hr.performance", "Performance Management", "Domain"),
        E("service.servicenow", "ServiceNow", "Tool", "service now"), E("service.freshdesk", "Freshdesk", "Tool"),
        E("service.jsm", "Jira Service Management", "Tool", "jira service desk"), E("service.sla", "SLA Management", "Domain", "service level agreement"),
        E("health.hipaa", "HIPAA", "Domain"), E("health.billing", "Medical Billing", "Domain"), E("health.icd10", "ICD-10", "Domain", "icd 10"),
        E("health.epic", "Epic", "Tool", "epic systems"), E("education.instructional", "Instructional Design", "Domain"),
        E("education.canvas", "Canvas LMS", "Tool", "canvas"), E("education.blackboard", "Blackboard", "Tool"),
        E("design.sketch", "Sketch", "Tool"), E("design.canva", "Canva", "Tool"), E("design.prototyping", "Prototyping", "Domain"),
        E("ops.lean", "Lean", "Domain", "lean management"), E("ops.sixsigma", "Six Sigma", "Domain"),
        E("ops.sop", "Standard Operating Procedures", "Domain", "sop", "sops"), E("ops.change", "Change Management", "Domain"),
        E("ops.vendor", "Vendor Management", "Domain"), E("ops.forecasting", "Forecasting", "Domain"),
        E("supply.wms", "Warehouse Management System", "Tool", "wms"), E("supply.demand", "Demand Planning", "Domain"),
        E("supply.erp", "Enterprise Resource Planning", "Tool", "erp"), E("soft.presentation", "Presentation Skills", "Soft", "public speaking"),
        E("soft.time", "Time Management", "Soft"), E("soft.adaptability", "Adaptability", "Soft"), E("soft.critical", "Critical Thinking", "Soft"),
        E("language.spanish", "Spanish", "Language"), E("language.french", "French", "Language"), E("language.german", "German", "Language"),
        E("language.arabic", "Arabic", "Language"), E("language.mandarin", "Mandarin Chinese", "Language", "mandarin")
    ];
}
