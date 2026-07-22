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
        if (mobileQuery.matches) {
            sidebar.inert = !expanded;
            sidebar.setAttribute('aria-hidden', String(!expanded));
        } else {
            sidebar.inert = false;
            sidebar.removeAttribute('aria-hidden');
        }
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
            if (mobileQuery.matches && document.body.classList.contains('sidebar-open')) {
                window.requestAnimationFrame(() => sidebar.querySelector('a.active, a')?.focus());
            } else if (button !== menuButton) {
                menuButton.focus();
            }
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

(() => {
    const widget = document.querySelector('[data-wiso-widget]');
    const panel = widget?.querySelector('[data-wiso-panel]');
    const launcher = widget?.querySelector('[data-wiso-open]');
    const closeButton = widget?.querySelector('[data-wiso-close]');
    const messages = widget?.querySelector('[data-wiso-messages]');
    const form = widget?.querySelector('[data-wiso-form]');
    const input = form?.querySelector('input[name="question"]');
    const submit = form?.querySelector('[data-wiso-submit]');
    const status = widget?.querySelector('[data-wiso-status]');
    if (!widget || !panel || !launcher || !messages || !form || !(input instanceof HTMLInputElement)) return;

    const storageKey = 'applywise.wiso.conversation.v2';
    let history = [];
    let restored = false;
    let busy = false;

    const safeActions = (actions) => Array.isArray(actions)
        ? actions
            .filter((action) => typeof action?.label === 'string' && typeof action?.url === 'string' && /^\/(?!\/)/.test(action.url))
            .slice(0, 4)
            .map((action) => ({ label: action.label.slice(0, 80), url: action.url }))
        : [];

    const storeHistory = () => {
        try { sessionStorage.setItem(storageKey, JSON.stringify(history.slice(-24))); } catch { }
    };

    const appendFormattedText = (container, value) => {
        String(value).split('**').forEach((part, index) => {
            if (index % 2 === 1) {
                const strong = document.createElement('strong');
                strong.textContent = part;
                container.appendChild(strong);
            } else {
                container.appendChild(document.createTextNode(part));
            }
        });
    };

    const renderMessage = (entry) => {
        const row = document.createElement('div');
        row.className = `wiso-message ${entry.kind === 'user' ? 'user' : 'assistant'}`;
        const copy = document.createElement('div');
        appendFormattedText(copy, entry.text);
        row.appendChild(copy);

        const actions = safeActions(entry.actions);
        if (actions.length) {
            const actionRow = document.createElement('div');
            actionRow.className = 'wiso-actions';
            actions.forEach((action) => {
                const link = document.createElement('a');
                link.href = action.url;
                link.textContent = action.label;
                actionRow.appendChild(link);
            });
            row.appendChild(actionRow);
        }

        messages.appendChild(row);
        messages.scrollTop = messages.scrollHeight;
    };

    const addMessage = (text, kind, actions = []) => {
        const entry = { text: String(text).slice(0, 1200), kind: kind === 'user' ? 'user' : 'assistant', actions: safeActions(actions) };
        history.push(entry);
        history = history.slice(-24);
        renderMessage(entry);
        storeHistory();
    };

    const restore = () => {
        if (restored) return;
        restored = true;
        try {
            const stored = JSON.parse(sessionStorage.getItem(storageKey) ?? '[]');
            if (Array.isArray(stored)) {
                history = stored
                    .filter((entry) => typeof entry?.text === 'string' && (entry.kind === 'user' || entry.kind === 'assistant'))
                    .slice(-24)
                    .map((entry) => ({ text: entry.text.slice(0, 1200), kind: entry.kind, actions: safeActions(entry.actions) }));
            }
        } catch {
            history = [];
            try { sessionStorage.removeItem(storageKey); } catch { }
        }

        if (!history.length) {
            history = [{
                text: `Hi ${widget.dataset.firstName || 'there'}, I’m Wiso. Ask me about your applications, interviews, reminders, or resumes.`,
                kind: 'assistant',
                actions: []
            }];
            storeHistory();
        }
        history.forEach(renderMessage);
    };

    const setOpen = (open, restoreFocus = false) => {
        panel.hidden = !open;
        widget.classList.toggle('is-open', open);
        launcher.setAttribute('aria-expanded', String(open));
        if (open) {
            restore();
            window.requestAnimationFrame(() => input.focus());
        } else if (restoreFocus) {
            launcher.focus();
        }
    };

    const setBusy = (nextBusy) => {
        busy = nextBusy;
        messages.setAttribute('aria-busy', String(nextBusy));
        form.setAttribute('aria-busy', String(nextBusy));
        input.disabled = nextBusy;
        if (submit instanceof HTMLButtonElement) submit.disabled = nextBusy;
        if (status) status.textContent = nextBusy ? 'Wiso is thinking.' : '';
    };

    launcher.addEventListener('click', () => setOpen(true));
    closeButton?.addEventListener('click', () => setOpen(false, true));
    widget.querySelectorAll('[data-wiso-suggestions] button').forEach((button) => {
        button.addEventListener('click', () => {
            input.value = button.textContent?.trim() ?? '';
            form.requestSubmit();
        });
    });
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && !panel.hidden) setOpen(false, true);
    });

    form.addEventListener('submit', async (event) => {
        event.preventDefault();
        const question = input.value.trim();
        if (busy || !question) return;

        input.value = '';
        addMessage(question, 'user');
        setBusy(true);
        const typing = document.createElement('div');
        typing.className = 'wiso-message assistant wiso-typing';
        typing.setAttribute('aria-hidden', 'true');
        typing.innerHTML = '<div><span></span><span></span><span></span></div>';
        messages.appendChild(typing);
        messages.scrollTop = messages.scrollHeight;

        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
            const response = await fetch('/wiso/ask', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: token },
                body: JSON.stringify({ question })
            });
            const isJson = response.headers.get('content-type')?.includes('application/json') === true;
            if (response.redirected || !isJson) throw new Error('Your session may have expired. Please refresh and log in again.');
            const data = await response.json();
            if (!response.ok) throw new Error(data?.message || 'Wiso could not answer that question.');
            addMessage(data?.message || 'I’m not sure about that yet.', 'assistant', data?.actions);
        } catch (error) {
            addMessage(error instanceof Error ? error.message : 'Wiso is unavailable right now. Please try again.', 'assistant');
        } finally {
            typing.remove();
            setBusy(false);
            input.focus();
        }
    });

    document.querySelectorAll('form[action*="Logout"]').forEach((logout) => {
        logout.addEventListener('submit', () => {
            try { sessionStorage.removeItem(storageKey); } catch { }
        });
    });
})();

(() => {
    const greeting = document.querySelector('[data-local-greeting]');
    const dashboard = document.querySelector('[data-display-name]');
    if (greeting && dashboard) {
        const hour = new Date().getHours();
        const salutation = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening';
        greeting.textContent = `${salutation}, ${dashboard.dataset.displayName}`;
    }
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
