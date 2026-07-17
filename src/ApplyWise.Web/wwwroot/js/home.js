(() => {
    const header = document.querySelector('[data-home-header]');
    const toggle = header?.querySelector('[data-home-nav-toggle]');
    const navigation = header?.querySelector('[data-home-nav]');
    if (!header || !toggle || !navigation) return;

    const close = (restoreFocus = false) => {
        header.classList.remove('is-open');
        toggle.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-label', 'Open navigation');
        if (restoreFocus) toggle.focus();
    };

    const open = () => {
        header.classList.add('is-open');
        toggle.setAttribute('aria-expanded', 'true');
        toggle.setAttribute('aria-label', 'Close navigation');
    };

    toggle.hidden = false;
    header.classList.add('is-nav-ready');
    toggle.addEventListener('click', () => header.classList.contains('is-open') ? close() : open());
    navigation.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => close()));
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && header.classList.contains('is-open')) close(true);
    });
    document.addEventListener('click', (event) => {
        if (header.classList.contains('is-open') && !header.contains(event.target)) close();
    });
    window.matchMedia('(min-width: 761px)').addEventListener?.('change', (event) => {
        if (event.matches) close();
    });
})();
