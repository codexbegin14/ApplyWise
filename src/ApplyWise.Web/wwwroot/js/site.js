// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
(() => {
    const sidebar = document.getElementById('appSidebar');
    const menuButton = document.querySelector('.menu-button[data-sidebar-toggle]');
    const toggles = document.querySelectorAll('[data-sidebar-toggle]');
    const mobileQuery = window.matchMedia('(max-width: 800px)');
    const storageKey = 'applywise.sidebar.collapsed';
    if (!sidebar || !menuButton || !toggles.length) return;

    const storeCollapsed = (collapsed) => {
        try { localStorage.setItem(storageKey, String(collapsed)); } catch { }
    };
    const readCollapsed = () => {
        try { return localStorage.getItem(storageKey) === 'true'; } catch { return false; }
    };
    const syncButton = () => {
        const expanded = mobileQuery.matches
            ? document.body.classList.contains('sidebar-open')
            : !document.body.classList.contains('sidebar-collapsed');
        menuButton.setAttribute('aria-expanded', String(expanded));
        menuButton.setAttribute('aria-label', `${expanded ? 'Close' : 'Open'} navigation sidebar`);
        menuButton.title = `${expanded ? 'Close' : 'Open'} navigation sidebar`;
    };
    const applyViewportState = () => {
        document.body.classList.remove('sidebar-open');
        document.body.classList.toggle('sidebar-collapsed', !mobileQuery.matches && readCollapsed());
        syncButton();
    };

    toggles.forEach((button) => {
        button.addEventListener('click', () => {
            if (mobileQuery.matches) {
                document.body.classList.toggle('sidebar-open');
            } else {
                const collapsed = document.body.classList.toggle('sidebar-collapsed');
                storeCollapsed(collapsed);
            }
            syncButton();
        });
    });

    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && document.body.classList.contains('sidebar-open')) {
            document.body.classList.remove('sidebar-open');
            syncButton();
            menuButton.focus();
        }
    });
    mobileQuery.addEventListener?.('change', applyViewportState);
    applyViewportState();
})();

// Give repeated workspace cards a restrained entrance rhythm.
if (!window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    document.querySelectorAll('.aw-slide-up').forEach((element, index) => {
        element.style.animationDelay = `${Math.min(index * 35, 210)}ms`;
    });
}

(() => {
    const widget = document.querySelector('[data-wiso-widget]');
    if (!widget) return;
    const panel = widget.querySelector('[data-wiso-panel]');
    const launcher = widget.querySelector('[data-wiso-open]');
    const close = widget.querySelector('[data-wiso-close]');
    const minimize = widget.querySelector('[data-wiso-minimize]');
    const messages = widget.querySelector('[data-wiso-messages]');
    const form = widget.querySelector('[data-wiso-form]');
    const input = form?.querySelector('input[name="question"]');
    const storageKey = 'applywise.wiso.conversation';
    let busy = false;

    const safeMarkdown = (value) => {
        const escaped = String(value).replace(/[&<>"']/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
        return escaped.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
    };
    const save = () => sessionStorage.setItem(storageKey, messages.innerHTML);
    const addMessage = (text, kind, actions = []) => {
        const row = document.createElement('div'); row.className = `wiso-message ${kind}`;
        row.innerHTML = `<div>${safeMarkdown(text)}</div>`;
        if (actions.length) {
            const actionRow = document.createElement('div'); actionRow.className = 'wiso-actions';
            actions.forEach((action) => { const link = document.createElement('a'); link.href = action.url; link.textContent = action.label; actionRow.appendChild(link); });
            row.appendChild(actionRow);
        }
        messages.appendChild(row); messages.scrollTop = messages.scrollHeight; save();
    };
    const restore = () => {
        const stored = sessionStorage.getItem(storageKey);
        if (stored) messages.innerHTML = stored;
        if (!messages.children.length) addMessage(`Hi ${widget.dataset.firstName}, I’m Wiso. I can check your applications, upcoming interviews, reminders, resume performance, and help you find anything in ApplyWise.`, 'assistant');
    };
    const open = () => { panel.hidden = false; widget.classList.add('is-open'); restore(); input?.focus(); };
    const hide = (focus = true) => { panel.hidden = true; widget.classList.remove('is-open'); if (focus) launcher?.focus(); };
    launcher?.addEventListener('click', open); close?.addEventListener('click', () => hide()); minimize?.addEventListener('click', () => hide());
    widget.querySelectorAll('[data-wiso-suggestions] button').forEach((button) => button.addEventListener('click', () => { input.value = button.textContent; form.requestSubmit(); }));
    document.addEventListener('keydown', (event) => { if (event.key === 'Escape' && !panel.hidden) hide(); });
    form?.addEventListener('submit', async (event) => {
        event.preventDefault(); if (busy || !input.value.trim()) return;
        busy = true; const question = input.value.trim(); input.value = ''; addMessage(question, 'user');
        const typing = document.createElement('div'); typing.className = 'wiso-message assistant wiso-typing'; typing.innerHTML = '<div><span></span><span></span><span></span></div>'; messages.appendChild(typing); messages.scrollTop = messages.scrollHeight;
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value;
            const response = await fetch('/wiso/ask', { method: 'POST', headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token ?? '' }, body: JSON.stringify({ question }) });
            const data = await response.json(); typing.remove(); addMessage(data.message ?? 'I’m not sure about that yet.', 'assistant', data.actions ?? []);
        } catch { typing.remove(); addMessage('Wiso is unavailable right now. Please try again in a moment.', 'assistant'); }
        finally { busy = false; }
    });
    document.querySelectorAll('form[action*="Logout"]').forEach((logout) => logout.addEventListener('submit', () => sessionStorage.removeItem(storageKey)));
    if (!panel.hidden) restore();
})();

(() => {
    const greeting = document.querySelector('[data-local-greeting]');
    const dashboard = document.querySelector('[data-display-name]');
    if (greeting && dashboard) {
        const hour = new Date().getHours();
        const salutation = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
        greeting.textContent = `${salutation}, ${dashboard.dataset.displayName}`;
    }
    const filters = document.querySelectorAll('[data-pipeline-filter]');
    const columns = document.querySelectorAll('[data-pipeline-stage]');
    filters.forEach((filter) => filter.addEventListener('click', () => {
        filters.forEach((item) => item.classList.toggle('is-selected', item === filter));
        const selected = filter.dataset.pipelineFilter;
        columns.forEach((column) => { column.hidden = selected !== 'all' && column.dataset.pipelineStage !== selected; });
    }));
})();

document.querySelectorAll('[data-password-toggle]').forEach((button) => {
    const inputId = button.getAttribute('aria-controls');
    const input = inputId ? document.getElementById(inputId) : null;
    const text = button.querySelector('[data-password-toggle-text]');
    if (!(input instanceof HTMLInputElement)) {
        button.hidden = true;
        return;
    }

    button.addEventListener('click', () => {
        const willShow = input.type === 'password';
        input.type = willShow ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(willShow));
        button.setAttribute('aria-label', `${willShow ? 'Hide' : 'Show'} ${inputId.includes('Confirm') ? 'confirmation password' : 'password'}`);
        if (text) text.textContent = willShow ? 'Hide' : 'Show';
    });
});

(() => {
    const menu = document.querySelector('[data-account-menu]');
    const trigger = menu?.querySelector('[data-account-menu-trigger]');
    const panel = menu?.querySelector('[data-account-menu-panel]');
    if (!menu || !trigger || !panel) return;

    const close = (restoreFocus = false) => {
        panel.hidden = true;
        trigger.setAttribute('aria-expanded', 'false');
        if (restoreFocus) trigger.focus();
    };
    const open = () => {
        panel.hidden = false;
        trigger.setAttribute('aria-expanded', 'true');
    };

    trigger.addEventListener('click', () => panel.hidden ? open() : close());
    document.addEventListener('click', (event) => {
        if (!menu.contains(event.target)) close();
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !panel.hidden) close(true);
    });
})();

document.querySelectorAll('[data-confirm-redirect]').forEach((container) => {
    const target = container.dataset.confirmRedirect;
    if (!target || !target.startsWith('/') || target.startsWith('//')) return;

    const configuredSeconds = Number.parseInt(container.dataset.confirmRedirectSeconds ?? '5', 10);
    let remaining = Number.isFinite(configuredSeconds) && configuredSeconds > 0 ? configuredSeconds : 5;
    const countdown = container.querySelector('[data-confirm-redirect-countdown]');
    if (countdown) countdown.textContent = String(remaining);

    const interval = window.setInterval(() => {
        remaining -= 1;
        if (countdown) countdown.textContent = String(Math.max(remaining, 0));
        if (remaining <= 0) {
            window.clearInterval(interval);
            window.location.assign(target);
        }
    }, 1000);
});

(() => {
    const container = document.querySelector('[data-custom-fields]');
    const add = document.querySelector('[data-custom-field-add]');
    const list = document.querySelector('[data-custom-fields-list]');
    const template = document.querySelector('[data-custom-field-template]');
    if (!container || !add || !list || !(template instanceof HTMLTemplateElement)) return;

    const reindex = () => {
        list.querySelectorAll('[data-custom-field-row]').forEach((row, index) => {
            row.querySelectorAll('input, label').forEach((item) => {
                ['name', 'id', 'for'].forEach((attribute) => {
                    const value = item.getAttribute(attribute);
                    if (value) item.setAttribute(attribute, value.replace(/CustomFields\[\d+\]|CustomFields_\d+_/g, (match) => match.startsWith('CustomFields[') ? `CustomFields[${index}]` : `CustomFields_${index}_`));
                });
            });
        });
    };

    add.addEventListener('click', () => {
        const index = list.querySelectorAll('[data-custom-field-row]').length;
        list.insertAdjacentHTML('beforeend', template.innerHTML.replaceAll('__index__', String(index)));
        list.querySelector('[data-custom-field-row]:last-child input')?.focus();
    });
    list.addEventListener('click', (event) => {
        const remove = event.target.closest('[data-custom-field-remove]');
        if (!remove) return;
        remove.closest('[data-custom-field-row]')?.remove();
        reindex();
    });
})();

(() => {
    const key = 'applywise-theme';
    const controls = document.querySelectorAll('[data-theme-toggle]');
    const isAuthenticatedWorkspace = document.documentElement.dataset.authenticatedWorkspace === 'true';
    const syncControls = (theme) => {
        const dark = theme === 'dark';
        controls.forEach((control) => {
            if (control instanceof HTMLInputElement) control.checked = dark;
        });
    };
    const applyTheme = (theme, persist = true) => {
        const nextTheme = isAuthenticatedWorkspace && theme === 'dark' ? 'dark' : 'light';
        document.documentElement.dataset.theme = nextTheme;
        document.documentElement.style.colorScheme = nextTheme;
        if (persist) {
            try { localStorage.setItem(key, nextTheme); } catch { }
        }
        syncControls(nextTheme);
    };

    if (!isAuthenticatedWorkspace) {
        applyTheme('light', false);
        return;
    }

    const initialTheme = document.documentElement.dataset.theme === 'dark' ? 'dark' : 'light';
    syncControls(initialTheme);
    controls.forEach((control) => {
        if (control instanceof HTMLInputElement) {
            control.addEventListener('change', () => applyTheme(control.checked ? 'dark' : 'light'));
        }
    });
})();

(() => {
    const dialog = document.querySelector('[data-avatar-dialog]');
    const openButton = document.querySelector('[data-avatar-open]');
    if (!(dialog instanceof HTMLDialogElement) || !openButton) return;

    const closeButton = dialog.querySelector('[data-avatar-close]');
    const viewport = dialog.querySelector('[data-avatar-viewport]');
    const slides = Array.from(dialog.querySelectorAll('[data-avatar-slide]'));
    const previous = dialog.querySelector('[data-avatar-previous]');
    const next = dialog.querySelector('[data-avatar-next]');
    const position = dialog.querySelector('[data-avatar-position]');
    const category = dialog.querySelector('[data-avatar-category]');
    let currentIndex = Math.max(0, slides.findIndex((slide) => slide.dataset.avatarSelected === 'true'));
    let scrollFrame = 0;

    const updateControls = () => {
        if (position) position.textContent = `${currentIndex + 1} of ${slides.length}`;
        if (previous) previous.disabled = currentIndex === 0;
        if (next) next.disabled = currentIndex === slides.length - 1;
        slides.forEach((slide, index) => slide.setAttribute('aria-hidden', String(index !== currentIndex)));
        const activeCategory = slides[currentIndex]?.dataset.avatarCategoryName ?? '';
        if (category && category.value && category.value !== activeCategory) category.value = '';
    };

    const goTo = (index, behavior = 'smooth') => {
        currentIndex = Math.max(0, Math.min(index, slides.length - 1));
        viewport?.scrollTo({ left: currentIndex * viewport.clientWidth, behavior });
        updateControls();
    };

    openButton.addEventListener('click', () => {
        dialog.showModal();
        window.requestAnimationFrame(() => goTo(currentIndex, 'auto'));
    });
    closeButton?.addEventListener('click', () => dialog.close());
    previous?.addEventListener('click', () => goTo(currentIndex - 1));
    next?.addEventListener('click', () => goTo(currentIndex + 1));
    category?.addEventListener('change', () => {
        if (!category.value) return;
        const targetIndex = slides.findIndex((slide) => slide.dataset.avatarCategoryName === category.value);
        if (targetIndex >= 0) goTo(targetIndex);
    });
    viewport?.addEventListener('scroll', () => {
        window.cancelAnimationFrame(scrollFrame);
        scrollFrame = window.requestAnimationFrame(() => {
            currentIndex = Math.round(viewport.scrollLeft / Math.max(viewport.clientWidth, 1));
            updateControls();
        });
    }, { passive: true });
    dialog.addEventListener('click', (event) => {
        if (event.target === dialog) dialog.close();
    });
    window.addEventListener('resize', () => {
        if (dialog.open) goTo(currentIndex, 'auto');
    });
    updateControls();

    const fileInput = document.querySelector('[data-avatar-upload-input]');
    const fileName = document.querySelector('[data-avatar-file-name]');
    const submit = document.querySelector('[data-avatar-upload-submit]');
    const preview = document.querySelector('[data-avatar-preview]');
    let previewUrl;

    fileInput?.addEventListener('change', () => {
        const file = fileInput.files?.[0];
        if (fileName) fileName.textContent = file?.name ?? 'No photo selected';
        if (submit) submit.disabled = !file;
        if (!file || !preview) return;

        if (previewUrl) URL.revokeObjectURL(previewUrl);
        previewUrl = URL.createObjectURL(file);
        const image = preview.querySelector('img');
        const fallback = preview.querySelector('span');
        if (image) {
            image.src = previewUrl;
            image.hidden = false;
        }
        if (fallback) fallback.hidden = true;
    });
})();
