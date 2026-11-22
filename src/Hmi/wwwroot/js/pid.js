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
