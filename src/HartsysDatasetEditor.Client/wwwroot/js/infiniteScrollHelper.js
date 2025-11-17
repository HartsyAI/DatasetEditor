// infiniteScrollHelper.js - IntersectionObserver for infinite scroll
window.infiniteScrollHelper = {
    observer: null,
    dotNetRef: null,

    /**
     * Initialize IntersectionObserver to detect when sentinel element becomes visible
     * @param {object} dotNetReference - .NET object reference to call back
     * @param {string} sentinelId - ID of the sentinel element to observe
     * @param {number} rootMargin - Margin in pixels to trigger before sentinel is visible (default: 500px)
     */
    initialize: function (dotNetReference, sentinelId, rootMargin = 500) {
        console.log('[InfiniteScroll] Initializing observer for sentinel:', sentinelId);
        
        this.dotNetRef = dotNetReference;
        
        // Clean up existing observer if any
        if (this.observer) {
            this.observer.disconnect();
        }

        // Create IntersectionObserver with specified root margin
        const options = {
            root: null, // viewport
            rootMargin: `${rootMargin}px`, // Trigger before sentinel is actually visible
            threshold: 0.0 // Fire as soon as any pixel is visible
        };

        this.observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    console.log('[InfiniteScroll] Sentinel visible, requesting more items');
                    // Call back to .NET to load more items
                    dotNetReference.invokeMethodAsync('OnScrolledToBottom');
                }
            });
        }, options);

        // Find and observe the sentinel element
        const sentinel = document.getElementById(sentinelId);
        if (sentinel) {
            this.observer.observe(sentinel);
            console.log('[InfiniteScroll] Observer attached to sentinel');
        } else {
            console.error('[InfiniteScroll] Sentinel element not found:', sentinelId);
        }
    },

    /**
     * Disconnect the observer and clean up
     */
    dispose: function () {
        console.log('[InfiniteScroll] Disposing observer');
        if (this.observer) {
            this.observer.disconnect();
            this.observer = null;
        }
        this.dotNetRef = null;
    },

    /**
     * Manually trigger a check (useful for debugging)
     */
    triggerCheck: function () {
        console.log('[InfiniteScroll] Manual trigger check');
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnScrolledToBottom');
        }
    }
};
