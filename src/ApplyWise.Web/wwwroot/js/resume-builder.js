/* ApplyWise Resume Builder
 * Pure state/PDF helpers are exported through CommonJS for dependency-free tests.
 * In a browser the same module is exposed as ApplyWiseResumeBuilder and starts itself.
 */
(function (globalObject, factory) {
    'use strict';

    const api = factory();
    if (typeof module === 'object' && module.exports) {
        module.exports = api;
        return;
    }

    globalObject.ApplyWiseResumeBuilder = api;
    const start = function () { api.autoInitialize(); };
    if (globalObject.document && globalObject.document.readyState === 'loading') {
        globalObject.document.addEventListener('DOMContentLoaded', start, { once: true });
    } else if (globalObject.document) {
        start();
    }
}(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    const SCHEMA_VERSION = 1;
    const LIMITS = Object.freeze({
        draftBytes: 512000,
        shortText: 180,
        name: 120,
        email: 254,
        url: 2048,
        summary: 2500,
        longText: 4000,
        bullet: 1000,
        tag: 80,
        entries: 25,
        customSections: 10,
        customEntries: 20,
        bullets: 20,
        tags: 50
    });
    const ONE_PAGE_RECOMMENDATIONS = Object.freeze({
        professionalSummary: 3,
        education: 2,
        experience: 3,
        projects: 3,
        achievementsAndCertifications: 3
    });
    const READINESS_DEBOUNCE_MS = 600;

    const SECTION_DEFAULTS = Object.freeze([
        Object.freeze({ key: 'professionalSummary', title: 'Professional Summary', isVisible: true }),
        Object.freeze({ key: 'education', title: 'Education', isVisible: true }),
        Object.freeze({ key: 'skills', title: 'Technical Skills', isVisible: true }),
        Object.freeze({ key: 'experience', title: 'Experience', isVisible: true }),
        Object.freeze({ key: 'projects', title: 'Projects', isVisible: true }),
        Object.freeze({ key: 'achievementsAndCertifications', title: 'Achievements & Certifications', isVisible: true }),
        Object.freeze({ key: 'languages', title: 'Languages', isVisible: false }),
        Object.freeze({ key: 'volunteerExperience', title: 'Volunteer Experience', isVisible: false }),
        Object.freeze({ key: 'interests', title: 'Interests', isVisible: false }),
        Object.freeze({ key: 'customSections', title: 'Custom Sections', isVisible: false })
    ]);
    const SECTION_KEYS = new Set(SECTION_DEFAULTS.map(function (section) { return section.key; }));
    const BUILDER_SECTION_ALIASES = new Map([
        ['personalinformation', 'personalInformation'],
        ['professionalsummary', 'professionalSummary'],
        ['experience', 'experience'],
        ['projects', 'projects'],
        ['education', 'education'],
        ['skills', 'skills'],
        ['achievements', 'achievementsAndCertifications'],
        ['certifications', 'achievementsAndCertifications'],
        ['achievementsandcertifications', 'achievementsAndCertifications']
    ]);
    const MONTHS = Object.freeze(['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']);
    const CONTROL_CHARACTERS = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/g;
    let runtimeId = 0;

    function isRecord(value) {
        if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
        const prototype = Object.getPrototypeOf(value);
        return prototype === Object.prototype || prototype === null;
    }

    function text(value, maximum, trim) {
        if (typeof value !== 'string' && typeof value !== 'number') return '';
        let result = String(value).replace(/\r\n?/g, '\n').replace(CONTROL_CHARACTERS, '');
        if (trim !== false) result = result.trim();
        return result.slice(0, maximum);
    }

    function normalizeBuilderSection(value) {
        const candidate = text(value, 64).toLocaleLowerCase();
        return BUILDER_SECTION_ALIASES.get(candidate) || '';
    }

    function builderSectionSelector(value) {
        const section = normalizeBuilderSection(value);
        return section ? '[data-editor-section="' + section + '"]' : '';
    }

    function boolean(value) {
        return value === true;
    }

    function array(value, maximum) {
        return Array.isArray(value) ? value.slice(0, maximum) : [];
    }

    function safeId(value, prefix, index) {
        const candidate = text(value, 64);
        if (/^[A-Za-z0-9_-]{1,64}$/.test(candidate)) return candidate;
        return prefix + '-' + String(index + 1);
    }

    function newId(prefix) {
        runtimeId += 1;
        return prefix + '-' + Date.now().toString(36) + '-' + runtimeId.toString(36);
    }

    function uniqueStrings(value, maximumItems, maximumLength) {
        const result = [];
        const seen = new Set();
        array(value, maximumItems).forEach(function (item) {
            const normalized = text(item, maximumLength);
            const key = normalized.toLocaleLowerCase();
            if (normalized && !seen.has(key)) {
                seen.add(key);
                result.push(normalized);
            }
        });
        return result;
    }

    function stringList(value, maximumItems, maximumLength) {
        return array(value, maximumItems).map(function (item) { return text(item, maximumLength); });
    }

    function createEmptyState() {
        return {
            schemaVersion: SCHEMA_VERSION,
            personalInformation: {
                fullName: '',
                professionalTitle: '',
                phoneNumber: '',
                emailAddress: '',
                location: '',
                linkedInUrl: '',
                gitHubUrl: '',
                portfolioUrl: ''
            },
            professionalSummary: '',
            education: [],
            experience: [],
            projects: [],
            skills: [],
            achievementsAndCertifications: [],
            languages: [],
            volunteerExperience: [],
            interests: [],
            customSections: [],
            sections: SECTION_DEFAULTS.map(function (section) { return Object.assign({}, section); })
        };
    }

    function normalizePersonal(value) {
        const source = isRecord(value) ? value : {};
        return {
            fullName: text(source.fullName, LIMITS.name),
            professionalTitle: text(source.professionalTitle, LIMITS.shortText),
            phoneNumber: text(source.phoneNumber, 40),
            emailAddress: text(source.emailAddress, LIMITS.email),
            location: text(source.location, LIMITS.shortText),
            linkedInUrl: text(source.linkedInUrl, LIMITS.url),
            gitHubUrl: text(source.gitHubUrl, LIMITS.url),
            portfolioUrl: text(source.portfolioUrl, LIMITS.url)
        };
    }

    function normalizeEducation(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'education', index),
            institutionName: text(source.institutionName, LIMITS.shortText),
            degree: text(source.degree, LIMITS.shortText),
            fieldOfStudy: text(source.fieldOfStudy, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyStudying: boolean(source.isCurrentlyStudying),
            location: text(source.location, LIMITS.shortText),
            grade: text(source.grade, LIMITS.shortText),
            descriptionOrCoursework: text(source.descriptionOrCoursework, LIMITS.longText)
        };
    }

    function normalizeExperience(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'experience', index),
            companyName: text(source.companyName, LIMITS.shortText),
            jobTitle: text(source.jobTitle, LIMITS.shortText),
            employmentType: text(source.employmentType, LIMITS.shortText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyWorking: boolean(source.isCurrentlyWorking),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeProject(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'project', index),
            projectName: text(source.projectName, LIMITS.shortText),
            projectUrl: text(source.projectUrl, LIMITS.url),
            repositoryUrl: text(source.repositoryUrl, LIMITS.url),
            technologiesUsed: uniqueStrings(source.technologiesUsed, LIMITS.tags, LIMITS.tag),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isOngoing: boolean(source.isOngoing),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeSkill(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'skills', index),
            name: text(source.name, LIMITS.shortText),
            skills: uniqueStrings(source.skills, LIMITS.tags, LIMITS.tag)
        };
    }

    function normalizeAchievement(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'achievement', index),
            title: text(source.title, LIMITS.shortText),
            issuingOrganization: text(source.issuingOrganization, LIMITS.shortText),
            date: text(source.date, 7),
            credentialUrl: text(source.credentialUrl, LIMITS.url),
            description: text(source.description, LIMITS.longText)
        };
    }

    function normalizeLanguage(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'language', index),
            name: text(source.name, LIMITS.shortText),
            proficiency: text(source.proficiency, LIMITS.shortText)
        };
    }

    function normalizeVolunteer(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'volunteer', index),
            organizationName: text(source.organizationName, LIMITS.shortText),
            role: text(source.role, LIMITS.shortText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrentlyVolunteering: boolean(source.isCurrentlyVolunteering),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeCustomEntry(item, index, sectionIndex) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'custom-' + String(sectionIndex + 1), index),
            heading: text(source.heading, LIMITS.shortText),
            subheading: text(source.subheading, LIMITS.shortText),
            location: text(source.location, LIMITS.shortText),
            startDate: text(source.startDate, 7),
            endDate: text(source.endDate, 7),
            isCurrent: boolean(source.isCurrent),
            url: text(source.url, LIMITS.url),
            bulletPoints: stringList(source.bulletPoints, LIMITS.bullets, LIMITS.bullet)
        };
    }

    function normalizeCustomSection(item, index) {
        const source = isRecord(item) ? item : {};
        return {
            id: safeId(source.id, 'custom-section', index),
            title: text(source.title, LIMITS.shortText),
            entries: array(source.entries, LIMITS.customEntries).map(function (entry, entryIndex) {
                return normalizeCustomEntry(entry, entryIndex, index);
            })
        };
    }

    function normalizeSections(value) {
        const byKey = new Map(SECTION_DEFAULTS.map(function (section) { return [section.key, section]; }));
        const result = [];
        const used = new Set();
        array(value, SECTION_DEFAULTS.length).forEach(function (item) {
            if (!isRecord(item)) return;
            const key = text(item.key, 60);
            if (!SECTION_KEYS.has(key) || used.has(key)) return;
            const fallback = byKey.get(key);
            used.add(key);
            result.push({
                key: key,
                title: text(item.title, LIMITS.shortText) || fallback.title,
                isVisible: typeof item.isVisible === 'boolean' ? item.isVisible : fallback.isVisible
            });
        });
        SECTION_DEFAULTS.forEach(function (section) {
            if (!used.has(section.key)) result.push(Object.assign({}, section));
        });
        return result;
    }

    function normalizeState(value) {
        const source = isRecord(value) ? value : {};
        return {
            schemaVersion: SCHEMA_VERSION,
            personalInformation: normalizePersonal(source.personalInformation),
            professionalSummary: text(source.professionalSummary, LIMITS.summary),
            education: array(source.education, LIMITS.entries).map(normalizeEducation),
            experience: array(source.experience, LIMITS.entries).map(normalizeExperience),
            projects: array(source.projects, LIMITS.entries).map(normalizeProject),
            skills: array(source.skills, LIMITS.entries).map(normalizeSkill),
            achievementsAndCertifications: array(source.achievementsAndCertifications, LIMITS.entries).map(normalizeAchievement),
            languages: array(source.languages, LIMITS.entries).map(normalizeLanguage),
            volunteerExperience: array(source.volunteerExperience, LIMITS.entries).map(normalizeVolunteer),
            interests: uniqueStrings(source.interests, LIMITS.tags, LIMITS.tag),
            customSections: array(source.customSections, LIMITS.customSections).map(normalizeCustomSection),
            sections: normalizeSections(source.sections)
        };
    }

    function parseDraft(serialized) {
        if (typeof serialized !== 'string' || !serialized.trim()) {
            return { state: createEmptyState(), recovered: false, reason: 'empty' };
        }
        if (serialized.length > LIMITS.draftBytes) {
            return { state: createEmptyState(), recovered: true, reason: 'too-large' };
        }
        try {
            const parsed = JSON.parse(serialized);
            if (!isRecord(parsed)) {
                return { state: createEmptyState(), recovered: true, reason: 'invalid-shape' };
            }
            return { state: normalizeState(parsed), recovered: false, reason: null };
        } catch (_error) {
            return { state: createEmptyState(), recovered: true, reason: 'invalid-json' };
        }
    }

    function isEmail(value) {
        const candidate = text(value, LIMITS.email);
        if (!candidate || candidate.length > LIMITS.email || candidate.includes('..')) return false;
        const parts = candidate.split('@');
        if (parts.length !== 2) return false;
        const local = parts[0];
        const domain = parts[1];
        if (!local || local.length > 64 || local.startsWith('.') || local.endsWith('.') || !/^[A-Za-z0-9!#$%&'*+/=?^_`{|}~.-]+$/.test(local)) return false;
        if (!domain || domain.length > 253 || !domain.includes('.')) return false;
        const labels = domain.split('.');
        if (labels.some(function (label) { return !/^[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?$/.test(label); })) return false;
        const topLevel = labels[labels.length - 1];
        return /^[A-Za-z]{2,63}$/.test(topLevel) || /^xn--[A-Za-z0-9-]{2,59}$/i.test(topLevel);
    }

    function isPhone(value) {
        const candidate = text(value, 40);
        if (!candidate || !/^\+?[0-9().\-\s]+$/.test(candidate)) return false;
        const digits = candidate.replace(/\D/g, '');
        return digits.length >= 7 && digits.length <= 15;
    }

    function parseHttpUrl(value) {
        const candidate = text(value, LIMITS.url);
        if (!candidate) return null;
        try {
            const parsed = new URL(candidate);
            if ((parsed.protocol !== 'http:' && parsed.protocol !== 'https:') || !parsed.hostname || parsed.username || parsed.password) return null;
            return parsed.href;
        } catch (_error) {
            return null;
        }
    }

    function isHttpUrl(value) {
        return parseHttpUrl(value) !== null;
    }

    function isMonth(value) {
        const match = /^(\d{4})-(\d{2})$/.exec(text(value, 7));
        if (!match) return false;
        const month = Number(match[2]);
        return Number(match[1]) >= 1900 && Number(match[1]) <= 2200 && month >= 1 && month <= 12;
    }

    function addDateRangeErrors(errors, basePath, entry, currentProperty) {
        if (entry.startDate && !isMonth(entry.startDate)) errors[basePath + '.startDate'] = 'Choose a valid start month.';
        if (!entry[currentProperty] && entry.endDate && !isMonth(entry.endDate)) errors[basePath + '.endDate'] = 'Choose a valid end month.';
        if (!entry[currentProperty] && isMonth(entry.startDate) && isMonth(entry.endDate) && entry.endDate < entry.startDate) {
            errors[basePath + '.endDate'] = 'End month cannot be before the start month.';
        }
    }

    function validateState(value) {
        const state = normalizeState(value);
        const errors = {};
        const personal = state.personalInformation;
        if (!personal.fullName) errors['personalInformation.fullName'] = 'Full name is required.';
        if (!personal.professionalTitle) errors['personalInformation.professionalTitle'] = 'Professional title is required.';
        if (!personal.emailAddress) errors['personalInformation.emailAddress'] = 'Email address is required.';
        else if (!isEmail(personal.emailAddress)) errors['personalInformation.emailAddress'] = 'Enter a valid email address.';
        if (personal.phoneNumber && !isPhone(personal.phoneNumber)) errors['personalInformation.phoneNumber'] = 'Enter a valid phone number with 7 to 15 digits.';
        ['linkedInUrl', 'gitHubUrl', 'portfolioUrl'].forEach(function (field) {
            if (personal[field] && !isHttpUrl(personal[field])) errors['personalInformation.' + field] = 'Enter a complete http:// or https:// URL.';
        });

        state.education.forEach(function (entry, index) { addDateRangeErrors(errors, 'education.' + index, entry, 'isCurrentlyStudying'); });
        state.experience.forEach(function (entry, index) { addDateRangeErrors(errors, 'experience.' + index, entry, 'isCurrentlyWorking'); });
        state.projects.forEach(function (entry, index) {
            const base = 'projects.' + index;
            addDateRangeErrors(errors, base, entry, 'isOngoing');
            ['projectUrl', 'repositoryUrl'].forEach(function (field) {
                if (entry[field] && !isHttpUrl(entry[field])) errors[base + '.' + field] = 'Enter a complete http:// or https:// URL.';
            });
        });
        state.achievementsAndCertifications.forEach(function (entry, index) {
            const base = 'achievementsAndCertifications.' + index;
            if (entry.date && !isMonth(entry.date)) errors[base + '.date'] = 'Choose a valid month.';
            if (entry.credentialUrl && !isHttpUrl(entry.credentialUrl)) errors[base + '.credentialUrl'] = 'Enter a complete http:// or https:// URL.';
        });
        state.volunteerExperience.forEach(function (entry, index) { addDateRangeErrors(errors, 'volunteerExperience.' + index, entry, 'isCurrentlyVolunteering'); });
        state.customSections.forEach(function (section, sectionIndex) {
            section.entries.forEach(function (entry, entryIndex) {
                const base = 'customSections.' + sectionIndex + '.entries.' + entryIndex;
                addDateRangeErrors(errors, base, entry, 'isCurrent');
                if (entry.url && !isHttpUrl(entry.url)) errors[base + '.url'] = 'Enter a complete http:// or https:// URL.';
            });
        });
        return errors;
    }

    function formatMonth(value) {
        if (!isMonth(value)) return '';
        const parts = value.split('-');
        return MONTHS[Number(parts[1]) - 1] + ' ' + parts[0];
    }

    function formatDateRange(startDate, endDate, isCurrent) {
        const start = formatMonth(startDate);
        const end = isCurrent ? 'Present' : formatMonth(endDate);
        if (start && end) return start + ' - ' + end;
        return start || end;
    }

    function compact(values, separator) {
        return values.map(function (value) { return text(value, LIMITS.longText); }).filter(Boolean).join(separator || ' · ');
    }

    function hasText(value) {
        return typeof value === 'string' && value.trim().length > 0;
    }

    function hasEntryContent(entry) {
        if (!isRecord(entry)) return false;
        return Object.keys(entry).some(function (key) {
            if (key === 'id' || key.startsWith('isCurrent') || key === 'isOngoing') return false;
            const value = entry[key];
            return hasText(value) || (Array.isArray(value) && value.some(function (item) { return hasText(item) || hasEntryContent(item); }));
        });
    }

    function hasSectionContent(state, key) {
        if (key === 'professionalSummary') return hasText(state.professionalSummary);
        if (key === 'interests') return state.interests.some(hasText);
        const value = state[key];
        return Array.isArray(value) && value.some(hasEntryContent);
    }

    function richTextRuns(value, maximum) {
        const source = text(value, maximum || LIMITS.longText);
        const runs = [];
        const active = { b: 0, i: 0, u: 0 };
        const marker = /\[(\/?)(b|i|u)\]/gi;
        let position = 0;
        let match;

        function append(content) {
            if (!content) return;
            const run = { text: content };
            if (active.b > 0) run.bold = true;
            if (active.i > 0) run.italics = true;
            if (active.u > 0) run.decoration = 'underline';
            runs.push(run);
        }

        while ((match = marker.exec(source)) !== null) {
            append(source.slice(position, match.index));
            const tag = match[2].toLowerCase();
            if (match[1]) {
                if (active[tag] > 0) active[tag] -= 1;
                else append(match[0]);
            } else {
                active[tag] += 1;
            }
            position = marker.lastIndex;
        }
        append(source.slice(position));
        return runs;
    }

    function plainRichText(value, maximum) {
        return richTextRuns(value, maximum).map(function (run) { return run.text; }).join('');
    }

    function pdfRichText(value, options, maximum) {
        return Object.assign({ text: richTextRuns(value, maximum || LIMITS.longText) }, options || {});
    }

    function estimatedSummaryLines(value) {
        const plain = plainRichText(value, LIMITS.summary);
        if (!plain.trim()) return 0;
        return plain.split('\n').reduce(function (total, line) {
            const length = line.trim().length;
            return total + Math.max(1, Math.ceil(length / 105));
        }, 0);
    }

    function onePageGuidance(value) {
        const state = normalizeState(value);
        const visible = new Set(state.sections.filter(function (section) { return section.isVisible; }).map(function (section) { return section.key; }));
        const metrics = {
            professionalSummary: { value: visible.has('professionalSummary') ? estimatedSummaryLines(state.professionalSummary) : 0, maximum: ONE_PAGE_RECOMMENDATIONS.professionalSummary, label: 'summary lines' },
            education: { value: visible.has('education') ? state.education.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.education, label: 'education entries' },
            experience: { value: visible.has('experience') ? state.experience.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.experience, label: 'experience entries' },
            projects: { value: visible.has('projects') ? state.projects.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.projects, label: 'projects' },
            achievementsAndCertifications: { value: visible.has('achievementsAndCertifications') ? state.achievementsAndCertifications.filter(hasEntryContent).length : 0, maximum: ONE_PAGE_RECOMMENDATIONS.achievementsAndCertifications, label: 'achievements' }
        };
        const exceeded = Object.keys(metrics).filter(function (key) { return metrics[key].value > metrics[key].maximum; });
        return { metrics: metrics, exceeded: exceeded };
    }

    function evaluateDraftReadiness(value) {
        const state = normalizeState(value);
        const validationErrors = validateState(state);
        const visibleSections = new Set(state.sections.filter(function (section) { return section.isVisible; }).map(function (section) { return section.key; }));
        const checks = [];

        function addCheck(key, title, points, maximum, message) {
            const boundedPoints = Math.max(0, Math.min(maximum, Math.round(points)));
            checks.push({
                key: key,
                title: title,
                status: boundedPoints === maximum ? 'ready' : boundedPoints > 0 ? 'attention' : 'missing',
                points: boundedPoints,
                maximum: maximum,
                message: message
            });
        }

        const personal = state.personalInformation;
        const essentialCount = [personal.fullName, personal.professionalTitle, isEmail(personal.emailAddress) ? personal.emailAddress : ''].filter(Boolean).length;
        const personalErrorCount = Object.keys(validationErrors).filter(function (path) { return path.startsWith('personalInformation.'); }).length;
        if (essentialCount === 3 && personalErrorCount === 0) {
            addCheck('essentials', 'Contact essentials', 20, 20, 'Name, title, and contact details are ready.');
        } else if (essentialCount === 3) {
            addCheck('essentials', 'Contact essentials', 14, 20, 'Fix the highlighted phone, email, or link format.');
        } else {
            addCheck('essentials', 'Contact essentials', essentialCount * 6, 20, 'Add your name, professional title, and a valid email.');
        }

        const summaryLength = visibleSections.has('professionalSummary') ? plainRichText(state.professionalSummary, LIMITS.summary).trim().length : 0;
        if (summaryLength >= 300 && summaryLength <= 650) {
            addCheck('summary', 'Focused summary', 15, 15, 'The summary is concise and easy to scan.');
        } else if (summaryLength >= 150 && summaryLength <= 900) {
            addCheck('summary', 'Focused summary', 10, 15, 'Aim for roughly 300-600 characters with role-relevant strengths.');
        } else if (summaryLength >= 60) {
            addCheck('summary', 'Focused summary', 6, 15, 'Tighten the summary to a focused 300-600 characters.');
        } else {
            addCheck('summary', 'Focused summary', 0, 15, 'Add a short summary of your strengths and target work.');
        }

        const evidenceEntries = (visibleSections.has('experience') ? state.experience.filter(hasEntryContent).length : 0)
            + (visibleSections.has('projects') ? state.projects.filter(hasEntryContent).length : 0)
            + (visibleSections.has('volunteerExperience') ? state.volunteerExperience.filter(hasEntryContent).length : 0);
        const educationEntries = visibleSections.has('education') ? state.education.filter(hasEntryContent).length : 0;
        if (evidenceEntries >= 2) {
            addCheck('evidence', 'Relevant evidence', 20, 20, 'Experience and projects give recruiters concrete evidence.');
        } else if (evidenceEntries === 1) {
            addCheck('evidence', 'Relevant evidence', 14, 20, 'Add another relevant role, project, or volunteer example.');
        } else if (educationEntries > 0) {
            addCheck('evidence', 'Relevant evidence', 7, 20, 'Add a project or experience entry to support your education.');
        } else {
            addCheck('evidence', 'Relevant evidence', 0, 20, 'Add education plus a relevant project or experience entry.');
        }

        const bullets = [];
        if (visibleSections.has('experience')) state.experience.forEach(function (entry) { Array.prototype.push.apply(bullets, entry.bulletPoints); });
        if (visibleSections.has('projects')) state.projects.forEach(function (entry) { Array.prototype.push.apply(bullets, entry.bulletPoints); });
        if (visibleSections.has('volunteerExperience')) state.volunteerExperience.forEach(function (entry) { Array.prototype.push.apply(bullets, entry.bulletPoints); });
        if (visibleSections.has('customSections')) {
            state.customSections.forEach(function (section) {
                section.entries.forEach(function (entry) { Array.prototype.push.apply(bullets, entry.bulletPoints); });
            });
        }
        const meaningfulBullets = bullets.map(function (bullet) { return plainRichText(bullet, LIMITS.bullet).trim(); }).filter(Boolean);
        const actionBullets = meaningfulBullets.filter(function (bullet) {
            return /^(achieved|automated|built|co-led|created|delivered|designed|developed|enabled|implemented|improved|increased|launched|led|managed|optimized|reduced|resolved|shipped|streamlined)\b/i.test(bullet);
        });
        const quantifiedBullets = meaningfulBullets.filter(function (bullet) { return /(?:\b\d+(?:\.\d+)?%?\b|\$\s?\d+)/.test(bullet); });
        if (meaningfulBullets.length >= 4 && actionBullets.length >= 2) {
            addCheck('bullets', 'Impact bullets', 20, 20, quantifiedBullets.length ? 'Bullets use actions and measurable evidence.' : 'Strong action-led bullets are in place; add metrics where truthful.');
        } else if (meaningfulBullets.length >= 3) {
            addCheck('bullets', 'Impact bullets', 15, 20, 'Start more bullets with strong actions and add outcomes where truthful.');
        } else if (meaningfulBullets.length > 0) {
            addCheck('bullets', 'Impact bullets', 8, 20, 'Add at least three concise achievement or project bullets.');
        } else {
            addCheck('bullets', 'Impact bullets', 0, 20, 'Add action-led bullets showing what you delivered.');
        }

        const skillNames = [];
        if (visibleSections.has('skills')) state.skills.forEach(function (category) { Array.prototype.push.apply(skillNames, category.skills); });
        if (visibleSections.has('projects')) state.projects.forEach(function (project) { Array.prototype.push.apply(skillNames, project.technologiesUsed); });
        const uniqueSkills = new Set(skillNames.map(function (skill) { return skill.trim().toLocaleLowerCase(); }).filter(Boolean));
        if (uniqueSkills.size >= 8) {
            addCheck('skills', 'Searchable skills', 15, 15, 'Skills and technologies are easy for ATS search to find.');
        } else if (uniqueSkills.size >= 5) {
            addCheck('skills', 'Searchable skills', 11, 15, 'Add a few more specific tools or technologies you genuinely use.');
        } else if (uniqueSkills.size >= 2) {
            addCheck('skills', 'Searchable skills', 6, 15, 'Build clear skill categories with specific tools and technologies.');
        } else {
            addCheck('skills', 'Searchable skills', 0, 15, 'Add a skills category with role-relevant keywords.');
        }

        const visibleContentCount = state.sections.filter(function (section) {
            return section.isVisible && hasSectionContent(state, section.key);
        }).length;
        const nonPersonalErrors = Object.keys(validationErrors).filter(function (path) { return !path.startsWith('personalInformation.'); }).length;
        if (visibleContentCount >= 4 && nonPersonalErrors === 0) {
            addCheck('structure', 'Clean structure', 10, 10, 'Visible sections are populated and dates are consistent.');
        } else if (visibleContentCount >= 3 && nonPersonalErrors === 0) {
            addCheck('structure', 'Clean structure', 7, 10, 'Add one more useful section or supporting detail.');
        } else if (visibleContentCount > 0) {
            addCheck('structure', 'Clean structure', nonPersonalErrors ? 2 : 4, 10, nonPersonalErrors ? 'Fix the highlighted date or URL issues.' : 'Populate at least three relevant sections.');
        } else {
            addCheck('structure', 'Clean structure', 0, 10, 'Populate and show the sections that support your application.');
        }

        const score = checks.reduce(function (total, check) { return total + check.points; }, 0);
        const readyCount = checks.filter(function (check) { return check.status === 'ready'; }).length;
        const tone = score >= 85 ? 'strong' : score >= 60 ? 'growing' : 'starting';
        const label = tone === 'strong' ? 'Ready to tailor' : tone === 'growing' ? 'Strong foundation' : 'Keep building';
        const warnings = checks.filter(function (check) { return check.status !== 'ready'; }).map(function (check) { return check.message; }).slice(0, 3);
        return {
            score: score,
            readyCount: readyCount,
            totalChecks: checks.length,
            tone: tone,
            label: label,
            summary: readyCount + ' of ' + checks.length + ' local checks ready. ' + label + '.',
            checks: checks,
            warnings: warnings
        };
    }

    async function countPdfPages(blob) {
        if (!blob || typeof blob.arrayBuffer !== 'function') throw new Error('A PDF Blob is required.');
        const bytes = new Uint8Array(await blob.arrayBuffer());
        let source = '';
        const chunkSize = 32768;
        for (let index = 0; index < bytes.length; index += chunkSize) {
            source += String.fromCharCode.apply(null, bytes.subarray(index, Math.min(index + chunkSize, bytes.length)));
        }
        const pages = source.match(/\/Type\s*\/Page\b/g);
        if (!pages || !pages.length) throw new Error('The generated PDF page count could not be read.');
        return pages.length;
    }

    function pdfText(value, options) {
        return Object.assign({ text: text(value, LIMITS.longText) }, options || {});
    }

    function linkedText(label, url, options) {
        const safeUrl = parseHttpUrl(url);
        const node = pdfText(label || url, Object.assign({ color: '#0000EE', decoration: 'underline' }, options || {}));
        if (safeUrl) node.link = safeUrl;
        return node;
    }

    function rightDate(value) {
        return pdfText(value, { alignment: 'right', italics: true, color: '#333333', noWrap: true });
    }

    function headingColumns(left, right) {
        const columns = [{ width: '*', text: left || '' }];
        if (right) columns.push({ width: 'auto', text: right, alignment: 'right', italics: true, color: '#333333', noWrap: true });
        return { columns: columns, columnGap: 8, margin: [0, 0, 0, 1] };
    }

    function bulletList(items) {
        const bullets = items.map(function (item) { return text(item, LIMITS.bullet); }).filter(function (item) { return plainRichText(item, LIMITS.bullet).trim(); });
        return bullets.length ? {
            stack: bullets.map(function (item) {
                return {
                    columns: [
                        { width: 8, text: '-', alignment: 'right' },
                        { width: '*', text: richTextRuns(item, LIMITS.bullet) }
                    ],
                    columnGap: 4,
                    margin: [0, 0, 0, 0.5]
                };
            }),
            margin: [12, 0.5, 0, 1],
            lineHeight: 1.03
        } : null;
    }

    function sectionHeading(title) {
        return {
            stack: [
                pdfText(title, { style: 'sectionHeading' }),
                { canvas: [{ type: 'line', x1: 0, y1: 0, x2: 523, y2: 0, lineWidth: 0.55, lineColor: '#111111' }], margin: [0, 0.5, 0, 2.5] }
            ],
            margin: [0, 2.5, 0, 0]
        };
    }

    function educationNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const degree = compact([entry.degree, entry.fieldOfStudy], ', ');
            const title = degree || entry.institutionName;
            const secondary = degree ? entry.institutionName : '';
            const details = compact([entry.location, entry.grade], ' | ');
            const stack = [headingColumns(pdfText(title, { bold: true }), formatDateRange(entry.startDate, entry.endDate, entry.isCurrentlyStudying))];
            if (secondary) stack.push(pdfText(secondary, { italics: true }));
            if (details) stack.push(pdfText(details, { color: '#333333' }));
            if (entry.descriptionOrCoursework) stack.push(pdfRichText(entry.descriptionOrCoursework));
            return { stack: stack, margin: [0, 0, 0, 2.5] };
        });
    }

    function experienceNodes(entries, volunteer) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const organization = volunteer ? entry.organizationName : entry.companyName;
            const role = volunteer ? entry.role : entry.jobTitle;
            const current = volunteer ? entry.isCurrentlyVolunteering : entry.isCurrentlyWorking;
            const titleParts = [];
            if (role) titleParts.push({ text: role, bold: true });
            if (role && organization) titleParts.push({ text: ' - ' });
            if (organization) titleParts.push({ text: organization, bold: !role });
            const stack = [headingColumns(titleParts, formatDateRange(entry.startDate, entry.endDate, current))];
            const metadata = compact(volunteer ? [entry.location] : [entry.employmentType, entry.location], ' | ');
            if (metadata) stack.push(pdfText(metadata, { italics: true, color: '#333333' }));
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function projectNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const technology = entry.technologiesUsed.filter(hasText).join(', ');
            const left = [];
            if (entry.projectName) left.push({ text: entry.projectName, bold: true });
            if (technology) left.push({ text: (entry.projectName ? ' | ' : '') + technology, italics: true });
            const stack = [headingColumns(left, formatDateRange(entry.startDate, entry.endDate, entry.isOngoing))];
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            const links = [];
            if (parseHttpUrl(entry.projectUrl)) links.push(linkedText('Live project', entry.projectUrl));
            if (parseHttpUrl(entry.repositoryUrl)) links.push(linkedText('Repository', entry.repositoryUrl));
            if (links.length) {
                const joined = [];
                links.forEach(function (link, index) {
                    if (index) joined.push({ text: ' | ', color: '#333333' });
                    joined.push(link);
                });
                stack.push({ text: joined, margin: [12, 0, 0, 2] });
            }
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function skillNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const line = [];
            if (entry.name) line.push({ text: entry.name + (entry.skills.length ? ': ' : ''), bold: true });
            if (entry.skills.length) line.push({ text: entry.skills.filter(hasText).join(', ') });
            return { text: line, margin: [0, 0, 0, 1] };
        });
    }

    function achievementNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const left = [];
            if (entry.title) left.push({ text: entry.title, bold: true });
            if (entry.issuingOrganization) left.push({ text: (entry.title ? ' - ' : '') + entry.issuingOrganization });
            const stack = [headingColumns(left, formatMonth(entry.date))];
            if (entry.description) stack.push(pdfRichText(entry.description));
            if (parseHttpUrl(entry.credentialUrl)) stack.push(linkedText('View credential', entry.credentialUrl));
            return { stack: stack, margin: [0, 0, 0, 2] };
        });
    }

    function languageNodes(entries) {
        const values = entries.filter(hasEntryContent).map(function (entry) {
            return compact([entry.name, entry.proficiency], ' - ');
        }).filter(Boolean);
        return values.length ? [{ text: values.join(' | '), margin: [0, 0, 0, 2] }] : [];
    }

    function customEntryNodes(entries) {
        return entries.filter(hasEntryContent).map(function (entry) {
            const left = [];
            if (entry.heading) left.push({ text: entry.heading, bold: true });
            if (entry.subheading) left.push({ text: (entry.heading ? ' - ' : '') + entry.subheading });
            const stack = [headingColumns(left, formatDateRange(entry.startDate, entry.endDate, entry.isCurrent))];
            if (entry.location) stack.push(pdfText(entry.location, { italics: true, color: '#333333' }));
            if (parseHttpUrl(entry.url)) stack.push(linkedText('Link', entry.url));
            const bullets = bulletList(entry.bulletPoints);
            if (bullets) stack.push(bullets);
            return { stack: stack, margin: [0, 0, 0, 3] };
        });
    }

    function nodesForSection(state, descriptor) {
        switch (descriptor.key) {
            case 'professionalSummary':
                return state.professionalSummary.split(/\n\s*\n/).map(function (paragraph) { return text(paragraph, LIMITS.summary); }).filter(Boolean).map(function (paragraph) {
                    return pdfRichText(paragraph, { margin: [0, 0, 0, 2] }, LIMITS.summary);
                });
            case 'education': return educationNodes(state.education);
            case 'skills': return skillNodes(state.skills);
            case 'experience': return experienceNodes(state.experience, false);
            case 'projects': return projectNodes(state.projects);
            case 'achievementsAndCertifications': return achievementNodes(state.achievementsAndCertifications);
            case 'languages': return languageNodes(state.languages);
            case 'volunteerExperience': return experienceNodes(state.volunteerExperience, true);
            case 'interests': return state.interests.length ? [pdfText(state.interests.join(' | '), { margin: [0, 0, 0, 2] })] : [];
            default: return [];
        }
    }

    function contactLink(label, link) {
        return { text: label, link: link, color: '#0000EE', decoration: 'underline', alignment: 'right' };
    }

    function buildDocumentDefinition(value) {
        const state = normalizeState(value);
        const personal = state.personalInformation;
        const contact = [];
        if (personal.phoneNumber || personal.location) {
            const phoneLocation = [];
            if (personal.phoneNumber) {
                const phone = { text: personal.phoneNumber };
                if (isPhone(personal.phoneNumber)) {
                    phone.link = 'tel:' + personal.phoneNumber.replace(/[^+\d]/g, '');
                    phone.color = '#0000EE';
                    phone.decoration = 'underline';
                }
                phoneLocation.push(phone);
            }
            if (personal.phoneNumber && personal.location) phoneLocation.push({ text: ' | ' });
            if (personal.location) phoneLocation.push({ text: personal.location });
            contact.push({ text: phoneLocation, alignment: 'right' });
        }
        if (isEmail(personal.emailAddress)) contact.push(contactLink(personal.emailAddress, 'mailto:' + personal.emailAddress));
        [
            ['LinkedIn', personal.linkedInUrl],
            ['GitHub', personal.gitHubUrl],
            ['Portfolio', personal.portfolioUrl]
        ].forEach(function (entry) {
            const url = parseHttpUrl(entry[1]);
            if (url) contact.push(contactLink(entry[0], url));
        });

        const identity = [];
        if (personal.fullName) identity.push(pdfText(personal.fullName, { style: 'name' }));
        if (personal.professionalTitle) identity.push(pdfText(personal.professionalTitle, { style: 'professionalTitle' }));
        const content = [{
            columns: [
                {
                    width: '*',
                    stack: identity.length ? identity : [{ text: '' }]
                },
                { width: 210, stack: contact, margin: [12, 1, 0, 0] }
            ],
            columnGap: 10
        }, {
            canvas: [{ type: 'line', x1: 0, y1: 0, x2: 523, y2: 0, lineWidth: 0.75, lineColor: '#111111' }],
            margin: [0, 5, 0, 1]
        }];

        state.sections.forEach(function (descriptor) {
            if (!descriptor.isVisible || !hasSectionContent(state, descriptor.key)) return;
            if (descriptor.key === 'customSections') {
                state.customSections.filter(hasEntryContent).forEach(function (section) {
                    const nodes = customEntryNodes(section.entries);
                    if (!nodes.length) return;
                    content.push(sectionHeading(section.title || descriptor.title));
                    Array.prototype.push.apply(content, nodes);
                });
                return;
            }
            const nodes = nodesForSection(state, descriptor);
            if (!nodes.length) return;
            content.push(sectionHeading(descriptor.title));
            Array.prototype.push.apply(content, nodes);
        });

        return {
            pageSize: 'A4',
            pageMargins: [36, 27, 36, 27],
            info: {
                title: (personal.fullName || 'ApplyWise') + ' Resume',
                author: personal.fullName || 'ApplyWise Resume Builder',
                subject: 'Professional resume'
            },
            content: content,
            defaultStyle: { font: 'Roboto', fontSize: 9.1, color: '#111111', lineHeight: 1.07 },
            styles: {
                name: { fontSize: 22.5, bold: true, characterSpacing: 0.15, lineHeight: 1 },
                professionalTitle: { fontSize: 10, color: '#333333', margin: [0, 1, 0, 0] },
                sectionHeading: { fontSize: 11, bold: true, characterSpacing: 0.3, color: '#111111' }
            }
        };
    }

    function buildFilename(fullName) {
        let normalized = text(fullName, LIMITS.name);
        try { normalized = normalized.normalize('NFKD').replace(/[\u0300-\u036f]/g, ''); } catch (_error) { /* Old browsers. */ }
        const parts = normalized.replace(/[^A-Za-z0-9\s_-]/g, ' ').split(/\s+/).map(function (part) {
            return part.replace(/[^A-Za-z0-9_-]/g, '');
        }).filter(Boolean);
        const selected = parts.length > 1 ? [parts[0], parts[parts.length - 1]] : parts;
        const stem = selected.join('_').slice(0, 70) || 'ApplyWise';
        return stem + '_Resume.pdf';
    }

    function createSampleState() {
        return normalizeState({
            schemaVersion: SCHEMA_VERSION,
            personalInformation: {
                fullName: 'Jordan Lee',
                professionalTitle: 'Product-Minded Software Engineer',
                phoneNumber: '+1 202 555 0147',
                emailAddress: 'jordan.lee@example.com',
                location: 'Seattle, WA',
                linkedInUrl: 'https://example.com/jordan-linkedin',
                gitHubUrl: 'https://example.com/jordan-github',
                portfolioUrl: 'https://example.com/jordan-portfolio'
            },
            professionalSummary: '[b]Product-minded software engineer[/b] building accessible full-stack applications with React, .NET, and PostgreSQL. Turns ambiguous requirements into reliable features, communicates clearly across teams, and improves delivery through pragmatic testing and automation.',
            education: [
                { id: 'education-northbridge', institutionName: 'Northbridge University', degree: 'Bachelor of Science', fieldOfStudy: 'Computer Science', startDate: '2023-08', endDate: '2027-06', location: 'Seattle, WA', grade: 'GPA: 3.6 / 4.0', descriptionOrCoursework: 'Relevant coursework: [i]Data Structures, Algorithms, Databases, Operating Systems[/i]' },
                { id: 'education-harbor', institutionName: 'Harbor College', degree: 'Intermediate', fieldOfStudy: 'Pre-Engineering', startDate: '2020-08', endDate: '2022-06', location: 'Seattle, WA', grade: '90%', descriptionOrCoursework: 'Mathematics, Physics, and Chemistry' }
            ],
            experience: [
                { id: 'experience-codesprint', companyName: 'CodeSprint - Northbridge University', jobTitle: 'Co-Head', employmentType: 'Leadership', location: 'Seattle, WA', startDate: '2025-03', endDate: '2026-01', bulletPoints: ['Co-led a full-stack competition platform supporting registration, problem access, and [b]real-time results[/b].'] },
                { id: 'experience-workshops', companyName: 'Open Source Society', jobTitle: 'Workshop Lead', employmentType: 'Volunteer', location: 'Seattle, WA', startDate: '2024-08', isCurrentlyWorking: true, bulletPoints: ['Lead practical Git and GitHub workshops for student development teams.'] },
                { id: 'experience-intern', companyName: 'Example Labs', jobTitle: 'Software Engineering Intern', employmentType: 'Internship', location: 'Remote', startDate: '2024-06', endDate: '2024-08', bulletPoints: ['Shipped tested API and dashboard improvements that reduced manual reporting work.'] }
            ],
            projects: [
                { id: 'project-campus-collab', projectName: 'Campus Collaboration Hub', projectUrl: 'https://example.com/campus-hub', repositoryUrl: 'https://example.com/campus-hub-source', technologiesUsed: ['Next.js', 'Python', 'PostgreSQL'], startDate: '2025-01', isOngoing: true, bulletPoints: ['Connected university projects with skill-matched contributors across batches.'] },
                { id: 'project-investments', projectName: 'Investment Recommender', repositoryUrl: 'https://example.com/investments-source', technologiesUsed: ['Python', 'Pandas', 'Streamlit'], startDate: '2024-08', endDate: '2024-12', bulletPoints: ['Built recommendation filters and interactive portfolio visualizations.'] },
                { id: 'project-scoreboard', projectName: 'CodeVerse Scoreboard', repositoryUrl: 'https://example.com/scoreboard-source', technologiesUsed: ['React', 'TypeScript', 'WebSockets'], startDate: '2024-03', endDate: '2024-07', bulletPoints: ['Automated live standings and delivered score changes through [u]WebSockets[/u].'] }
            ],
            skills: [
                { id: 'skills-languages', name: 'Languages', skills: ['JavaScript', 'TypeScript', 'Python', 'C++', 'SQL'] },
                { id: 'skills-frameworks', name: 'Frameworks', skills: ['React', 'Next.js', '.NET', 'Node.js'] },
                { id: 'skills-databases', name: 'Databases', skills: ['PostgreSQL', 'MongoDB', 'Redis'] },
                { id: 'skills-tools', name: 'Tools', skills: ['Git', 'Linux', 'Docker', 'Figma'] }
            ],
            achievementsAndCertifications: [
                { id: 'achievement-coder-cup', title: 'Coder Cup Runner-up', issuingOrganization: 'Northbridge University', date: '2025-02', description: 'Runner-up in the university programming competition.' },
                { id: 'achievement-reader', title: 'Best Reader', issuingOrganization: 'Harbor College', date: '2021-05', description: 'Recognized for consistent academic contribution.' },
                { id: 'achievement-web', title: 'Responsive Web Design', issuingOrganization: 'freeCodeCamp', date: '2024-01', description: 'Completed accessible HTML and responsive CSS coursework.' }
            ],
            languages: [{ id: 'language-english', name: 'English', proficiency: 'Professional working proficiency' }, { id: 'language-urdu', name: 'Urdu', proficiency: 'Native proficiency' }],
            volunteerExperience: [],
            interests: ['Open-source software', 'Competitive programming', 'System design'],
            customSections: [],
            sections: SECTION_DEFAULTS.map(function (section) { return Object.assign({}, section); })
        });
    }

    // Browser controller is defined below; keeping helpers above DOM-free makes them safe in Node.
    const ALLOWED_PATH_ROOTS = new Set([
        'personalInformation', 'professionalSummary', 'education', 'experience', 'projects', 'skills',
        'achievementsAndCertifications', 'languages', 'volunteerExperience', 'interests', 'customSections', 'sections'
    ]);

    function pathParts(path) {
        if (typeof path !== 'string' || path.length > 180) return null;
        const parts = path.split('.');
        if (!parts.length || !ALLOWED_PATH_ROOTS.has(parts[0])) return null;
        if (parts.some(function (part) { return !part || part === '__proto__' || part === 'prototype' || part === 'constructor' || !/^[A-Za-z][A-Za-z0-9]*$|^\d+$/.test(part); })) return null;
        return parts;
    }

    function getPath(object, path) {
        const parts = pathParts(path);
        if (!parts) return undefined;
        let cursor = object;
        for (let index = 0; index < parts.length; index += 1) {
            if (cursor === null || cursor === undefined) return undefined;
            cursor = cursor[parts[index]];
        }
        return cursor;
    }

    function setPath(object, path, value) {
        const parts = pathParts(path);
        if (!parts) return false;
        let cursor = object;
        for (let index = 0; index < parts.length - 1; index += 1) {
            if (cursor === null || cursor === undefined || typeof cursor !== 'object') return false;
            cursor = cursor[parts[index]];
        }
        if (cursor === null || cursor === undefined || typeof cursor !== 'object') return false;
        cursor[parts[parts.length - 1]] = value;
        return true;
    }

    function moveItem(items, index, offset) {
        const destination = index + offset;
        if (!Array.isArray(items) || index < 0 || index >= items.length || destination < 0 || destination >= items.length) return false;
        const item = items.splice(index, 1)[0];
        items.splice(destination, 0, item);
        return true;
    }

    function domElement(documentObject, tagName, className, content) {
        const element = documentObject.createElement(tagName);
        if (className) element.className = className;
        if (content !== undefined && content !== null) element.textContent = String(content);
        return element;
    }

    function domButton(documentObject, label, action, className) {
        const button = domElement(documentObject, 'button', className || 'aw-rb-text-button', label);
        button.type = 'button';
        button.dataset.action = action;
        return button;
    }

    function replaceChildren(element) {
        while (element && element.firstChild) element.removeChild(element.firstChild);
        if (!element) return;
        for (let index = 1; index < arguments.length; index += 1) element.appendChild(arguments[index]);
    }

    const FIELD_MAXIMUMS = Object.freeze({
        fullName: LIMITS.name,
        professionalTitle: LIMITS.shortText,
        phoneNumber: 40,
        emailAddress: LIMITS.email,
        location: LIMITS.shortText,
        linkedInUrl: LIMITS.url,
        gitHubUrl: LIMITS.url,
        portfolioUrl: LIMITS.url,
        institutionName: LIMITS.shortText,
        degree: LIMITS.shortText,
        fieldOfStudy: LIMITS.shortText,
        grade: LIMITS.shortText,
        descriptionOrCoursework: LIMITS.longText,
        companyName: LIMITS.shortText,
        jobTitle: LIMITS.shortText,
        employmentType: LIMITS.shortText,
        projectName: LIMITS.shortText,
        projectUrl: LIMITS.url,
        repositoryUrl: LIMITS.url,
        name: LIMITS.shortText,
        title: LIMITS.shortText,
        issuingOrganization: LIMITS.shortText,
        credentialUrl: LIMITS.url,
        description: LIMITS.longText,
        proficiency: LIMITS.shortText,
        organizationName: LIMITS.shortText,
        role: LIMITS.shortText,
        heading: LIMITS.shortText,
        subheading: LIMITS.shortText,
        url: LIMITS.url
    });

    const ENTRY_FACTORIES = Object.freeze({
        education: function () { return { id: newId('education'), institutionName: '', degree: '', fieldOfStudy: '', startDate: '', endDate: '', isCurrentlyStudying: false, location: '', grade: '', descriptionOrCoursework: '' }; },
        experience: function () { return { id: newId('experience'), companyName: '', jobTitle: '', employmentType: '', location: '', startDate: '', endDate: '', isCurrentlyWorking: false, bulletPoints: [''] }; },
        projects: function () { return { id: newId('project'), projectName: '', projectUrl: '', repositoryUrl: '', technologiesUsed: [], startDate: '', endDate: '', isOngoing: false, bulletPoints: [''] }; },
        skills: function () { return { id: newId('skills'), name: '', skills: [] }; },
        achievementsAndCertifications: function () { return { id: newId('achievement'), title: '', issuingOrganization: '', date: '', credentialUrl: '', description: '' }; },
        languages: function () { return { id: newId('language'), name: '', proficiency: '' }; },
        volunteerExperience: function () { return { id: newId('volunteer'), organizationName: '', role: '', location: '', startDate: '', endDate: '', isCurrentlyVolunteering: false, bulletPoints: [''] }; },
        customSections: function () { return { id: newId('custom-section'), title: '', entries: [] }; }
    });

    function customEntryFactory() {
        return { id: newId('custom-entry'), heading: '', subheading: '', location: '', startDate: '', endDate: '', isCurrent: false, url: '', bulletPoints: [''] };
    }

    function createBuilder(root, options) {
        if (!root || !root.ownerDocument || root.dataset.resumeBuilderInitialized === 'true') return null;
        root.dataset.resumeBuilderInitialized = 'true';
        const documentObject = root.ownerDocument;
        const windowObject = documentObject.defaultView;
        const configuration = options || {};
        const storageKey = text(root.dataset.draftKey, 180);
        const initialSection = normalizeBuilderSection(root.dataset.initialSection);
        const form = root.querySelector('[data-resume-form]');
        const dynamicEditor = root.querySelector('[data-section-editor]');
        const sectionManager = root.querySelector('[data-section-manager]');
        const previewFrame = root.querySelector('[data-resume-preview]');
        const previewState = root.querySelector('[data-preview-state]');
        const saveStatus = root.querySelector('[data-save-status]');
        const pdfStatus = root.querySelector('[data-pdf-status]');
        const clearDialog = root.querySelector('[data-clear-dialog]');
        const summaryCount = root.querySelector('[data-summary-count]');
        const pageFitStatus = root.querySelector('[data-page-fit-status]');
        const onePageWarning = root.querySelector('[data-one-page-warning]');
        const readinessPanel = root.querySelector('[data-readiness-panel]');
        const readinessMeter = root.querySelector('[data-readiness-meter]');
        const readinessScore = root.querySelector('[data-readiness-score]');
        const readinessSummary = root.querySelector('[data-readiness-summary]');
        const readinessChecks = root.querySelector('[data-readiness-checks]');
        const readinessNext = root.querySelector('[data-readiness-next]');
        const touched = new Set();
        let showAllErrors = false;
        let state = createEmptyState();
        let revision = 0;
        let savedRevision = 0;
        let saveTimer = 0;
        let previewTimer = 0;
        let readinessTimer = 0;
        let initialFocusTimer = 0;
        let targetHighlightTimer = 0;
        let previewRequest = 0;
        let previewPromise = null;
        let currentPreviewUrl = '';
        let latestBlob = null;
        let latestBlobRevision = -1;
        let latestPageCount = 0;
        let destroyed = false;
        let initialSectionHandled = false;
        let storageEnabled = Boolean(storageKey);
        const tabList = root.querySelector('[role="tablist"]');

        function isCompactLayout() {
            if (!tabList || !windowObject || typeof windowObject.getComputedStyle !== 'function') return false;
            return windowObject.getComputedStyle(tabList).display !== 'none';
        }

        function setStatus(element, message) {
            if (element) element.textContent = message;
        }

        function setPreviewMessage(message, kind) {
            if (!previewState) return;
            previewState.hidden = !message;
            previewState.classList.toggle('is-error', kind === 'error');
            previewState.setAttribute('aria-busy', String(kind === 'loading'));
            const textTarget = previewState.querySelector('[data-preview-state-text]') || Array.from(previewState.children).find(function (child) {
                return !child.classList.contains('aw-rb-preview-spinner');
            });
            (textTarget || previewState).textContent = message || '';
        }

        function storageGet() {
            if (!storageEnabled || !windowObject) return null;
            try { return windowObject.localStorage.getItem(storageKey); }
            catch (_error) { storageEnabled = false; return null; }
        }

        function storageSet(serialized) {
            if (!storageEnabled || !windowObject) return false;
            try { windowObject.localStorage.setItem(storageKey, serialized); return true; }
            catch (_error) { storageEnabled = false; return false; }
        }

        function storageRemove() {
            if (!storageKey || !windowObject) return;
            try { windowObject.localStorage.removeItem(storageKey); }
            catch (_error) { storageEnabled = false; }
        }

        function readSample() {
            const source = root.querySelector('[data-sample-resume]');
            if (!source) return createSampleState();
            const serialized = typeof source.value === 'string' ? source.value : source.textContent;
            const parsed = parseDraft(serialized || '');
            return parsed.recovered || parsed.reason === 'empty' ? createSampleState() : parsed.state;
        }

        function loadInitialState() {
            const serialized = storageGet();
            if (serialized === null) {
                setStatus(saveStatus, storageEnabled ? 'Changes save automatically on this device.' : 'Local draft saving is unavailable.');
                return createEmptyState();
            }
            const parsed = parseDraft(serialized);
            if (parsed.recovered) {
                storageRemove();
                setStatus(saveStatus, 'The saved draft was unreadable, so a new resume was started.');
            } else {
                setStatus(saveStatus, 'Draft restored from this device.');
            }
            return parsed.state;
        }

        function showSection(key) {
            const descriptor = state.sections.find(function (item) { return item.key === key; });
            if (descriptor) descriptor.isVisible = true;
        }

        function syncStaticFields() {
            root.querySelectorAll('[data-field]').forEach(function (control) {
                const path = control.dataset.field;
                const value = getPath(state, path);
                if (control.type === 'checkbox') control.checked = Boolean(value);
                else control.value = value === undefined || value === null ? '' : String(value);
                const fieldName = path.split('.').pop();
                if (FIELD_MAXIMUMS[fieldName] && !control.maxLength) control.maxLength = FIELD_MAXIMUMS[fieldName];
                if (path === 'professionalSummary') control.maxLength = LIMITS.summary;
            });
            if (summaryCount) {
                const length = plainRichText(state.professionalSummary, LIMITS.summary).length;
                summaryCount.textContent = length + ' characters - keep the summary to about 3 lines';
            }
        }

        function formatToolbar(targetId, label) {
            const toolbar = domElement(documentObject, 'div', 'aw-rb-format-toolbar');
            toolbar.setAttribute('aria-label', (label || 'Description') + ' formatting');
            toolbar.appendChild(domElement(documentObject, 'span', '', 'Select words, then format:'));
            const controls = domElement(documentObject, 'div');
            controls.setAttribute('role', 'group');
            controls.setAttribute('aria-label', 'Text formatting');
            [
                { key: 'bold', label: 'B', title: 'Bold selected text' },
                { key: 'italic', label: 'I', title: 'Italicize selected text' },
                { key: 'underline', label: 'U', title: 'Underline selected text' }
            ].forEach(function (definition) {
                const button = domElement(documentObject, 'button', 'aw-rb-format-' + definition.key, definition.label);
                button.type = 'button';
                button.dataset.format = definition.key;
                button.dataset.formatTarget = targetId;
                button.title = definition.title;
                button.setAttribute('aria-label', definition.title + ' in ' + (label || 'description'));
                controls.appendChild(button);
            });
            toolbar.appendChild(controls);
            return toolbar;
        }

        function fieldControl(path, definition) {
            const wrapper = domElement(documentObject, 'div', 'aw-rb-field' + (definition.wide ? ' aw-rb-field-wide' : ''));
            const id = 'rb-' + path.replace(/[^A-Za-z0-9]+/g, '-');
            if (definition.type === 'checkbox') {
                wrapper.classList.add('aw-rb-checkbox');
                const input = domElement(documentObject, 'input', 'form-check-input');
                input.type = 'checkbox';
                input.id = id;
                input.checked = Boolean(getPath(state, path));
                input.dataset.bindPath = path;
                const label = domElement(documentObject, 'label', 'form-check-label', definition.label);
                label.htmlFor = id;
                wrapper.append(input, label);
                return wrapper;
            }
            const label = domElement(documentObject, 'label', 'form-label', definition.label);
            label.htmlFor = id;
            const control = domElement(documentObject, definition.type === 'textarea' ? 'textarea' : 'input', 'form-control');
            control.id = id;
            control.dataset.bindPath = path;
            if (definition.type !== 'textarea') control.type = definition.type || 'text';
            else control.rows = definition.rows || 3;
            if (definition.placeholder) control.placeholder = definition.placeholder;
            const fieldName = path.split('.').pop();
            control.maxLength = definition.maximum || FIELD_MAXIMUMS[fieldName] || LIMITS.shortText;
            control.value = String(getPath(state, path) || '');
            const error = domElement(documentObject, 'span', 'aw-rb-error field-validation-error');
            error.dataset.errorFor = path;
            error.id = id + '-error';
            error.setAttribute('aria-live', 'polite');
            control.setAttribute('aria-describedby', error.id);
            wrapper.appendChild(label);
            if (definition.richText) wrapper.appendChild(formatToolbar(id, definition.label));
            wrapper.append(control, error);
            return wrapper;
        }

        function entryActions(section, index, count, label) {
            const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
            const up = domButton(documentObject, '↑', 'move-entry-up', 'aw-rb-icon-button');
            up.dataset.section = section; up.dataset.index = String(index); up.disabled = index === 0;
            up.title = 'Move up'; up.setAttribute('aria-label', 'Move ' + label + ' up');
            const down = domButton(documentObject, '↓', 'move-entry-down', 'aw-rb-icon-button');
            down.dataset.section = section; down.dataset.index = String(index); down.disabled = index === count - 1;
            down.title = 'Move down'; down.setAttribute('aria-label', 'Move ' + label + ' down');
            const remove = domButton(documentObject, 'Remove', 'remove-entry', 'aw-rb-text-button');
            remove.dataset.section = section; remove.dataset.index = String(index);
            remove.setAttribute('aria-label', 'Remove ' + label);
            actions.append(up, down, remove);
            return actions;
        }

        function sectionCard(section, title, help, addLabel) {
            const card = domElement(documentObject, 'section', 'aw-rb-section-card aw-rb-section-' + section);
            card.dataset.editorSection = section;
            const header = domElement(documentObject, 'header');
            const headingGroup = domElement(documentObject, 'div');
            headingGroup.appendChild(domElement(documentObject, 'h3', '', title));
            if (help) headingGroup.appendChild(domElement(documentObject, 'p', 'form-text', help));
            header.appendChild(headingGroup);
            if (addLabel) {
                const add = domButton(documentObject, addLabel, 'add-entry', 'aw-rb-text-button');
                add.dataset.section = section;
                header.appendChild(add);
            }
            card.appendChild(header);
            return card;
        }

        function renderEmpty(container, message) {
            container.appendChild(domElement(documentObject, 'p', 'aw-rb-empty', message));
        }

        function renderBullets(container, path, label) {
            const list = domElement(documentObject, 'div', 'aw-rb-bullet-list');
            list.appendChild(domElement(documentObject, 'p', 'form-label', label));
            const bullets = getPath(state, path) || [];
            bullets.forEach(function (bullet, index) {
                const row = domElement(documentObject, 'div', 'aw-rb-bullet-row');
                const input = domElement(documentObject, 'textarea', 'form-control');
                const itemPath = path + '.' + index;
                input.id = 'rb-' + itemPath.replace(/[^A-Za-z0-9]+/g, '-');
                input.dataset.bindPath = itemPath;
                input.value = bullet;
                input.rows = 2;
                input.maxLength = LIMITS.bullet;
                input.placeholder = 'Start with a strong action verb and include a measurable result when possible.';
                input.setAttribute('aria-label', label + ' ' + String(index + 1));
                const remove = domButton(documentObject, 'Remove bullet', 'remove-bullet', 'aw-rb-text-button aw-rb-remove-bullet');
                remove.dataset.listPath = path; remove.dataset.index = String(index);
                remove.setAttribute('aria-label', 'Remove ' + label.toLowerCase() + ' ' + String(index + 1));
                const editor = domElement(documentObject, 'div', 'aw-rb-rich-editor');
                editor.append(formatToolbar(input.id, label + ' ' + String(index + 1)), input);
                row.append(editor, remove);
                list.appendChild(row);
            });
            const add = domButton(documentObject, '+ Add bullet', 'add-bullet', 'aw-rb-text-button');
            add.dataset.listPath = path;
            list.appendChild(add);
            container.appendChild(list);
        }

        function renderTags(container, path, label, placeholder) {
            const group = domElement(documentObject, 'div', 'aw-rb-tag-entry');
            group.appendChild(domElement(documentObject, 'p', 'form-label', label));
            const tags = domElement(documentObject, 'div', 'aw-rb-tags');
            (getPath(state, path) || []).forEach(function (tag, index) {
                const chip = domElement(documentObject, 'span', 'aw-rb-tag');
                chip.appendChild(domElement(documentObject, 'span', '', tag));
                const remove = domButton(documentObject, '×', 'remove-tag', 'aw-rb-icon-button');
                remove.dataset.listPath = path; remove.dataset.index = String(index);
                remove.setAttribute('aria-label', 'Remove ' + tag);
                chip.appendChild(remove);
                tags.appendChild(chip);
            });
            group.appendChild(tags);
            const inputRow = domElement(documentObject, 'div', 'aw-rb-bullet-row');
            const input = domElement(documentObject, 'input', 'form-control');
            input.type = 'text'; input.maxLength = LIMITS.tag;
            input.placeholder = placeholder || 'Type a value, then press Enter';
            input.dataset.tagInput = '';
            input.dataset.listPath = path;
            input.setAttribute('aria-label', 'Add ' + label.toLowerCase());
            const add = domButton(documentObject, 'Add', 'add-tag', 'aw-rb-text-button');
            add.dataset.listPath = path;
            inputRow.append(input, add);
            group.appendChild(inputRow);
            container.appendChild(group);
        }

        function renderCollection(config) {
            const card = sectionCard(config.key, config.title, config.help, config.addLabel || '+ Add entry');
            const entries = state[config.key];
            if (!entries.length) renderEmpty(card, config.empty);
            entries.forEach(function (entry, index) {
                const entryCard = domElement(documentObject, 'article', 'aw-rb-entry-card');
                entryCard.dataset.entrySection = config.key;
                entryCard.dataset.entryIndex = String(index);
                const head = domElement(documentObject, 'div', 'aw-rb-entry-head');
                head.appendChild(domElement(documentObject, 'h4', '', config.entryTitle(entry, index)));
                head.appendChild(entryActions(config.key, index, entries.length, config.singular + ' ' + String(index + 1)));
                entryCard.appendChild(head);
                const grid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                config.fields.forEach(function (definition) {
                    if (definition.when && !definition.when(entry)) return;
                    grid.appendChild(fieldControl(config.key + '.' + index + '.' + definition.property, definition));
                });
                entryCard.appendChild(grid);
                if (config.extra) config.extra(entryCard, entry, index);
                card.appendChild(entryCard);
            });
            dynamicEditor.appendChild(card);
        }

        function renderCustomSections() {
            const card = sectionCard('customSections', 'Custom sections', 'Create flexible sections for publications, memberships, speaking, or anything else.', '+ Add custom section');
            if (!state.customSections.length) renderEmpty(card, 'No custom sections yet.');
            state.customSections.forEach(function (section, sectionIndex) {
                const sectionPath = 'customSections.' + sectionIndex;
                const outer = domElement(documentObject, 'article', 'aw-rb-entry-card');
                const head = domElement(documentObject, 'div', 'aw-rb-entry-head');
                head.appendChild(domElement(documentObject, 'h4', '', section.title || 'Custom section ' + String(sectionIndex + 1)));
                head.appendChild(entryActions('customSections', sectionIndex, state.customSections.length, 'custom section ' + String(sectionIndex + 1)));
                outer.appendChild(head);
                const titleGrid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                titleGrid.appendChild(fieldControl(sectionPath + '.title', { label: 'Section title', placeholder: 'e.g. Publications' }));
                outer.appendChild(titleGrid);
                const entriesHead = domElement(documentObject, 'div', 'aw-rb-entry-head');
                entriesHead.appendChild(domElement(documentObject, 'h5', '', 'Entries'));
                const addEntry = domButton(documentObject, '+ Add entry', 'add-custom-entry', 'aw-rb-text-button');
                addEntry.dataset.sectionIndex = String(sectionIndex);
                entriesHead.appendChild(addEntry);
                outer.appendChild(entriesHead);
                if (!section.entries.length) renderEmpty(outer, 'No entries in this section yet.');
                section.entries.forEach(function (entry, entryIndex) {
                    const entryPath = sectionPath + '.entries.' + entryIndex;
                    const nested = domElement(documentObject, 'div', 'aw-rb-entry-card');
                    const nestedHead = domElement(documentObject, 'div', 'aw-rb-entry-head');
                    nestedHead.appendChild(domElement(documentObject, 'h5', '', entry.heading || 'Entry ' + String(entryIndex + 1)));
                    const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
                    ['up', 'down'].forEach(function (direction) {
                        const control = domButton(documentObject, direction === 'up' ? '↑' : '↓', 'move-custom-entry-' + direction, 'aw-rb-icon-button');
                        control.dataset.sectionIndex = String(sectionIndex); control.dataset.entryIndex = String(entryIndex);
                        control.disabled = direction === 'up' ? entryIndex === 0 : entryIndex === section.entries.length - 1;
                        control.setAttribute('aria-label', 'Move custom entry ' + direction);
                        actions.appendChild(control);
                    });
                    const remove = domButton(documentObject, 'Remove', 'remove-custom-entry', 'aw-rb-text-button');
                    remove.dataset.sectionIndex = String(sectionIndex); remove.dataset.entryIndex = String(entryIndex);
                    actions.appendChild(remove);
                    nestedHead.appendChild(actions);
                    nested.appendChild(nestedHead);
                    const grid = domElement(documentObject, 'div', 'aw-rb-dynamic-grid');
                    [
                        { property: 'heading', label: 'Heading' },
                        { property: 'subheading', label: 'Subheading' },
                        { property: 'location', label: 'Location' },
                        { property: 'url', label: 'URL', type: 'url', placeholder: 'https://…' },
                        { property: 'startDate', label: 'Start month', type: 'month' },
                        { property: 'endDate', label: 'End month', type: 'month', when: function () { return !entry.isCurrent; } },
                        { property: 'isCurrent', label: 'Current', type: 'checkbox' }
                    ].forEach(function (definition) {
                        if (!definition.when || definition.when()) grid.appendChild(fieldControl(entryPath + '.' + definition.property, definition));
                    });
                    nested.appendChild(grid);
                    renderBullets(nested, entryPath + '.bulletPoints', 'Highlights');
                    outer.appendChild(nested);
                });
                card.appendChild(outer);
            });
            dynamicEditor.appendChild(card);
        }

        function renderDynamicEditor() {
            if (!dynamicEditor) return;
            replaceChildren(dynamicEditor);
            renderCollection({
                key: 'education', title: 'Education', singular: 'education entry', addLabel: '+ Add education', empty: 'No education entries yet.',
                help: 'Add your most recent or most relevant education first.', entryTitle: function (entry, index) { return entry.institutionName || 'Education ' + String(index + 1); },
                fields: [
                    { property: 'institutionName', label: 'Institution name', placeholder: 'FAST National University' },
                    { property: 'degree', label: 'Degree', placeholder: 'Bachelor of Science' },
                    { property: 'fieldOfStudy', label: 'Field of study', placeholder: 'Computer Science' },
                    { property: 'location', label: 'Location' },
                    { property: 'startDate', label: 'Start month', type: 'month' },
                    { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyStudying; } },
                    { property: 'isCurrentlyStudying', label: 'Currently studying', type: 'checkbox' },
                    { property: 'grade', label: 'CGPA / GPA / grade', placeholder: 'CGPA: 3.5 / 4.0' },
                    { property: 'descriptionOrCoursework', label: 'Description or relevant coursework', type: 'textarea', wide: true, rows: 3, richText: true }
                ]
            });
            renderCollection({
                key: 'experience', title: 'Experience', singular: 'experience entry', addLabel: '+ Add experience', empty: 'No experience entries yet.',
                help: 'Lead with outcomes and quantify impact where you can.', entryTitle: function (entry, index) { return entry.jobTitle || entry.companyName || 'Experience ' + String(index + 1); },
                fields: [
                    { property: 'companyName', label: 'Company name' }, { property: 'jobTitle', label: 'Job title' },
                    { property: 'employmentType', label: 'Employment type', placeholder: 'Full-time, internship, contract…' }, { property: 'location', label: 'Location' },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyWorking; } },
                    { property: 'isCurrentlyWorking', label: 'Currently working', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) { renderBullets(container, 'experience.' + index + '.bulletPoints', 'Responsibilities and achievements'); }
            });
            renderCollection({
                key: 'projects', title: 'Projects', singular: 'project', addLabel: '+ Add project', empty: 'No projects yet.',
                help: 'Show the problem, what you built, and the result.', entryTitle: function (entry, index) { return entry.projectName || 'Project ' + String(index + 1); },
                fields: [
                    { property: 'projectName', label: 'Project name' }, { property: 'projectUrl', label: 'Live project URL', type: 'url', placeholder: 'https://…' },
                    { property: 'repositoryUrl', label: 'Repository URL', type: 'url', placeholder: 'https://…' },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isOngoing; } },
                    { property: 'isOngoing', label: 'Ongoing project', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) {
                    renderTags(container, 'projects.' + index + '.technologiesUsed', 'Technologies used', 'Type a technology, then press Enter');
                    renderBullets(container, 'projects.' + index + '.bulletPoints', 'Project highlights');
                }
            });
            renderCollection({
                key: 'skills', title: 'Skills', singular: 'skill category', addLabel: '+ Add category', empty: 'No skill categories yet.',
                help: 'Group related skills so recruiters can scan them quickly.', entryTitle: function (entry, index) { return entry.name || 'Skill category ' + String(index + 1); },
                fields: [{ property: 'name', label: 'Category name', placeholder: 'Languages, Frameworks, Databases…', wide: true }],
                extra: function (container, _entry, index) { renderTags(container, 'skills.' + index + '.skills', 'Skills', 'Type a skill, then press Enter'); }
            });
            renderCollection({
                key: 'achievementsAndCertifications', title: 'Achievements & certifications', singular: 'achievement', addLabel: '+ Add achievement', empty: 'No achievements or certifications yet.',
                help: '', entryTitle: function (entry, index) { return entry.title || 'Achievement ' + String(index + 1); },
                fields: [
                    { property: 'title', label: 'Title' }, { property: 'issuingOrganization', label: 'Issuing organization' },
                    { property: 'date', label: 'Date', type: 'month' }, { property: 'credentialUrl', label: 'Credential URL', type: 'url', placeholder: 'https://…' },
                    { property: 'description', label: 'Short description', type: 'textarea', wide: true, rows: 3, richText: true }
                ]
            });
            renderCollection({
                key: 'languages', title: 'Languages', singular: 'language', addLabel: '+ Add language', empty: 'No languages yet.',
                help: '', entryTitle: function (entry, index) { return entry.name || 'Language ' + String(index + 1); },
                fields: [{ property: 'name', label: 'Language' }, { property: 'proficiency', label: 'Proficiency', placeholder: 'Native, fluent, conversational…' }]
            });
            renderCollection({
                key: 'volunteerExperience', title: 'Volunteer experience', singular: 'volunteer entry', addLabel: '+ Add volunteer role', empty: 'No volunteer experience yet.',
                help: '', entryTitle: function (entry, index) { return entry.role || entry.organizationName || 'Volunteer role ' + String(index + 1); },
                fields: [
                    { property: 'organizationName', label: 'Organization name' }, { property: 'role', label: 'Role' }, { property: 'location', label: 'Location' },
                    { property: 'startDate', label: 'Start month', type: 'month' }, { property: 'endDate', label: 'End month', type: 'month', when: function (entry) { return !entry.isCurrentlyVolunteering; } },
                    { property: 'isCurrentlyVolunteering', label: 'Currently volunteering', type: 'checkbox' }
                ],
                extra: function (container, _entry, index) { renderBullets(container, 'volunteerExperience.' + index + '.bulletPoints', 'Highlights'); }
            });
            const interests = sectionCard('interests', 'Interests', 'Keep this concise and specific.', null);
            renderTags(interests, 'interests', 'Interests', 'Type an interest, then press Enter');
            dynamicEditor.appendChild(interests);
            renderCustomSections();
        }

        function renderSectionManager() {
            if (!sectionManager) return;
            const rows = [];
            state.sections.forEach(function (section, index) {
                const row = domElement(documentObject, 'div', 'aw-rb-manager-row');
                row.dataset.sectionKey = section.key;
                row.classList.toggle('is-hidden', !section.isVisible);
                const label = domElement(documentObject, 'span', '', section.title);
                const actions = domElement(documentObject, 'div', 'aw-rb-entry-actions');
                const up = domButton(documentObject, '↑', 'move-section-up', 'aw-rb-icon-button');
                up.dataset.index = String(index); up.disabled = index === 0; up.setAttribute('aria-label', 'Move ' + section.title + ' up');
                const down = domButton(documentObject, '↓', 'move-section-down', 'aw-rb-icon-button');
                down.dataset.index = String(index); down.disabled = index === state.sections.length - 1; down.setAttribute('aria-label', 'Move ' + section.title + ' down');
                const toggle = domButton(documentObject, section.isVisible ? 'Hide' : 'Show', 'toggle-section', 'aw-rb-text-button');
                toggle.dataset.index = String(index); toggle.setAttribute('aria-pressed', String(section.isVisible));
                toggle.setAttribute('aria-label', (section.isVisible ? 'Hide ' : 'Show ') + section.title + ' in resume');
                actions.append(up, down, toggle);
                row.append(label, actions);
                rows.push(row);
            });
            replaceChildren.apply(null, [sectionManager].concat(rows));
        }

        function currentErrors() {
            return validateState(state);
        }

        function updateOnePageStatus() {
            const guidance = onePageGuidance(state);
            Object.keys(guidance.metrics).forEach(function (key) {
                const metric = guidance.metrics[key];
                const element = root.querySelector('[data-guide-metric="' + key + '"]');
                if (!element) return;
                const value = element.querySelector('strong');
                const suffix = key === 'professionalSummary' ? ' lines' : '';
                if (value) value.textContent = metric.value + ' / ' + metric.maximum + suffix;
                element.classList.toggle('is-over', metric.value > metric.maximum);
            });

            const pageCountIsCurrent = latestBlobRevision === revision && latestPageCount > 0;
            if (pageFitStatus) {
                pageFitStatus.classList.toggle('is-checking', !pageCountIsCurrent);
                pageFitStatus.classList.toggle('is-overflow', pageCountIsCurrent && latestPageCount !== 1);
                pageFitStatus.textContent = !pageCountIsCurrent
                    ? 'Checking page fit…'
                    : latestPageCount === 1
                        ? 'Fits on 1 A4 page'
                        : latestPageCount + ' pages - shorten content';
            }

            const messages = [];
            if (pageCountIsCurrent && latestPageCount > 1) {
                messages.push('This resume currently creates ' + latestPageCount + ' pages. Download is blocked until it fits on one page.');
            }
            if (guidance.exceeded.length) {
                const labels = guidance.exceeded.map(function (key) { return guidance.metrics[key].label; });
                messages.push('Above the one-page guide: ' + labels.join(', ') + '. Keep only the most relevant content or hide a section.');
            }
            if (onePageWarning) {
                onePageWarning.textContent = messages.join(' ');
                onePageWarning.hidden = messages.length === 0;
            }
            return { guidance: guidance, pageCountIsCurrent: pageCountIsCurrent };
        }

        function updateValidation() {
            const errors = currentErrors();
            root.querySelectorAll('[data-error-for]').forEach(function (message) {
                const path = message.dataset.errorFor;
                const visible = showAllErrors || touched.has(path);
                message.textContent = visible ? (errors[path] || '') : '';
                const control = Array.from(root.querySelectorAll('[data-field], [data-bind-path]')).find(function (item) {
                    return item.dataset.field === path || item.dataset.bindPath === path;
                });
                if (control) {
                    control.classList.toggle('input-validation-error', Boolean(visible && errors[path]));
                    control.setAttribute('aria-invalid', String(Boolean(visible && errors[path])));
                }
            });
            const valid = Object.keys(errors).length === 0;
            const onePage = updateOnePageStatus();
            const canDownload = valid && onePage.pageCountIsCurrent && latestPageCount === 1;
            root.querySelectorAll('[data-action="download"]').forEach(function (button) { button.disabled = !canDownload; });
            return { valid: valid, errors: errors, canDownload: canDownload, pageCount: latestPageCount };
        }

        function renderAll() {
            syncStaticFields();
            renderSectionManager();
            renderDynamicEditor();
            updateValidation();
        }

        function focusInitialSection() {
            if (destroyed || initialSectionHandled || !initialSection) return false;
            const selector = builderSectionSelector(initialSection);
            const target = selector ? root.querySelector(selector) : null;
            if (!target) return false;

            initialSectionHandled = true;
            activateTab('form');
            target.classList.add('is-deep-link-target');
            const control = target.querySelector('[data-field], [data-bind-path], input:not([type="hidden"]):not([disabled]), textarea:not([disabled]), select:not([disabled])')
                || target.querySelector('button:not([disabled]), [href]');
            if (control && typeof control.focus === 'function') {
                try { control.focus({ preventScroll: true }); }
                catch (_error) { control.focus(); }
            } else if (typeof target.focus === 'function') {
                target.tabIndex = -1;
                try { target.focus({ preventScroll: true }); }
                catch (_error) { target.focus(); }
            }
            if (typeof target.scrollIntoView === 'function') {
                const reducedMotion = windowObject && typeof windowObject.matchMedia === 'function'
                    && windowObject.matchMedia('(prefers-reduced-motion: reduce)').matches;
                target.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'center' });
            }
            if (windowObject) {
                windowObject.clearTimeout(targetHighlightTimer);
                targetHighlightTimer = windowObject.setTimeout(function () {
                    target.classList.remove('is-deep-link-target');
                }, 2400);
            }
            return true;
        }

        function scheduleInitialSectionFocus() {
            if (!initialSection || !windowObject || initialSectionHandled) return;
            windowObject.clearTimeout(initialFocusTimer);
            initialFocusTimer = windowObject.setTimeout(focusInitialSection, 0);
        }

        function renderReadiness() {
            if (!readinessPanel) return null;
            const result = evaluateDraftReadiness(state);
            readinessPanel.dataset.readinessTone = result.tone;
            readinessPanel.style.setProperty('--aw-rb-readiness', String(result.score));
            if (readinessScore) readinessScore.textContent = String(result.score);
            if (readinessMeter) readinessMeter.setAttribute('aria-label', 'Draft readiness score: ' + result.score + ' out of 100');
            if (readinessSummary) readinessSummary.textContent = result.summary;
            if (readinessChecks) {
                replaceChildren(readinessChecks);
                result.checks.forEach(function (check) {
                    const row = domElement(documentObject, 'div', 'aw-rb-readiness-check');
                    const mark = domElement(documentObject, 'span', 'aw-rb-readiness-check-mark', check.status === 'ready' ? '\u2713' : check.status === 'missing' ? '!' : '\u2022');
                    const copy = domElement(documentObject, 'span');
                    row.dataset.readinessStatus = check.status;
                    mark.setAttribute('aria-hidden', 'true');
                    copy.appendChild(domElement(documentObject, 'strong', '', check.title));
                    copy.appendChild(domElement(documentObject, 'small', '', check.message));
                    row.appendChild(mark);
                    row.appendChild(copy);
                    readinessChecks.appendChild(row);
                });
            }
            if (readinessNext) {
                const warning = result.warnings[0];
                readinessNext.hidden = !warning;
                readinessNext.textContent = warning ? 'Next improvement: ' + warning : '';
            }
            return result;
        }

        function scheduleReadiness(delay) {
            if (!readinessPanel || !windowObject) return;
            windowObject.clearTimeout(readinessTimer);
            readinessTimer = windowObject.setTimeout(renderReadiness, typeof delay === 'number' ? delay : READINESS_DEBOUNCE_MS);
        }

        function saveNow() {
            if (destroyed || revision === savedRevision) return;
            if (!storageEnabled) {
                setStatus(saveStatus, 'Local draft saving is unavailable.');
                return;
            }
            let serialized;
            try { serialized = JSON.stringify(normalizeState(state)); }
            catch (_error) { setStatus(saveStatus, 'This draft could not be saved.'); return; }
            if (serialized.length > LIMITS.draftBytes) {
                setStatus(saveStatus, 'This draft is too large to save locally. Shorten a few entries.');
                return;
            }
            if (storageSet(serialized)) {
                savedRevision = revision;
                const now = new Date();
                setStatus(saveStatus, 'Saved at ' + now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }) + '.');
            } else {
                setStatus(saveStatus, 'This browser could not save the draft locally.');
            }
        }

        function scheduleSave() {
            if (!storageEnabled) return;
            setStatus(saveStatus, 'Saving…');
            windowObject.clearTimeout(saveTimer);
            saveTimer = windowObject.setTimeout(saveNow, 500);
        }

        function pdfMakeInstance() {
            return configuration.pdfMake || (windowObject && windowObject.pdfMake);
        }

        function createPdfBlob(definition) {
            const pdfMake = pdfMakeInstance();
            if (!pdfMake || typeof pdfMake.createPdf !== 'function') return Promise.reject(new Error('PDF generator is unavailable.'));
            try {
                const result = pdfMake.createPdf(definition);
                if (!result || typeof result.getBlob !== 'function') return Promise.reject(new Error('PDF generator did not return a document.'));
                return Promise.resolve(result.getBlob());
            } catch (error) {
                return Promise.reject(error);
            }
        }

        function revokePreviewUrl() {
            if (!currentPreviewUrl || !windowObject || !windowObject.URL) return;
            windowObject.URL.revokeObjectURL(currentPreviewUrl);
            currentPreviewUrl = '';
        }

        function onPageHide() {
            saveNow();
            revokePreviewUrl();
        }

        function onPageShow(event) {
            if (event && event.persisted) schedulePreview(0);
        }

        function onStorage(event) {
            if (event.key !== storageKey) return;
            windowObject.clearTimeout(saveTimer);
            if (event.newValue === null) {
                state = createEmptyState();
                revision += 1;
                savedRevision = revision;
                latestBlobRevision = -1;
                latestPageCount = 0;
                touched.clear();
                showAllErrors = false;
                renderAll();
                schedulePreview(0);
                scheduleReadiness(0);
                setStatus(saveStatus, 'Resume cleared in another tab.');
                setStatus(pdfStatus, '');
                return;
            }
            if (typeof event.newValue !== 'string') return;
            const parsed = parseDraft(event.newValue);
            if (parsed.recovered) return;
            state = parsed.state;
            revision += 1;
            savedRevision = revision;
            latestBlobRevision = -1;
            latestPageCount = 0;
            touched.clear();
            showAllErrors = false;
            renderAll();
            schedulePreview(0);
            scheduleReadiness(0);
            setStatus(saveStatus, 'Draft updated from another tab.');
        }

        function refreshPreview() {
            if (destroyed || !previewFrame) return Promise.resolve(null);
            const request = ++previewRequest;
            const requestedRevision = revision;
            const definition = buildDocumentDefinition(state);
            setPreviewMessage('Updating resume preview…', 'loading');
            previewPromise = createPdfBlob(definition).then(async function (blob) {
                if (destroyed || request !== previewRequest || requestedRevision !== revision) return null;
                if (!blob || typeof blob.size !== 'number') throw new Error('The generated PDF was empty.');
                const pageCount = await countPdfPages(blob);
                if (destroyed || request !== previewRequest || requestedRevision !== revision) return null;
                latestBlob = blob;
                latestBlobRevision = revision;
                latestPageCount = pageCount;
                revokePreviewUrl();
                currentPreviewUrl = windowObject.URL.createObjectURL(blob);
                previewFrame.src = currentPreviewUrl;
                setPreviewMessage('', 'ready');
                updateValidation();
                return blob;
            }).catch(function (_error) {
                if (request === previewRequest) setPreviewMessage('The preview could not be generated. Check the PDF library and try again.', 'error');
                return null;
            }).finally(function () {
                if (request === previewRequest) previewPromise = null;
                if (requestedRevision !== revision && !destroyed) schedulePreview(0);
            });
            return previewPromise;
        }

        function schedulePreview(delay) {
            if (!previewFrame || !windowObject) return;
            windowObject.clearTimeout(previewTimer);
            previewTimer = windowObject.setTimeout(refreshPreview, typeof delay === 'number' ? delay : 650);
        }

        function changed(rerender) {
            revision += 1;
            latestBlobRevision = -1;
            latestPageCount = 0;
            if (rerender) {
                renderSectionManager();
                renderDynamicEditor();
            }
            syncStaticFields();
            updateValidation();
            scheduleSave();
            schedulePreview();
            scheduleReadiness();
        }

        function notifyLimit(message) {
            setStatus(saveStatus, message);
        }

        function addTag(input) {
            if (!input) return;
            const path = input.dataset.listPath;
            const list = getPath(state, path);
            const value = text(input.value, LIMITS.tag);
            if (!Array.isArray(list) || !value) return;
            if (list.length >= LIMITS.tags) { notifyLimit('That list has reached its ' + LIMITS.tags + '-item limit.'); return; }
            if (list.some(function (item) { return item.toLocaleLowerCase() === value.toLocaleLowerCase(); })) {
                input.value = '';
                notifyLimit('That item is already in the list.');
                return;
            }
            list.push(value);
            input.value = '';
            changed(true);
            const matching = Array.from(root.querySelectorAll('[data-tag-input]')).find(function (candidate) { return candidate.dataset.listPath === path; });
            if (matching) matching.focus();
        }

        function applyTextFormat(button) {
            const markers = {
                bold: ['[b]', '[/b]'],
                italic: ['[i]', '[/i]'],
                underline: ['[u]', '[/u]']
            };
            const pair = markers[button.dataset.format];
            const target = documentObject.getElementById(button.dataset.formatTarget || '');
            if (!pair || !target || !root.contains(target) || typeof target.selectionStart !== 'number') return;
            const start = target.selectionStart;
            const end = target.selectionEnd;
            const selected = target.value.slice(start, end);
            const replacement = pair[0] + selected + pair[1];
            const nextValue = target.value.slice(0, start) + replacement + target.value.slice(end);
            if (target.maxLength > 0 && nextValue.length > target.maxLength) {
                setStatus(saveStatus, 'That field is at its character limit; shorten it before adding formatting.');
                target.focus();
                return;
            }
            target.value = nextValue;
            target.focus();
            const selectionStart = start + pair[0].length;
            target.setSelectionRange(selectionStart, selectionStart + selected.length);
            target.dispatchEvent(new windowObject.Event('input', { bubbles: true }));
        }

        function confirmClear() {
            state = createEmptyState();
            touched.clear();
            showAllErrors = false;
            storageRemove();
            revision += 1;
            savedRevision = revision;
            latestBlobRevision = -1;
            latestPageCount = 0;
            if (clearDialog && typeof clearDialog.close === 'function') clearDialog.close();
            renderAll();
            setStatus(saveStatus, 'Resume cleared.');
            setStatus(pdfStatus, '');
            schedulePreview(0);
            scheduleReadiness(0);
            const first = root.querySelector('[data-field="personalInformation.fullName"]');
            if (first) first.focus();
        }

        function requestClear() {
            if (clearDialog && typeof clearDialog.showModal === 'function') {
                if (!clearDialog.open) clearDialog.showModal();
                return;
            }
            if (!windowObject || windowObject.confirm('Clear all resume information? This cannot be undone.')) confirmClear();
        }

        function focusFirstError(errors) {
            const firstPath = Object.keys(errors)[0];
            if (!firstPath) return;
            const control = Array.from(root.querySelectorAll('[data-field], [data-bind-path]')).find(function (item) {
                return item.dataset.field === firstPath || item.dataset.bindPath === firstPath;
            });
            if (control) control.focus();
        }

        function downloadBlob(blob, filename) {
            const url = windowObject.URL.createObjectURL(blob);
            const anchor = documentObject.createElement('a');
            anchor.href = url;
            anchor.download = filename;
            anchor.hidden = true;
            documentObject.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
            windowObject.setTimeout(function () { windowObject.URL.revokeObjectURL(url); }, 1000);
        }

        async function getCurrentPdfBlob() {
            const maximumAttempts = 3;
            for (let attempt = 0; attempt < maximumAttempts; attempt += 1) {
                const requestedRevision = revision;
                if (latestBlob && latestBlobRevision === requestedRevision) {
                    if (!latestPageCount) latestPageCount = await countPdfPages(latestBlob);
                    return latestBlob;
                }

                if (previewPromise) {
                    await previewPromise;
                    if (latestBlob && latestBlobRevision === revision) return latestBlob;
                    if (requestedRevision !== revision) continue;
                }

                const definition = buildDocumentDefinition(state);
                const generatedBlob = await createPdfBlob(definition);
                const generatedPageCount = await countPdfPages(generatedBlob);
                if (requestedRevision !== revision) continue;

                latestBlob = generatedBlob;
                latestBlobRevision = requestedRevision;
                latestPageCount = generatedPageCount;
                return generatedBlob;
            }

            throw new Error('The resume changed while the PDF was being prepared.');
        }

        async function downloadPdf() {
            showAllErrors = true;
            const validation = updateValidation();
            if (!validation.valid) {
                setStatus(pdfStatus, 'Complete the highlighted fields before downloading.');
                focusFirstError(validation.errors);
                return;
            }
            const buttons = root.querySelectorAll('[data-action="download"]');
            buttons.forEach(function (button) { button.disabled = true; button.setAttribute('aria-busy', 'true'); });
            setStatus(pdfStatus, 'Creating your PDF…');
            try {
                const blob = await getCurrentPdfBlob();
                const currentValidation = updateValidation();
                if (!currentValidation.valid) {
                    setStatus(pdfStatus, 'Complete the highlighted fields before downloading.');
                    focusFirstError(currentValidation.errors);
                    return;
                }
                if (currentValidation.pageCount !== 1) {
                    setStatus(pdfStatus, 'This resume is ' + currentValidation.pageCount + ' pages. Shorten it until the one-page check passes.');
                    return;
                }
                downloadBlob(blob, buildFilename(state.personalInformation.fullName));
                setStatus(pdfStatus, 'PDF ready. Your download has started.');
            } catch (_error) {
                setStatus(pdfStatus, 'The PDF could not be created. Please try again.');
            } finally {
                buttons.forEach(function (button) { button.removeAttribute('aria-busy'); });
                updateValidation();
            }
        }

        function handleAction(button) {
            const action = button.dataset.action;
            const section = button.dataset.section;
            const index = Number(button.dataset.index);
            if (action === 'load-sample') {
                const personal = state.personalInformation || {};
                const hasDraftContent = Object.keys(personal).some(function (key) { return hasText(personal[key]); })
                    || hasText(state.professionalSummary)
                    || state.sections.some(function (descriptor) { return hasSectionContent(state, descriptor.key); });
                if (hasDraftContent && windowObject && !windowObject.confirm('Replace your current resume with the sample? Your saved draft will be overwritten.')) return;
                state = readSample(); touched.clear(); showAllErrors = false; revision += 1; latestBlobRevision = -1; latestPageCount = 0;
                renderAll(); scheduleSave(); schedulePreview(0); scheduleReadiness(0); setStatus(pdfStatus, 'Sample data loaded.'); return;
            }
            if (action === 'clear') { requestClear(); return; }
            if (action === 'confirm-clear') { confirmClear(); return; }
            if (action === 'cancel-clear') { if (clearDialog && typeof clearDialog.close === 'function') clearDialog.close(); return; }
            if (action === 'download') { downloadPdf(); return; }
            if (action === 'toggle-preview-size') {
                const panel = root.querySelector('[data-tab-panel="preview"]');
                const backdrop = root.querySelector('[data-preview-backdrop]');
                const expanded = panel ? !panel.classList.contains('is-expanded') : false;
                if (panel) panel.classList.toggle('is-expanded', expanded);
                if (backdrop) backdrop.hidden = !expanded;
                root.classList.toggle('has-expanded-preview', expanded);
                root.querySelectorAll('[data-action="toggle-preview-size"]').forEach(function (control) {
                    control.setAttribute('aria-expanded', String(expanded));
                });
                const label = root.querySelector('[data-preview-size-label]');
                if (label) label.textContent = expanded ? 'Close preview' : 'Expand preview';
                if (expanded) schedulePreview(0);
                return;
            }
            if (action === 'add-custom-section') {
                if (state.customSections.length >= LIMITS.customSections) { notifyLimit('Custom sections have reached their ' + LIMITS.customSections + '-section limit.'); return; }
                state.customSections.push(ENTRY_FACTORIES.customSections()); showSection('customSections'); changed(true); return;
            }
            if (action === 'add-entry' && ENTRY_FACTORIES[section]) {
                const list = state[section];
                const maximum = section === 'customSections' ? LIMITS.customSections : LIMITS.entries;
                if (list.length >= maximum) { notifyLimit('That section has reached its ' + maximum + '-entry limit.'); return; }
                list.push(ENTRY_FACTORIES[section]()); showSection(section); changed(true); return;
            }
            if ((action === 'remove-entry' || action === 'move-entry-up' || action === 'move-entry-down') && Array.isArray(state[section])) {
                if (!Number.isInteger(index) || index < 0 || index >= state[section].length) return;
                if (action === 'remove-entry') state[section].splice(index, 1);
                else moveItem(state[section], index, action.endsWith('up') ? -1 : 1);
                changed(true); return;
            }
            if (action === 'add-bullet') {
                const list = getPath(state, button.dataset.listPath);
                if (!Array.isArray(list)) return;
                if (list.length >= LIMITS.bullets) { notifyLimit('That entry has reached its ' + LIMITS.bullets + '-bullet limit.'); return; }
                list.push(''); changed(true); return;
            }
            if (action === 'remove-bullet') {
                const list = getPath(state, button.dataset.listPath);
                if (Array.isArray(list) && Number.isInteger(index) && index >= 0 && index < list.length) { list.splice(index, 1); changed(true); }
                return;
            }
            if (action === 'add-tag') { addTag(button.closest('.aw-rb-tag-entry').querySelector('[data-tag-input]')); return; }
            if (action === 'remove-tag') {
                const list = getPath(state, button.dataset.listPath);
                if (Array.isArray(list) && Number.isInteger(index) && index >= 0 && index < list.length) { list.splice(index, 1); changed(true); }
                return;
            }
            if (action === 'move-section-up' || action === 'move-section-down') {
                if (moveItem(state.sections, index, action.endsWith('up') ? -1 : 1)) changed(true);
                return;
            }
            if (action === 'toggle-section' && state.sections[index]) { state.sections[index].isVisible = !state.sections[index].isVisible; changed(true); return; }
            const sectionIndex = Number(button.dataset.sectionIndex);
            const entryIndex = Number(button.dataset.entryIndex);
            const customSection = state.customSections[sectionIndex];
            if (action === 'add-custom-entry' && customSection) {
                if (customSection.entries.length >= LIMITS.customEntries) { notifyLimit('That custom section has reached its entry limit.'); return; }
                customSection.entries.push(customEntryFactory()); showSection('customSections'); changed(true); return;
            }
            if (action === 'remove-custom-entry' && customSection && customSection.entries[entryIndex]) { customSection.entries.splice(entryIndex, 1); changed(true); return; }
            if ((action === 'move-custom-entry-up' || action === 'move-custom-entry-down') && customSection) {
                if (moveItem(customSection.entries, entryIndex, action.endsWith('up') ? -1 : 1)) changed(true);
            }
        }

        function activateTab(name) {
            root.querySelectorAll('[data-tab]').forEach(function (tab) {
                const active = tab.dataset.tab === name;
                tab.classList.toggle('is-active', active);
                tab.setAttribute('aria-selected', String(active));
                tab.tabIndex = active ? 0 : -1;
            });
            root.querySelectorAll('[data-tab-panel]').forEach(function (panel) {
                const active = panel.dataset.tabPanel === name;
                panel.classList.toggle('is-active', active);
                // CSS shows both columns on desktop; only the compact layout behaves as a true tab switcher.
                panel.setAttribute('aria-hidden', String(isCompactLayout() && !active));
            });
            if (name === 'preview') schedulePreview(0);
        }

        function onLayoutChange() {
            const selected = root.querySelector('[data-tab][aria-selected="true"]');
            const selectedName = selected ? selected.dataset.tab : 'form';
            root.querySelectorAll('[data-tab-panel]').forEach(function (panel) {
                panel.setAttribute('aria-hidden', String(isCompactLayout() && panel.dataset.tabPanel !== selectedName));
            });
        }

        function onInput(event) {
            const control = event.target.closest('[data-field], [data-bind-path]');
            if (!control || !root.contains(control)) return;
            const path = control.dataset.field || control.dataset.bindPath;
            let value = control.type === 'checkbox' ? control.checked : control.value;
            const maximum = control.maxLength > 0 ? control.maxLength : LIMITS.longText;
            if (typeof value === 'string' && value.length > maximum) { value = value.slice(0, maximum); control.value = value; }
            if (!setPath(state, path, value)) return;
            if (path === 'professionalSummary' && summaryCount) summaryCount.textContent = plainRichText(value, LIMITS.summary).length + ' characters - keep the summary to about 3 lines';
            if (control.type === 'checkbox') changed(true);
            else changed(false);
        }

        function onBlur(event) {
            const control = event.target.closest('[data-field], [data-bind-path]');
            if (!control) return;
            touched.add(control.dataset.field || control.dataset.bindPath);
            updateValidation();
        }

        function onClick(event) {
            const tab = event.target.closest('[data-tab]');
            if (tab && root.contains(tab)) { activateTab(tab.dataset.tab); return; }
            const format = event.target.closest('[data-format]');
            if (format && root.contains(format)) { event.preventDefault(); applyTextFormat(format); return; }
            const action = event.target.closest('[data-action]');
            if (action && root.contains(action)) { event.preventDefault(); handleAction(action); }
        }

        function onKeyDown(event) {
            if (event.key === 'Escape') {
                const expandedPreview = root.querySelector('.aw-rb-preview-panel.is-expanded');
                const backdrop = root.querySelector('[data-preview-backdrop]');
                if (expandedPreview && backdrop) {
                    event.preventDefault();
                    handleAction(backdrop);
                    root.querySelector('[data-action="toggle-preview-size"]')?.focus();
                    return;
                }
            }
            const tagInput = event.target.closest('[data-tag-input]');
            if (tagInput && (event.key === 'Enter' || event.key === ',')) { event.preventDefault(); addTag(tagInput); return; }
            const tab = event.target.closest('[data-tab]');
            if (tab && (event.key === 'ArrowLeft' || event.key === 'ArrowRight')) {
                event.preventDefault();
                const tabs = Array.from(root.querySelectorAll('[data-tab]'));
                const current = tabs.indexOf(tab);
                const next = tabs[(current + (event.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length];
                activateTab(next.dataset.tab); next.focus();
            }
        }

        state = loadInitialState();
        renderAll();
        root.addEventListener('input', onInput);
        root.addEventListener('change', onInput);
        root.addEventListener('focusout', onBlur);
        root.addEventListener('click', onClick);
        root.addEventListener('keydown', onKeyDown);
        if (form) form.addEventListener('submit', function (event) { event.preventDefault(); downloadPdf(); });
        if (clearDialog) clearDialog.addEventListener('cancel', function (event) { event.preventDefault(); clearDialog.close(); });
        if (windowObject) {
            windowObject.addEventListener('beforeunload', saveNow);
            windowObject.addEventListener('pagehide', onPageHide);
            windowObject.addEventListener('pageshow', onPageShow);
            windowObject.addEventListener('resize', onLayoutChange);
            windowObject.addEventListener('storage', onStorage);
        }
        activateTab((root.querySelector('[data-tab].is-active') || {}).dataset ? root.querySelector('[data-tab].is-active').dataset.tab : 'form');
        scheduleInitialSectionFocus();
        schedulePreview(0);
        scheduleReadiness(0);

        return {
            getState: function () { return normalizeState(state); },
            setState: function (value) { state = normalizeState(value); touched.clear(); showAllErrors = false; revision += 1; latestBlobRevision = -1; latestPageCount = 0; renderAll(); scheduleSave(); schedulePreview(0); scheduleReadiness(0); },
            saveNow: saveNow,
            refreshPreview: refreshPreview,
            downloadPdf: downloadPdf,
            destroy: function () {
                saveNow();
                destroyed = true;
                windowObject.clearTimeout(saveTimer); windowObject.clearTimeout(previewTimer); windowObject.clearTimeout(readinessTimer);
                windowObject.clearTimeout(initialFocusTimer); windowObject.clearTimeout(targetHighlightTimer);
                revokePreviewUrl();
                root.removeEventListener('input', onInput); root.removeEventListener('change', onInput);
                root.removeEventListener('focusout', onBlur); root.removeEventListener('click', onClick); root.removeEventListener('keydown', onKeyDown);
                windowObject.removeEventListener('beforeunload', saveNow);
                windowObject.removeEventListener('pagehide', onPageHide);
                windowObject.removeEventListener('pageshow', onPageShow);
                windowObject.removeEventListener('resize', onLayoutChange);
                windowObject.removeEventListener('storage', onStorage);
                delete root.dataset.resumeBuilderInitialized;
            }
        };
    }

    function autoInitialize() {
        if (typeof document === 'undefined') return [];
        return Array.from(document.querySelectorAll('[data-resume-builder]')).map(function (root) { return createBuilder(root); }).filter(Boolean);
    }

    return Object.freeze({
        SCHEMA_VERSION: SCHEMA_VERSION,
        LIMITS: LIMITS,
        ONE_PAGE_RECOMMENDATIONS: ONE_PAGE_RECOMMENDATIONS,
        READINESS_DEBOUNCE_MS: READINESS_DEBOUNCE_MS,
        SECTION_DEFAULTS: SECTION_DEFAULTS,
        normalizeBuilderSection: normalizeBuilderSection,
        builderSectionSelector: builderSectionSelector,
        createEmptyState: createEmptyState,
        createSampleState: createSampleState,
        normalizeState: normalizeState,
        parseDraft: parseDraft,
        validateState: validateState,
        isEmail: isEmail,
        isPhone: isPhone,
        isHttpUrl: isHttpUrl,
        formatMonth: formatMonth,
        formatDateRange: formatDateRange,
        hasSectionContent: hasSectionContent,
        richTextRuns: richTextRuns,
        plainRichText: plainRichText,
        onePageGuidance: onePageGuidance,
        evaluateDraftReadiness: evaluateDraftReadiness,
        countPdfPages: countPdfPages,
        buildFilename: buildFilename,
        buildDocumentDefinition: buildDocumentDefinition,
        createBuilder: createBuilder,
        autoInitialize: autoInitialize
    });
}));
