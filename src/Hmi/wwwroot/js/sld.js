// Single-line diagram interop: pointer capture, client<->SVG coordinate transforms, overlay placement and a
// resize observer. All pan/zoom/selection state stays in the Blazor circuit; this module only reports geometry.
const observers = new Map();

export function point(svg, clientX, clientY) {
    const matrix = svg.getScreenCTM();
    if (!matrix) throw new Error('SVG canvas is not attached');
    const p = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
    return { x: p.x, y: p.y };
}

export function clientPoint(svg, x, y) {
    const matrix = svg.getScreenCTM();
    if (!matrix) throw new Error('SVG canvas is not attached');
    const p = new DOMPoint(x, y).matrixTransform(matrix);
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

export function rect(el) {
    const r = el.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
}

// Position a fixed overlay (context menu, tooltip) at client coordinates via the CSSOM, so no inline style
// attribute is ever emitted into the markup.
export function placeAt(el, clientX, clientY) {
    el.style.left = `${Math.round(clientX)}px`;
    el.style.top = `${Math.round(clientY)}px`;
    return true;
}

export function observeSize(dotNetRef, el) {
    if (observers.has(el)) return true;
    const observer = new ResizeObserver(entries => {
        const r = entries[0].contentRect;
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
