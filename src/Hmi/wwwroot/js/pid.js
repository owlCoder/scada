// Pointer capture and client-to-canvas coordinate mapping for the water P&ID workspace.
// All viewport state (viewBox, selection, menu) lives in the Blazor circuit; this module only reports geometry.
export function point(svg, clientX, clientY) {
    const matrix = svg.getScreenCTM();
    if (!matrix) throw new Error('P&ID canvas is not attached');
    const p = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
    return { x: p.x, y: p.y };
}
export function capture(svg, pointerId) {
    svg.setPointerCapture(pointerId);
    return true;
}
export function release(svg, pointerId) {
    if (svg.hasPointerCapture(pointerId)) svg.releasePointerCapture(pointerId);
    return true;
}

// Container size reporting for the aspect-aware fit (same contract as sld.js).
export function rect(el) {
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
}

const observers = new Map();

export function observeSize(dotNetRef, el) {
    if (observers.has(el)) return true;
    const observer = new ResizeObserver(entries => {
        const r = entries[0].contentRect;
        // 0x0 means the element was hidden or removed (e.g. navigating away): nothing to report.
        if (r.width > 0 && r.height > 0)
            dotNetRef.invokeMethodAsync('OnResize', r.width, r.height);
    });
    observer.observe(el);
    observers.set(el, observer);
    return true;
}

export function unobserveSize(el) {
    const observer = observers.get(el);
    if (observer) {
        observer.disconnect();
        observers.delete(el);
    }
    return true;
}
