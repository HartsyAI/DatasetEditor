// Hartsy's Dataset Editor - JavaScript Interop
// Provides browser-specific functionality to Blazor via JS Interop

window.interop = {
    /**
     * Reads a file as text from an input element
     * @param {HTMLInputElement} inputElement - File input element
     * @returns {Promise<string>} File content as text
     */
    readFileAsText: function (inputElement) {
        return new Promise((resolve, reject) => {
            if (!inputElement || !inputElement.files || inputElement.files.length === 0) {
                reject('No file selected');
                return;
            }

            const file = inputElement.files[0];
            const reader = new FileReader();

            reader.onload = (event) => {
                resolve(event.target.result);
            };

            reader.onerror = (error) => {
                reject(`Error reading file: ${error}`);
            };

            reader.readAsText(file);
        });
    },

    /**
     * Gets file information without reading content
     * @param {HTMLInputElement} inputElement - File input element
     * @returns {Object} File metadata
     */
    getFileInfo: function (inputElement) {
        if (!inputElement || !inputElement.files || inputElement.files.length === 0) {
            return null;
        }

        const file = inputElement.files[0];
        return {
            name: file.name,
            size: file.size,
            type: file.type,
            lastModified: new Date(file.lastModified)
        };
    },

    /**
     * Checks if a file is selected
     * @param {HTMLInputElement} inputElement - File input element
     * @returns {boolean} True if file is selected
     */
    hasFile: function (inputElement) {
        return inputElement && inputElement.files && inputElement.files.length > 0;
    },

    /**
     * Sets up IntersectionObserver for lazy loading images
     * @param {HTMLElement} element - Image element to observe
     */
    observeLazyLoad: function (element) {
        if (!element) return;

        // Check if IntersectionObserver is supported
        if (!('IntersectionObserver' in window)) {
            // Fallback: Load image immediately
            if (element.dataset.src) {
                element.src = element.dataset.src;
            }
            return;
        }

        const observer = new IntersectionObserver(
            (entries) => {
                entries.forEach((entry) => {
                    if (entry.isIntersecting) {
                        const img = entry.target;
                        
                        // Load the actual image
                        if (img.dataset.src) {
                            img.src = img.dataset.src;
                            img.classList.remove('image-loading');
                        }
                        
                        // Stop observing this image
                        observer.unobserve(img);
                    }
                });
            },
            {
                rootMargin: '50px', // Start loading 50px before image enters viewport
                threshold: 0.01
            }
        );

        observer.observe(element);
    },

    /**
     * Downloads a blob as a file
     * @param {string} filename - Name for the downloaded file
     * @param {string} contentType - MIME type
     * @param {Uint8Array} data - File data
     */
    downloadFile: function (filename, contentType, data) {
        const blob = new Blob([data], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
        URL.revokeObjectURL(url);
    },

    /**
     * Copies text to clipboard
     * @param {string} text - Text to copy
     * @returns {Promise<boolean>} True if successful
     */
    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            console.error('Failed to copy text:', err);
            return false;
        }
    },

    /**
     * Gets the current browser window size
     * @returns {Object} Width and height
     */
    getWindowSize: function () {
        return {
            width: window.innerWidth,
            height: window.innerHeight
        };
    },

    /**
     * Scrolls an element into view
     * @param {HTMLElement} element - Element to scroll to
     * @param {boolean} smooth - Use smooth scrolling
     */
    scrollIntoView: function (element, smooth = true) {
        if (!element) return;
        element.scrollIntoView({
            behavior: smooth ? 'smooth' : 'auto',
            block: 'nearest'
        });
    },

    /**
     * Sets focus on an element
     * @param {HTMLElement} element - Element to focus
     */
    focusElement: function (element) {
        if (element) {
            element.focus();
        }
    },

    /**
     * Programmatically clicks an element
     * @param {HTMLElement} element - Element to click
     */
    clickElement: function (element) {
        if (element) {
            element.click();
        }
    },

    /**
     * Programmatically clicks an element by id
     * @param {string} id - The element id attribute
     */
    clickElementById: function (id) {
        const element = document.getElementById(id);
        if (element) {
            element.click();
        }
    }
};

// Additional file reader utilities
window.fileReader = {
    /**
     * Reads file as text
     * @param {File} file - File object
     * @returns {Promise<string>} File content
     */
    readAsText: async function (file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(reader.error);
            reader.readAsText(file);
        });
    },

    /**
     * Reads file as data URL (base64)
     * @param {File} file - File object
     * @returns {Promise<string>} Base64 data URL
     */
    readAsDataURL: async function (file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(reader.error);
            reader.readAsDataURL(file);
        });
    }
};

// Console logging for debugging (can be removed in production)
console.log('Hartsy\'s Dataset Editor - Interop loaded');

// TODO: Add zoom/pan functionality for image viewer
// TODO: Add keyboard shortcut handling
// TODO: Add drag-drop file handling
// TODO: Add IndexedDB wrapper for large dataset caching
// TODO: Add Web Worker for background processing
