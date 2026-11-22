// Dispatcher shell: forwards keyboard shortcuts and reports browser connectivity.
// All state and decisions stay in the Blazor circuit; this module only observes the DOM.

let keyHandler = null;

function editable(target) {
    return target instanceof HTMLElement &&
        (target.isContentEditable || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT');
}

function interesting(event) {
    if (event.ctrlKey || event.metaKey) return true;               // Ctrl/Cmd chords, e.g. Ctrl+K, Ctrl+Shift+A
    if (event.key === 'F2' || event.key === 'F3' || event.key === 'Escape') return true;
    return event.key === '?' && !editable(event.target);           // '?' typed into a field is just text
}

export function registerKeys(dotNet) {
    if (keyHandler) document.removeEventListener('keydown', keyHandler);
    keyHandler = (event) => {
        if (!interesting(event)) return;
        if (editable(event.target) && !event.ctrlKey && !event.metaKey && event.key !== 'Escape') return;
        event.preventDefault();
        dotNet.invokeMethodAsync('OnKeyAsync', {
            key: event.key,
            ctrl: event.ctrlKey,
            shift: event.shiftKey,
            alt: event.altKey,
            meta: event.metaKey
        });
    };
    document.addEventListener('keydown', keyHandler);
    return true;
}

export function watchConnection(dotNet) {
    const report = () => dotNet.invokeMethodAsync('OnConnectionAsync', navigator.onLine);
    window.addEventListener('online', report);
    window.addEventListener('offline', report);
    report();
    return navigator.onLine;
}
