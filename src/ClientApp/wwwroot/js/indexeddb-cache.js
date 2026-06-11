/**
 * IndexedDB Cache Manager for Hartsy Dataset Editor
 * Uses Dexie.js for simplified IndexedDB operations
 */
window.indexedDbCache = {
    db: null,
    
    /**
     * Initializes the IndexedDB database
     */
    async initialize() {
        try {
            this.db = new Dexie('HartsyDatasetEditor');
            
            this.db.version(1).stores({
                // Dataset items keyed by id
                items: 'id, datasetId, title, createdAt',
                
                // Cached pages keyed by [datasetId+page]
                pages: '[datasetId+page], datasetId, page, cachedAt',
                
                // Dataset metadata
                datasets: 'id, name, updatedAt',
                
                // General key-value cache
                cache: 'key, expiresAt'
            });
            
            await this.db.open();
            console.log('✅ IndexedDB cache initialized');
            
            // Clean expired cache on startup
            await this.cleanExpiredCache();
            
            return true;
        } catch (error) {
            console.error('❌ Failed to initialize IndexedDB', error);
            return false;
        }
    },
    
    /**
     * Saves multiple items to cache
     */
    async saveItems(items) {
        try {
            await this.db.items.bulkPut(items);
            console.log(`✅ Cached ${items.length} items`);
            return true;
        } catch (error) {
            console.error('❌ Failed to save items', error);
            return false;
        }
    },
    
    /**
     * Gets items for a specific dataset with pagination
     */
    async getItems(datasetId, page, pageSize) {
        try {
            const items = await this.db.items
                .where('datasetId').equals(datasetId)
                .offset(page * pageSize)
                .limit(pageSize)
                .toArray();
            
            console.log(`📦 Retrieved ${items.length} items from cache`);
            return items;
        } catch (error) {
            console.error('❌ Failed to get items', error);
            return [];
        }
    },
    
    /**
     * Saves a page of items
     */
    async savePage(datasetId, page, items) {
        try {
            const pageData = {
                datasetId: datasetId,
                page: page,
                items: items,
                cachedAt: new Date().toISOString(),
                itemCount: items.length
            };
            
            await this.db.pages.put(pageData);
            
            // Also save individual items
            await this.saveItems(items);
            
            console.log(`✅ Cached page ${page} with ${items.length} items`);
            return true;
        } catch (error) {
            console.error('❌ Failed to save page', error);
            return false;
        }
    },
    
    /**
     * Gets a cached page
     */
    async getPage(datasetId, page) {
        try {
            const pageData = await this.db.pages.get([datasetId, page]);
            
            if (!pageData) {
                console.log(`💤 Cache miss for page ${page}`);
                return null;
            }
            
            // Check if cache is expired (older than 1 hour)
            const cachedAt = new Date(pageData.cachedAt);
            const now = new Date();
            const hoursSinceCached = (now - cachedAt) / 1000 / 60 / 60;
            
            if (hoursSinceCached > 1) {
                console.log(`⏰ Cache expired for page ${page} (${hoursSinceCached.toFixed(2)}h old)`);
                return null;
            }
            
            console.log(`🎯 Cache hit for page ${page}`);
            return pageData;
        } catch (error) {
            console.error('❌ Failed to get page', error);
            return null;
        }
    },
    
    /**
     * Clears all cached data for a specific dataset
     */
    async clearDataset(datasetId) {
        try {
            await this.db.items.where('datasetId').equals(datasetId).delete();
            await this.db.pages.where('datasetId').equals(datasetId).delete();
            console.log(`🧹 Cleared cache for dataset ${datasetId}`);
            return true;
        } catch (error) {
            console.error('❌ Failed to clear dataset', error);
            return false;
        }
    },
    
    /**
     * Saves dataset metadata
     */
    async saveDataset(dataset) {
        try {
            await this.db.datasets.put(dataset);
            console.log(`✅ Cached dataset: ${dataset.name}`);
            return true;
        } catch (error) {
            console.error('❌ Failed to save dataset', error);
            return false;
        }
    },
    
    /**
     * Gets dataset metadata
     */
    async getDataset(datasetId) {
        try {
            return await this.db.datasets.get(datasetId);
        } catch (error) {
            console.error('❌ Failed to get dataset', error);
            return null;
        }
    },
    
    /**
     * Saves a value to general cache with optional expiration
     */
    async setCacheValue(key, value, expiresInMinutes = 60) {
        try {
            const expiresAt = new Date();
            expiresAt.setMinutes(expiresAt.getMinutes() + expiresInMinutes);
            
            await this.db.cache.put({
                key: key,
                value: value,
                expiresAt: expiresAt.toISOString()
            });
            
            console.log(`✅ Cached key: ${key} (expires in ${expiresInMinutes}m)`);
            return true;
        } catch (error) {
            console.error('❌ Failed to set cache value', error);
            return false;
        }
    },
    
    /**
     * Gets a value from general cache
     */
    async getCacheValue(key) {
        try {
            const entry = await this.db.cache.get(key);
            
            if (!entry) {
                return null;
            }
            
            // Check expiration
            const expiresAt = new Date(entry.expiresAt);
            const now = new Date();
            
            if (now > expiresAt) {
                await this.db.cache.delete(key);
                console.log(`⏰ Cache key expired: ${key}`);
                return null;
            }
            
            return entry.value;
        } catch (error) {
            console.error('❌ Failed to get cache value', error);
            return null;
        }
    },
    
    /**
     * Cleans up expired cache entries
     */
    async cleanExpiredCache() {
        try {
            const now = new Date().toISOString();
            const deleted = await this.db.cache.where('expiresAt').below(now).delete();
            if (deleted > 0) {
                console.log(`🧹 Cleaned ${deleted} expired cache entries`);
            }
        } catch (error) {
            console.error('❌ Failed to clean cache', error);
        }
    },
    
    /**
     * Gets cache statistics
     */
    async getCacheStats() {
        try {
            const itemCount = await this.db.items.count();
            const pageCount = await this.db.pages.count();
            const datasetCount = await this.db.datasets.count();
            
            return {
                items: itemCount,
                pages: pageCount,
                datasets: datasetCount
            };
        } catch (error) {
            console.error('❌ Failed to get cache stats', error);
            return null;
        }
    },
    
    /**
     * Clears all cached data
     */
    async clearAll() {
        try {
            await this.db.items.clear();
            await this.db.pages.clear();
            await this.db.datasets.clear();
            await this.db.cache.clear();
            console.log('🧹 All cache cleared');
            return true;
        } catch (error) {
            console.error('❌ Failed to clear cache', error);
            return false;
        }
    }
};

// Auto-initialize on load
indexedDbCache.initialize();
