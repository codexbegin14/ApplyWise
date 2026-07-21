(() => {
    'use strict';

    const input = document.querySelector('[data-ats-upload-input]');
    const fileName = document.querySelector('[data-ats-file-name]');
    const fileHelp = document.querySelector('[data-ats-file-help]');
    const dropzone = input?.closest('.aw-ats-dropzone');

    input?.addEventListener('change', () => {
        const file = input.files?.[0];
        if (fileName) fileName.textContent = file?.name ?? 'Choose your resume PDF';
        if (fileHelp) {
            fileHelp.textContent = file
                ? `${Math.max(1, Math.ceil(file.size / 1024))} KB selected · ready for ATS checks`
                : 'Use a text-based, unlocked PDF. Scanned image-only files cannot be analyzed.';
        }
        dropzone?.classList.toggle('is-selected', Boolean(file));
    });

    const savedForm = document.querySelector('[data-saved-ats-form]');
    const savedSubmit = savedForm?.querySelector('[data-saved-ats-submit]');
    savedForm?.addEventListener('submit', () => {
        if (!savedForm.checkValidity()) return;
        if (window.jQuery && !window.jQuery(savedForm).valid()) return;
        if (!savedSubmit) return;
        savedSubmit.disabled = true;
        savedSubmit.textContent = 'Checking resume…';
    });

    const result = document.querySelector('[data-analysis-result]');
    if (result && window.location.hash === `#${result.id}`) {
        window.requestAnimationFrame(() => {
            result.scrollIntoView({
                behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'auto' : 'smooth',
                block: 'start'
            });
            result.focus({ preventScroll: true });
        });
    }
})();
