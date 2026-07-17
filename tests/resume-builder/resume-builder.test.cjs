'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const builder = require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'));

function walk(value, visitor) {
    if (Array.isArray(value)) {
        value.forEach((item) => walk(item, visitor));
        return;
    }
    if (!value || typeof value !== 'object') return;
    visitor(value);
    Object.values(value).forEach((item) => walk(item, visitor));
}

function allText(value) {
    const values = [];
    walk(value, (node) => {
        if (typeof node.text === 'string') values.push(node.text);
        else if (Array.isArray(node.text)) {
            node.text.forEach((part) => {
                if (typeof part === 'string') values.push(part);
                else if (part && typeof part.text === 'string') values.push(part.text);
            });
        }
        if (Array.isArray(node.ul)) values.push(...node.ul.filter((item) => typeof item === 'string'));
    });
    return values.join('\n');
}

function links(value) {
    const values = [];
    walk(value, (node) => {
        if (typeof node.link === 'string') values.push(node.link);
    });
    return values;
}

test('empty state implements the complete versioned server contract', () => {
    const state = builder.createEmptyState();
    assert.equal(state.schemaVersion, 1);
    assert.deepEqual(Object.keys(state.personalInformation), [
        'fullName', 'professionalTitle', 'phoneNumber', 'emailAddress', 'location',
        'linkedInUrl', 'gitHubUrl', 'portfolioUrl'
    ]);
    assert.deepEqual(state.sections.map((section) => section.key), [
        'professionalSummary', 'education', 'skills', 'experience', 'projects',
        'achievementsAndCertifications', 'languages', 'volunteerExperience', 'interests', 'customSections'
    ]);
    assert.equal(state.sections.length, 10);
    assert.ok(state.sections.every((section) => typeof section.title === 'string' && typeof section.isVisible === 'boolean'));
});

test('normalization keeps exact nested field names and drops unknown data', () => {
    const normalized = builder.normalizeState({
        schemaVersion: 999,
        personalInformation: { fullName: '  Ada Lovelace  ', emailAddress: 'ada@example.com', extra: 'drop' },
        education: [{ Id: 'wrong-case', institutionName: 'Analytical Academy', descriptionOrCoursework: 'Math', isCurrentlyStudying: true, evil: 'drop' }],
        experience: [{ companyName: 'Engine Works', bulletPoints: ['Designed systems'], isCurrentlyWorking: true }],
        projects: [{ projectName: 'Engine', technologiesUsed: ['Math', 'math', 'Logic'], repositoryUrl: 'https://example.com/repo', isOngoing: true }],
        achievementsAndCertifications: [{ title: 'Fellow', issuingOrganization: 'Society' }],
        volunteerExperience: [{ organizationName: 'Community', isCurrentlyVolunteering: true }],
        customSections: [{ title: 'Publications', entries: [{ heading: 'Notes', isCurrent: true, bulletPoints: ['An article'] }] }],
        unknownRoot: 'drop'
    });

    assert.equal(normalized.schemaVersion, builder.SCHEMA_VERSION);
    assert.equal(normalized.personalInformation.fullName, 'Ada Lovelace');
    assert.equal(normalized.education[0].descriptionOrCoursework, 'Math');
    assert.equal(normalized.education[0].isCurrentlyStudying, true);
    assert.equal(normalized.experience[0].isCurrentlyWorking, true);
    assert.deepEqual(normalized.projects[0].technologiesUsed, ['Math', 'Logic']);
    assert.equal(normalized.achievementsAndCertifications[0].issuingOrganization, 'Society');
    assert.equal(normalized.volunteerExperience[0].organizationName, 'Community');
    assert.equal(normalized.customSections[0].entries[0].heading, 'Notes');
    assert.equal('unknownRoot' in normalized, false);
    assert.equal('extra' in normalized.personalInformation, false);
    assert.equal('evil' in normalized.education[0], false);
});

test('normalization removes control characters and applies defensive caps', () => {
    const tooMany = Array.from({ length: builder.LIMITS.entries + 8 }, (_, index) => ({ institutionName: `School ${index}` }));
    const state = builder.normalizeState({
        personalInformation: { fullName: `A\u0000da${'x'.repeat(500)}` },
        professionalSummary: 's'.repeat(builder.LIMITS.summary + 100),
        education: tooMany,
        interests: Array.from({ length: builder.LIMITS.tags + 10 }, (_, index) => `Interest ${index}`)
    });
    assert.equal(state.personalInformation.fullName.includes('\u0000'), false);
    assert.equal(state.personalInformation.fullName.length, builder.LIMITS.name);
    assert.equal(state.professionalSummary.length, builder.LIMITS.summary);
    assert.equal(state.education.length, builder.LIMITS.entries);
    assert.equal(state.interests.length, builder.LIMITS.tags);
});

test('draft parsing recovers from malformed, oversized, and non-object data', () => {
    assert.equal(builder.parseDraft('{broken').recovered, true);
    assert.equal(builder.parseDraft('[]').reason, 'invalid-shape');
    assert.equal(builder.parseDraft('x'.repeat(builder.LIMITS.draftBytes + 1)).reason, 'too-large');
    const empty = builder.parseDraft('');
    assert.equal(empty.recovered, false);
    assert.equal(empty.reason, 'empty');

    const valid = builder.parseDraft(JSON.stringify({ personalInformation: { fullName: 'Grace Hopper' } }));
    assert.equal(valid.recovered, false);
    assert.equal(valid.state.personalInformation.fullName, 'Grace Hopper');
});

test('builder deep-link sections normalize to fixed selectors and reject selector input', () => {
    const aliases = {
        personalInformation: 'personalInformation',
        professionalSummary: 'professionalSummary',
        experience: 'experience',
        projects: 'projects',
        education: 'education',
        skills: 'skills',
        achievements: 'achievementsAndCertifications',
        certifications: 'achievementsAndCertifications',
        achievementsAndCertifications: 'achievementsAndCertifications'
    };
    Object.entries(aliases).forEach(([requested, expected]) => {
        assert.equal(builder.normalizeBuilderSection(requested), expected);
        assert.equal(builder.builderSectionSelector(requested), '[data-editor-section="' + expected + '"]');
    });
    assert.equal(builder.normalizeBuilderSection(' CERTIFICATIONS '), 'achievementsAndCertifications');
    assert.equal(builder.normalizeBuilderSection('experience.other'), '');
    assert.equal(builder.builderSectionSelector('experience"] [data-action="clear'), '');
    assert.equal(builder.builderSectionSelector('a'.repeat(65)), '');
});

test('builder view exposes normalized section hooks and deep-link highlighting', () => {
    const view = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/Views/ResumeBuilder/Index.cshtml'), 'utf8');
    assert.match(view, /data-initial-section="@Model\.InitialSection"/);
    assert.match(view, /data-editor-section="personalInformation"/);
    assert.match(view, /data-editor-section="professionalSummary"/);

    const stylesheet = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/css/resume-builder.css'), 'utf8');
    assert.match(stylesheet, /\[data-editor-section\]\.is-deep-link-target/);

    const script = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/js/resume-builder.js'), 'utf8');
    assert.match(script, /classList\.add\('is-deep-link-target'\)/);
    assert.match(script, /focus\(\{ preventScroll: true \}\)/);
    assert.match(script, /scrollIntoView\(\{ behavior:/);
});

test('email, phone, and URL helpers reject unsafe or malformed values', () => {
    assert.equal(builder.isEmail('engineer@example.com'), true);
    assert.equal(builder.isEmail('not-an-email'), false);
    assert.equal(builder.isEmail('a@b.com/foo'), false);
    assert.equal(builder.isEmail('a@example..com'), false);
    assert.equal(builder.isEmail('a..b@example.com'), false);
    assert.equal(builder.isPhone('+1 (202) 555-0147'), true);
    assert.equal(builder.isPhone('call-me-maybe'), false);
    assert.equal(builder.isHttpUrl('https://example.com/path?q=1'), true);
    assert.equal(builder.isHttpUrl('http://localhost:5000/path'), true);
    assert.equal(builder.isHttpUrl('javascript:alert(1)'), false);
    assert.equal(builder.isHttpUrl('https://user:password@example.com'), false);
    assert.equal(builder.isHttpUrl('//example.com'), false);
});

test('validation enforces required personal fields and all supported formats', () => {
    const emptyErrors = builder.validateState(builder.createEmptyState());
    assert.equal(emptyErrors['personalInformation.fullName'], 'Full name is required.');
    assert.equal(emptyErrors['personalInformation.professionalTitle'], 'Professional title is required.');
    assert.equal(emptyErrors['personalInformation.emailAddress'], 'Email address is required.');

    const state = builder.createSampleState();
    state.personalInformation.emailAddress = 'wrong';
    state.personalInformation.phoneNumber = 'abc';
    state.personalInformation.portfolioUrl = 'javascript:alert(1)';
    state.projects[0].projectUrl = 'ftp://example.com/file';
    state.projects[0].repositoryUrl = 'https://user:pass@example.com';
    state.achievementsAndCertifications[0].credentialUrl = 'relative/path';
    const errors = builder.validateState(state);
    assert.ok(errors['personalInformation.emailAddress']);
    assert.ok(errors['personalInformation.phoneNumber']);
    assert.ok(errors['personalInformation.portfolioUrl']);
    assert.ok(errors['projects.0.projectUrl']);
    assert.ok(errors['projects.0.repositoryUrl']);
    assert.ok(errors['achievementsAndCertifications.0.credentialUrl']);
});

test('date validation catches reversed ranges but ignores end dates for current roles', () => {
    const state = builder.createSampleState();
    state.education[0].startDate = '2027-01';
    state.education[0].endDate = '2026-12';
    state.experience[0].startDate = 'bad';
    let errors = builder.validateState(state);
    assert.ok(errors['education.0.endDate']);
    assert.ok(errors['experience.0.startDate']);

    state.education[0].isCurrentlyStudying = true;
    state.education[0].endDate = 'not-a-month';
    errors = builder.validateState(state);
    assert.equal(errors['education.0.endDate'], undefined);
    assert.equal(builder.formatMonth('2026-07'), 'Jul 2026');
    assert.equal(builder.formatDateRange('2024-01', '', true), 'Jan 2024 - Present');
});

test('date and URL validation includes nested custom entries', () => {
    const state = builder.createSampleState();
    state.customSections = [{
        id: 'custom', title: 'Publications', entries: [{
            id: 'entry', heading: 'Article', startDate: '2026-02', endDate: '2025-01',
            isCurrent: false, url: 'data:text/html,test', bulletPoints: []
        }]
    }];
    const errors = builder.validateState(state);
    assert.ok(errors['customSections.0.entries.0.endDate']);
    assert.ok(errors['customSections.0.entries.0.url']);
});

test('download filename uses first and last name and blocks path characters', () => {
    assert.equal(builder.buildFilename('Ada Byron Lovelace'), 'Ada_Lovelace_Resume.pdf');
    assert.equal(builder.buildFilename('  René Descartes  '), 'Rene_Descartes_Resume.pdf');
    assert.equal(builder.buildFilename('../../evil\\name'), 'evil_name_Resume.pdf');
    assert.equal(builder.buildFilename(''), 'ApplyWise_Resume.pdf');
    assert.equal(builder.buildFilename('Single'), 'Single_Resume.pdf');
});

test('restricted rich-text markers produce safe selectable PDF runs', () => {
    const source = '[b]Fast [i]and reliable[/i][/b] [u]delivery[/u] <script>alert(1)</script>';
    const runs = builder.richTextRuns(source);
    assert.equal(builder.plainRichText(source), 'Fast and reliable delivery <script>alert(1)</script>');
    assert.ok(runs.some((run) => run.text === 'Fast ' && run.bold === true));
    assert.ok(runs.some((run) => run.text === 'and reliable' && run.bold === true && run.italics === true));
    assert.ok(runs.some((run) => run.text === 'delivery' && run.decoration === 'underline'));

    const state = builder.createSampleState();
    state.professionalSummary = source;
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(output.includes('[b]'), false);
    assert.ok(output.includes('<script>alert(1)</script>'));
});

test('one-page guidance tracks the recommended content limits', () => {
    const state = builder.createSampleState();
    const withinGuide = builder.onePageGuidance(state);
    assert.equal(withinGuide.metrics.education.value, 2);
    assert.equal(withinGuide.metrics.experience.value, 3);
    assert.equal(withinGuide.metrics.projects.value, 3);
    assert.equal(withinGuide.metrics.achievementsAndCertifications.value, 3);
    assert.deepEqual(withinGuide.exceeded, []);

    state.sections.find((section) => section.key === 'projects').isVisible = false;
    assert.equal(builder.onePageGuidance(state).metrics.projects.value, 0);
    state.sections.find((section) => section.key === 'projects').isVisible = true;

    state.projects.push({ ...state.projects[0], id: 'fourth-project' });
    state.professionalSummary = 'Long summary sentence. '.repeat(30);
    const overGuide = builder.onePageGuidance(state);
    assert.ok(overGuide.exceeded.includes('projects'));
    assert.ok(overGuide.exceeded.includes('professionalSummary'));
});

test('draft readiness uses the documented local debounce and transparent six-check contract', () => {
    assert.equal(builder.READINESS_DEBOUNCE_MS, 600);
    assert.ok(builder.READINESS_DEBOUNCE_MS >= 500 && builder.READINESS_DEBOUNCE_MS <= 700);

    const state = builder.createEmptyState();
    const before = JSON.stringify(state);
    const readiness = builder.evaluateDraftReadiness(state);
    assert.equal(JSON.stringify(state), before, 'readiness evaluation must not mutate the in-memory draft');
    assert.equal(readiness.score, 0);
    assert.equal(readiness.readyCount, 0);
    assert.equal(readiness.totalChecks, 6);
    assert.equal(readiness.tone, 'starting');
    assert.deepEqual(readiness.checks.map((check) => check.key), [
        'essentials', 'summary', 'evidence', 'bullets', 'skills', 'structure'
    ]);
    assert.ok(readiness.warnings.length > 0);
    assert.equal('jobMatchScore' in readiness, false);
});

test('complete sample produces strong local draft readiness without claiming a job match', () => {
    const readiness = builder.evaluateDraftReadiness(builder.createSampleState());
    assert.ok(readiness.score >= 85);
    assert.equal(readiness.tone, 'strong');
    assert.ok(readiness.readyCount >= 5);
    assert.match(readiness.summary, /local checks ready/i);
    assert.equal(readiness.checks.every((check) => Number.isInteger(check.points) && check.points <= check.maximum), true);
});

test('readiness responds to missing essentials, evidence, bullets, and skills', () => {
    const complete = builder.createSampleState();
    const baseline = builder.evaluateDraftReadiness(complete);
    const sparse = builder.createEmptyState();
    sparse.personalInformation.fullName = 'Jordan Lee';
    sparse.education = complete.education.slice(0, 1);

    const readiness = builder.evaluateDraftReadiness(sparse);
    assert.ok(readiness.score < baseline.score);
    ['essentials', 'summary', 'bullets', 'skills'].forEach((key) => {
        assert.notEqual(readiness.checks.find((check) => check.key === key).status, 'ready');
    });
    assert.equal(readiness.checks.find((check) => check.key === 'evidence').status, 'attention');
});

test('readiness structure check reflects date validation errors', () => {
    const state = builder.createSampleState();
    state.education[0].startDate = '2027-01';
    state.education[0].endDate = '2026-12';
    const readiness = builder.evaluateDraftReadiness(state);
    const structure = readiness.checks.find((check) => check.key === 'structure');
    assert.equal(structure.status, 'attention');
    assert.match(structure.message, /date or URL/i);
});

test('readiness only credits content that will appear in the generated resume', () => {
    const state = builder.createSampleState();
    ['professionalSummary', 'education', 'experience', 'projects', 'skills', 'volunteerExperience'].forEach((key) => {
        state.sections.find((section) => section.key === key).isVisible = false;
    });
    const readiness = builder.evaluateDraftReadiness(state);
    ['summary', 'evidence', 'bullets', 'skills'].forEach((key) => {
        assert.equal(readiness.checks.find((check) => check.key === key).status, 'missing');
    });
});

test('readiness panel routes to the analyzer and explains that the draft stays local', () => {
    const view = fs.readFileSync(path.resolve(__dirname, '../../src/ApplyWise.Web/Views/ResumeBuilder/Index.cshtml'), 'utf8');
    assert.match(view, /data-readiness-panel/);
    assert.match(view, /asp-controller="ResumeAnalyzer"\s+asp-action="Index"/);
    assert.match(view, /Analyze against a job/);
    assert.match(view, /local draft is not uploaded automatically/i);
});

test('document definition is A4, selectable-text based, Roboto, and one-page optimized', () => {
    const state = builder.createSampleState();
    const documentDefinition = builder.buildDocumentDefinition(state);
    assert.equal(documentDefinition.pageSize, 'A4');
    assert.deepEqual(documentDefinition.pageMargins, [36, 27, 36, 27]);
    assert.equal(documentDefinition.defaultStyle.font, 'Roboto');
    assert.ok(Array.isArray(documentDefinition.content));
    assert.equal(documentDefinition.footer, undefined);
    assert.equal(JSON.stringify(documentDefinition).includes('image'), false);
});

test('PDF omits placeholder identity and suppresses hidden or empty sections', () => {
    const empty = builder.createEmptyState();
    const emptyText = allText(builder.buildDocumentDefinition(empty));
    assert.equal(emptyText.includes('Your Name'), false);
    assert.equal(emptyText.includes('Professional Title'), false);
    assert.equal(emptyText.includes('Education'), false);

    const state = builder.createSampleState();
    state.sections.find((section) => section.key === 'projects').isVisible = false;
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(output.includes('Projects'), false);
    assert.equal(output.includes('Campus Collaboration Hub'), false);
    assert.ok(output.includes('Professional Summary'));
});

test('PDF respects user section ordering', () => {
    const state = builder.createSampleState();
    const projects = state.sections.find((section) => section.key === 'projects');
    const summary = state.sections.find((section) => section.key === 'professionalSummary');
    state.sections = [projects, summary, ...state.sections.filter((section) => section !== projects && section !== summary)];
    const output = allText(builder.buildDocumentDefinition(state));
    assert.ok(output.indexOf('Projects') < output.indexOf('Professional Summary'));
});

test('PDF links are sanitized, blue/clickable, and phone stays clickable beside location', () => {
    const state = builder.createSampleState();
    state.personalInformation.phoneNumber = '+1 (202) 555-0147';
    state.personalInformation.location = 'Seattle, WA';
    state.projects[0].projectUrl = 'javascript:alert(1)';
    const documentDefinition = builder.buildDocumentDefinition(state);
    const documentLinks = links(documentDefinition);
    assert.ok(documentLinks.includes('tel:+12025550147'));
    assert.ok(documentLinks.includes('mailto:jordan.lee@example.com'));
    assert.equal(documentLinks.some((link) => link.startsWith('javascript:')), false);
    let linkedPhoneNode = null;
    walk(documentDefinition, (node) => { if (node.link === 'tel:+12025550147') linkedPhoneNode = node; });
    assert.equal(linkedPhoneNode.color, '#0000EE');
    assert.equal(linkedPhoneNode.decoration, 'underline');
});

test('PDF renders nested custom sections and suppresses blank custom sections', () => {
    const state = builder.createSampleState();
    state.customSections = [
        { id: 'blank', title: 'Blank section', entries: [] },
        { id: 'publications', title: 'Publications', entries: [{ id: 'paper', heading: 'Reliable Systems', subheading: 'Author', startDate: '2025-01', isCurrent: true, url: 'https://example.com/paper', bulletPoints: ['Explains resilient design.'] }] }
    ];
    const customDescriptor = state.sections.find((section) => section.key === 'customSections');
    customDescriptor.isVisible = true;
    const documentDefinition = builder.buildDocumentDefinition(state);
    const output = allText(documentDefinition);
    assert.equal(output.includes('Blank section'), false);
    assert.ok(output.includes('Publications'));
    assert.ok(output.includes('Reliable Systems'));
    assert.ok(links(documentDefinition).includes('https://example.com/paper'));
});

test('all generated PDF separator strings use ASCII hyphens', () => {
    const state = builder.createSampleState();
    state.languages[0].proficiency = 'Native';
    const output = allText(builder.buildDocumentDefinition(state));
    assert.equal(/[–—]/.test(output), false);
    assert.match(output, /Jan 2025 - Present/);
});

test('vendored pdfmake creates a real one-page sample and detects overflow', async () => {
    const pdfMake = require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/pdfmake.min.js'));
    require(path.resolve(__dirname, '../../src/ApplyWise.Web/wwwroot/lib/pdfmake/vfs_fonts.js'));
    const sample = builder.createSampleState();
    const blob = await pdfMake.createPdf(builder.buildDocumentDefinition(sample)).getBlob();
    assert.equal(blob.type, 'application/pdf');
    assert.ok(blob.size > 1000);
    const signature = Buffer.from(await blob.slice(0, 5).arrayBuffer()).toString('ascii');
    assert.equal(signature, '%PDF-');
    assert.equal(await builder.countPdfPages(blob), 1);

    sample.experience = Array.from({ length: 14 }, (_, index) => ({
        ...sample.experience[0],
        id: `overflow-${index}`,
        jobTitle: `Overflow role ${index + 1}`,
        bulletPoints: Array(4).fill('Delivered a substantial production improvement with measurable impact across a cross-functional team.')
    }));
    const overflowBlob = await pdfMake.createPdf(builder.buildDocumentDefinition(sample)).getBlob();
    assert.ok(await builder.countPdfPages(overflowBlob) > 1);
});
