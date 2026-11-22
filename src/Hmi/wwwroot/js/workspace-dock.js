// Grid dispatcher workspace: bottom-dock dragging, keyboard forwarding and per-user layout persistence.
// Small first-party ES module; all workspace state stays in the Blazor circuit, this module only
// forwards pointer/keyboard geometry and remembers dock sizes in localStorage.

export function load(key) {
    try {
        const raw = localStorage.getItem(key);
        return raw ? JSON.parse(raw) : null;
    } catch {
        return null;
    }
}

export function save(key, layout) {
    try {
        localStorage.setItem(key, JSON.stringify(layout));
    } catch {
        // Storage full or blocked: the layout simply is not remembered.
    }
}

export function applyBottomHeight(dock, px) {
    if (dock && Number.isFinite(px))
        dock.style.height = Math.round(px) + 'px';
}

export function attachKeys(dotNet) {
    window.addEventListener('keydown', (e) => {
        const target = e.target;
        if (target && (target.tagName === 'INPUT' || target.tagName === 'SELECT' || target.tagName === 'TEXTAREA' || target.isContentEditable))
            return;
        if (e.key === 'Escape' || e.key === 'Enter' || e.key === 'Delete') {
            e.preventDefault();
            dotNet.invokeMethodAsync('OnWorkspaceKey', e.key);
        }
    });
    return true;
}

export function startBottomDrag(handle, dock, dotNet) {
    handle.addEventListener('pointerdown', (e) => {
        e.preventDefault();
        handle.setPointerCapture(e.pointerId);
        const move = (ev) => {
            const px = Math.min(Math.max(window.innerHeight - ev.clientY, 120), Math.round(window.innerHeight * 0.7));
            applyBottomHeight(dock, px);
            dotNet.invokeMethodAsync('OnBottomResize', px);
        };
        const up = () => {
            handle.removeEventListener('pointermove', move);
            handle.removeEventListener('pointerup', up);
            handle.removeEventListener('pointercancel', up);
        };
        handle.addEventListener('pointermove', move);
        handle.addEventListener('pointerup', up);
        handle.addEventListener('pointercancel', up);
    });
    return true;
}
