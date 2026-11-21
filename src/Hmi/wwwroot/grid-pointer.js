// Geometry and pointer capture only. All editor and viewport state belongs to the Blazor circuit.
export function point(svg, clientX, clientY) {
    const matrix = svg.getScreenCTM();
    if (!matrix) throw new Error('SVG canvas is not attached');
    const p = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
    return { x: p.x, y: p.y };
}
export function capture(svg, pointerId, clientX, clientY) {
    svg.setPointerCapture(pointerId);
    return point(svg, clientX, clientY);
}
export function release(svg, pointerId) {
    if (svg.hasPointerCapture(pointerId)) svg.releasePointerCapture(pointerId);
    return true;
}
