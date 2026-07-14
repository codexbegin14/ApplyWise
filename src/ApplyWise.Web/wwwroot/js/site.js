// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
document.querySelectorAll('[data-sidebar-toggle]').forEach((button) => {
    button.addEventListener('click', () => {
        const isOpen = document.body.classList.toggle('sidebar-open');
        document.querySelector('.menu-button')?.setAttribute('aria-expanded', String(isOpen));
    });
});

document.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && document.body.classList.contains('sidebar-open')) {
        document.body.classList.remove('sidebar-open');
        document.querySelector('.menu-button')?.setAttribute('aria-expanded', 'false');
        document.querySelector('.menu-button')?.focus();
    }
});

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
