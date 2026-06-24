// viewportInterop.js - measures a scroll container's width and reports resizes.
// Replaces the old sentinel-based infiniteScrollHelper now that scrolling/prefetch
// is driven by Blazor's <Virtualize> component (see VirtualizedItemView).
window.viewportInterop = {
    _observers: new Map(),

    /** Returns the element's current content width in px (0 if missing). */
    getWidth: function (el) {
        return el ? el.clientWidth : 0;
    },

    /** Observes width changes and calls back into .NET with the new width. */
    observeWidth: function (el, dotNetRef) {
        if (!el || typeof ResizeObserver === 'undefined') {
            return;
        }
        this.unobserve(el);
        const ro = new ResizeObserver(entries => {
            for (const entry of entries) {
                const width = entry.contentRect ? entry.contentRect.width : el.clientWidth;
                dotNetRef.invokeMethodAsync('OnContainerResized', width);
            }
        });
        ro.observe(el);
        this._observers.set(el, ro);
    },

    /** Stops observing an element and releases its ResizeObserver. */
    unobserve: function (el) {
        const ro = this._observers.get(el);
        if (ro) {
            ro.disconnect();
            this._observers.delete(el);
        }
    }
};
