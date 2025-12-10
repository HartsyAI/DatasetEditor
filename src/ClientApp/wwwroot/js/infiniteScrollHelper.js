// infiniteScrollHelper.js - IntersectionObserver for infinite scroll
window.infiniteScrollHelper = {
    observer: null,
    dotNetRef: null,
    topSentinelId: null,
    bottomSentinelId: null,

    /**
     * Initialize IntersectionObserver to detect when top/bottom sentinels become visible
     * @param {object} dotNetReference - .NET object reference to call back
     * @param {string} topSentinelId - ID of the top sentinel element to observe
     * @param {string} bottomSentinelId - ID of the bottom sentinel element to observe
     * @param {number} rootMargin - Margin in pixels to trigger before sentinel is visible (default: 500px)
     */
    initialize: function (dotNetReference, topSentinelId, bottomSentinelId, rootMargin = 500) {
        console.log('[InfiniteScroll] Initializing observers for sentinels:', topSentinelId, bottomSentinelId);
        
        this.dotNetRef = dotNetReference;
        this.topSentinelId = topSentinelId;
        this.bottomSentinelId = bottomSentinelId;
        
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
                if (!entry.isIntersecting) {
                    return;
                }

                const targetId = entry.target.id;
                if (targetId === this.bottomSentinelId) {
                    console.log('[InfiniteScroll] Bottom sentinel visible, requesting more items');
                    // Call back to .NET to load more items
                    dotNetReference.invokeMethodAsync('OnScrolledToBottom');
                } else if (targetId === this.topSentinelId) {
                    console.log('[InfiniteScroll] Top sentinel visible, requesting previous items');
                    // Call back to .NET to load previous items
                    dotNetReference.invokeMethodAsync('OnScrolledToTop');
                }
            });
        }, options);

        // Find and observe the top sentinel element
        const top = document.getElementById(topSentinelId);
        if (top) {
            this.observer.observe(top);
            console.log('[InfiniteScroll] Observer attached to top sentinel');
        } else {
            console.warn('[InfiniteScroll] Top sentinel element not found:', topSentinelId);
        }

        // Find and observe the bottom sentinel element
        const bottom = document.getElementById(bottomSentinelId);
        if (bottom) {
            this.observer.observe(bottom);
            console.log('[InfiniteScroll] Observer attached to bottom sentinel');
        } else {
            console.error('[InfiniteScroll] Bottom sentinel element not found:', bottomSentinelId);
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
        this.topSentinelId = null;
        this.bottomSentinelId = null;
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
