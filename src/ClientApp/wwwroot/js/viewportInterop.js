// viewportInterop.js - container measurement and end-of-list detection for VirtualizedItemView.
// Resize notifications are debounced (one .NET call per settled width, not one per frame), and
// the end of the list is watched with an IntersectionObserver on a sentinel element instead of
// a prefetch check run inside Blazor's row template on every render.
window.viewportInterop = {
    _resize: new Map(),   // container -> { observer, timer }
    _sentinels: new Map(), // container -> IntersectionObserver
    _debounceMs: 120,

    /** Returns the element's current content width in px (0 if missing). */
    getWidth: function (el) {
        return el ? el.clientWidth : 0;
    },

    /** Observes width changes and calls OnContainerResized(width) once the width settles. */
    observeWidth: function (el, dotNetRef) {
        if (!el || typeof ResizeObserver === 'undefined') {
            return;
        }
        this._unobserveWidth(el);
        const state = { observer: null, timer: 0 };
        state.observer = new ResizeObserver(entries => {
            const entry = entries[entries.length - 1];
            const width = entry.contentRect ? entry.contentRect.width : el.clientWidth;
            clearTimeout(state.timer);
            state.timer = setTimeout(() => {
                dotNetRef.invokeMethodAsync('OnContainerResized', width);
            }, this._debounceMs);
        });
        state.observer.observe(el);
        this._resize.set(el, state);
    },

    /**
     * Watches a sentinel at the end of the list inside a scroll container and calls
     * OnSentinelVisible() whenever it comes within marginPx of the visible area. Calling this
     * again for the same container replaces the previous observer; the new observer reports the
     * current state immediately, which re-checks after each appended page.
     */
    observeSentinel: function (sentinel, container, dotNetRef, marginPx) {
        if (!sentinel || !container || typeof IntersectionObserver === 'undefined') {
            return;
        }
        const previous = this._sentinels.get(container);
        if (previous) {
            previous.disconnect();
        }
        const io = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    dotNetRef.invokeMethodAsync('OnSentinelVisible');
                }
            }
        }, { root: container, rootMargin: '0px 0px ' + (marginPx || 0) + 'px 0px' });
        io.observe(sentinel);
        this._sentinels.set(container, io);
    },

    /** Stops every observer attached to a container. */
    unobserve: function (el) {
        this._unobserveWidth(el);
        const io = this._sentinels.get(el);
        if (io) {
            io.disconnect();
            this._sentinels.delete(el);
        }
    },

    _unobserveWidth: function (el) {
        const state = this._resize.get(el);
        if (state) {
            clearTimeout(state.timer);
            state.observer.disconnect();
            this._resize.delete(el);
        }
    }
};
