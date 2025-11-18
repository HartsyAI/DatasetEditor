# 🚀 Quick Start Guide - Hartsy Dataset Editor

## Prerequisites

- .NET 8.0 SDK or later
- Modern web browser (Chrome, Firefox, Edge)
- Git (for cloning)

---

## 🏃 Running the Application

### 1. Start the API Server

```powershell
# Terminal 1
cd src/HartsysDatasetEditor.Api
dotnet watch run
```

**Expected output:**
```
✅ Now listening on: https://localhost:7085
✅ Now listening on: http://localhost:5085
```

### 2. Start the Client Application

```powershell
# Terminal 2
cd src/HartsysDatasetEditor.Client
dotnet watch run
```

**Expected output:**
```
✅ Now listening on: https://localhost:7221
✅ Now listening on: http://localhost:5221
```

### 3. Open in Browser

Navigate to: **https://localhost:7221**

---

## 🎯 Quick Feature Tour

### 1. Upload Your First Dataset

1. Click **"Upload"** in navigation
2. Select a CSV file (or photos.csv + colors.csv + tags.csv)
3. Click **"Upload Dataset"**
4. Wait for processing
5. View your dataset!

### 2. Browse Datasets

1. Click **"My Datasets"** in navigation
2. See all your datasets in a grid
3. Use search box to filter
4. Click **"View"** on any dataset

### 3. Switch Layouts

1. In dataset viewer, click layout icon in toolbar
2. Try: **Grid**, **List**, **Masonry**, or **Slideshow**
3. Adjust columns with slider (Grid/Masonry only)

### 4. Edit Images

1. Click any image to open detail panel
2. Click edit icon next to **Title**
3. Type new title, press **Enter**
4. Click **"+"** to add tags
5. Click **X** on tags to remove them
6. Toggle **favorite star** ⭐

### 5. Check Cache

1. Open browser DevTools (**F12**)
2. Go to **Console** tab
3. Scroll dataset to load pages
4. See cache logs:
   ```
   [CACHE SAVED] Page 1 with 100 items
   [CACHE HIT] Page 2 loaded from IndexedDB (100 items)
   ```

---

## 🧪 Quick Test Scenarios

### Test 1: Single File Upload (30 seconds)

**Goal:** Upload and view a simple dataset

**Steps:**
1. Create `test.csv`:
   ```csv
   photo_id,photo_image_url,photo_description
   1,https://images.unsplash.com/photo-1,Beautiful mountain
   2,https://images.unsplash.com/photo-2,Ocean waves
   3,https://images.unsplash.com/photo-3,Forest trail
   ```
2. Go to `/upload`
3. Select `test.csv`
4. Click Upload
5. ✅ **Success:** 3 images display in grid

### Test 2: Multi-File Upload with Enrichment (1 minute)

**Goal:** Test enrichment merging

**Files:**
- `photos.csv` (from Test 1)
- `colors.csv`:
  ```csv
  photo_id,hex
  1,#8B4513
  2,#FF7F50
  3,#228B22
  ```
- `tags.csv`:
  ```csv
  photo_id,tag
  1,nature
  1,mountain
  2,ocean
  3,forest
  ```

**Steps:**
1. Go to `/upload`
2. Select all 3 files
3. ✅ **Success:** Auto-detection shows:
   - Primary: photos.csv
   - Enrichments: colors.csv (colors), tags.csv (tags)
4. Upload
5. ✅ **Success:** Images have color chips and tags

### Test 3: Layout Switching (30 seconds)

**Steps:**
1. Open any dataset
2. Click layout icon → Select **"List"**
3. ✅ **Success:** Large thumbnails with horizontal cards
4. Click layout icon → Select **"Grid"**
5. Move slider to **6 columns**
6. ✅ **Success:** Grid adjusts to 6 columns
7. Refresh page
8. ✅ **Success:** Layout persists (still 6 columns)

### Test 4: Inline Editing (1 minute)

**Steps:**
1. Click any image
2. Click edit icon next to title
3. Type "New Title" → Press **Enter**
4. ✅ **Success:** Title updates, success notification
5. Click **"+ Add Tag"**
6. Type "test-tag" → Click **"Add"**
7. ✅ **Success:** Tag appears as chip
8. Click **X** on tag
9. ✅ **Success:** Tag removed, success notification
10. Toggle favorite star
11. ✅ **Success:** Star fills/unfills

### Test 5: Cache Verification (2 minutes)

**Steps:**
1. Open dataset with 200+ items
2. Open DevTools (**F12**) → Console tab
3. Scroll down to load Page 2
4. ✅ **Success:** Console shows:
   ```
   [CACHE SAVED] Page 2 with 100 items
   ```
5. Scroll back up, then down again
6. ✅ **Success:** Console shows:
   ```
   [CACHE HIT] Page 2 loaded from IndexedDB (100 items)
   ```
7. DevTools → Application → IndexedDB → HartsyDatasetEditor
8. ✅ **Success:** See tables: items, pages, datasets, cache

### Test 6: Offline Mode (1 minute)

**Steps:**
1. Load a dataset (let first page load)
2. DevTools → Network tab
3. Set **"Offline"** mode
4. Scroll to load Page 2
5. ✅ **Success:** Page loads from cache (no network errors)
6. Try to load Page 3 (not cached)
7. ✅ **Expected:** Network error (graceful)

### Test 7: Search Datasets (30 seconds)

**Steps:**
1. Upload 3+ datasets with different names
2. Go to `/my-datasets`
3. Type search query (e.g., "mountain")
4. ✅ **Success:** Only matching datasets show
5. Clear search
6. ✅ **Success:** All datasets return

---

## 🐛 Troubleshooting

### API Won't Start

**Error:** `Port 7085 is already in use`

**Fix:**
```powershell
# Find process using port 7085
netstat -ano | findstr :7085

# Kill process (replace PID)
taskkill /PID <PID> /F

# Or use different port in appsettings.json
```

### Client Won't Start

**Error:** `Port 7221 is already in use`

**Fix:** Same as API, use port 7221

### Database Issues

**Error:** `LiteDB file is locked`

**Fix:**
```powershell
# Stop API server
# Delete database file
Remove-Item ./data/hartsy.db

# Restart API server
```

### Cache Issues

**Error:** `IndexedDB initialization failed`

**Fix:**
```javascript
// In browser console
await indexedDbCache.clearAll();
location.reload();
```

### Build Errors

**Error:** `Project file not found`

**Fix:**
```powershell
# Restore NuGet packages
dotnet restore

# Clean and rebuild
dotnet clean
dotnet build
```

---

## 📊 Performance Benchmarks

### Expected Timings:

| Operation | Expected Time |
|-----------|--------------|
| API startup | < 3 seconds |
| Client startup | < 5 seconds |
| Single file upload (1K rows) | < 5 seconds |
| Multi-file upload (3 files, 1K rows) | < 10 seconds |
| First page load | < 500ms |
| Cached page load | < 50ms |
| Inline edit save | < 300ms |
| Layout switch | < 100ms |

### If Slower:

1. Check CPU usage (high load?)
2. Check network (API reachable?)
3. Check console for errors
4. Clear cache and retry

---

## 🎓 Learning Resources

### Architecture:
- `docs/architecture.md` - System design
- `PHASE_3_4_COMPLETE.md` - Phases 3 & 4 details
- `PHASE_5_6_7_COMPLETE.md` - Phases 5, 6 & 7 details

### Code Examples:
- `tests/INTEGRATION_TESTS.md` - Test scenarios
- API: `src/HartsysDatasetEditor.Api/`
- Client: `src/HartsysDatasetEditor.Client/`
- Core: `src/HartsysDatasetEditor.Core/`

### External:
- [MudBlazor Docs](https://mudblazor.com/)
- [Blazor Docs](https://learn.microsoft.com/en-us/aspnet/core/blazor/)
- [Dexie.js Docs](https://dexie.org/)

---

## 🆘 Getting Help

### Console Logs:

**Check browser console (F12) for:**
- `✅` Success messages
- `❌` Error messages
- `[CACHE HIT/MISS]` Cache performance
- `💾` Save operations

**Check server console for:**
- API request logs
- Database operations
- Exceptions/errors

### Common Issues:

1. **Images not loading:** Check image URLs are valid
2. **Edits not saving:** Check API is running
3. **Cache not working:** Check IndexedDB in DevTools
4. **Layout not changing:** Check ViewState in LocalStorage

---

## 🎉 Next Steps

After quick start:

1. **Explore Features:**
   - Try all 4 layouts
   - Test inline editing
   - Upload real datasets

2. **Run Tests:**
   - `dotnet test` in tests project
   - Follow integration test guide

3. **Customize:**
   - Adjust layouts
   - Add custom themes
   - Extend functionality

4. **Deploy:**
   - Build for production
   - Deploy to hosting service
   - Configure for scale

---

## 📞 Support

- **Issues:** Check console logs first
- **Bugs:** Report with steps to reproduce
- **Features:** Request with use case
- **Questions:** Check documentation first

---

**Happy dataset editing!** 🚀✨
