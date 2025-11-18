# Integration Test Guide

This document provides step-by-step instructions for manually testing Phases 3 and 4.

## Prerequisites

1. Start the API server:
   ```powershell
   cd src/HartsysDatasetEditor.Api
   dotnet watch run
   ```

2. Start the Client application:
   ```powershell
   cd src/HartsysDatasetEditor.Client
   dotnet watch run
   ```

3. Verify both are running:
   - API: https://localhost:7085
   - Client: https://localhost:7221

---

## Phase 3: Multi-File Dataset Support - Integration Tests

### Test 1: Upload Single CSV File

**Objective:** Verify basic single-file upload still works

**Steps:**
1. Navigate to Upload page
2. Select a single CSV file (e.g., `photos.csv`)
3. Click Upload

**Expected Result:**
- File uploads successfully
- Dataset loads in viewer
- All images display correctly

---

### Test 2: Upload Multiple CSV Files (Primary + Enrichment)

**Objective:** Test multi-file detection and enrichment merging

**Test Data Preparation:**

Create `photos.csv`:
```csv
photo_id,photo_image_url,photo_description,photographer_name
1,https://images.unsplash.com/photo-1,Mountain landscape,John Doe
2,https://images.unsplash.com/photo-2,Ocean sunset,Jane Smith
3,https://images.unsplash.com/photo-3,Forest path,Bob Johnson
```

Create `colors.csv`:
```csv
photo_id,hex,red,green,blue,keyword
1,#8B4513,139,69,19,brown
2,#FF7F50,255,127,80,coral
3,#228B22,34,139,34,green
```

Create `tags.csv`:
```csv
photo_id,tag
1,nature
1,mountain
1,landscape
2,ocean
2,sunset
2,beach
3,forest
3,trees
3,nature
```

**Steps:**
1. Navigate to Upload page
2. Click "Select Files or ZIP"
3. Select all three files: `photos.csv`, `colors.csv`, `tags.csv`
4. Verify auto-detection results show:
   - Primary File: photos.csv
   - Enrichment Files:
     - colors.csv (colors - 3 records)
     - tags.csv (tags - 9 records)
5. Click Upload

**Expected Results:**
- ✅ All files detected correctly
- ✅ Dataset uploads successfully
- ✅ Images display with enriched data:
  - Photo 1: Has brown color swatch, tags: nature, mountain, landscape
  - Photo 2: Has coral color swatch, tags: ocean, sunset, beach
  - Photo 3: Has green color swatch, tags: forest, trees, nature
- ✅ Color chips appear in hover overlay
- ✅ Tags display correctly on image cards

---

### Test 3: ZIP File Upload

**Objective:** Test ZIP file extraction and processing

**Test Data Preparation:**
1. Create a ZIP file containing the three CSV files from Test 2
2. Name it: `unsplash-dataset.zip`

**Steps:**
1. Navigate to Upload page
2. Select `unsplash-dataset.zip`
3. Click Upload

**Expected Results:**
- ✅ ZIP extracts successfully
- ✅ Files detected same as Test 2
- ✅ Enrichment data merges correctly

---

### Test 4: Enrichment Type Detection

**Objective:** Verify correct detection of enrichment types

**Test Cases:**

| File Name | Expected Type | Key Column Detection |
|-----------|---------------|---------------------|
| `colors.csv000` | colors | photo_id |
| `tags.csv000` | tags | photo_id |
| `collections.csv000` | collections | photo_id |
| `metadata.csv000` | metadata | photo_id or first column |

**Steps:**
1. Create files with different naming patterns
2. Upload together with primary file
3. Check auto-detection results

**Expected Results:**
- ✅ Correct enrichment type detected for each file
- ✅ Foreign key column identified correctly
- ✅ Appropriate merge strategy applied

---

### Test 5: Large Dataset with Multiple Enrichments

**Objective:** Test performance with realistic data volume

**Test Data:**
- Primary: 1,000 photos
- Colors: 1,000 records
- Tags: 5,000 records (multiple tags per photo)
- Collections: 500 records

**Steps:**
1. Upload all files simultaneously
2. Monitor console for performance logs
3. Verify data integrity after merge

**Expected Results:**
- ✅ Upload completes in reasonable time (< 30 seconds)
- ✅ All enrichments applied successfully
- ✅ No data loss or corruption
- ✅ UI remains responsive

---

## Phase 4: Inline Editing with Persistence - Integration Tests

### Test 1: Edit Image Title

**Steps:**
1. Load a dataset
2. Click on an image to open detail panel
3. Click edit icon next to title
4. Change title to "New Test Title"
5. Press Enter or click outside field

**Expected Results:**
- ✅ Title field becomes editable
- ✅ On save, success notification appears
- ✅ Title updates in detail panel
- ✅ Title updates in grid card
- ✅ API receives PATCH request
- ✅ Database persists change

**Verification:**
```powershell
# Check database
sqlite3 ./data/hartsy.db "SELECT Id, Title FROM Items WHERE Title LIKE '%New Test Title%';"
```

---

### Test 2: Edit Description

**Steps:**
1. Open image detail panel
2. Click edit icon next to description
3. Enter multi-line description
4. Click outside textarea

**Expected Results:**
- ✅ Description becomes editable
- ✅ Multi-line input works correctly
- ✅ Success notification appears
- ✅ Description persists to database

---

### Test 3: Add Tags

**Steps:**
1. Open image detail panel
2. Click "+" button next to Tags
3. Type "test-tag" in dialog
4. Click Add

**Expected Results:**
- ✅ Add Tag dialog opens
- ✅ Suggested tags appear (if any exist)
- ✅ New tag adds successfully
- ✅ Tag appears as chip
- ✅ Tag persists to database

---

### Test 4: Remove Tags

**Steps:**
1. Open image with existing tags
2. Click X on a tag chip

**Expected Results:**
- ✅ Confirmation or immediate removal
- ✅ Tag disappears from UI
- ✅ Success notification
- ✅ Database updated

---

### Test 5: Toggle Favorite

**Steps:**
1. Hover over image card
2. Click star icon in top-left corner
3. Check detail panel

**Expected Results:**
- ✅ Star icon toggles filled/outline
- ✅ Favorite status updates immediately
- ✅ API request sent
- ✅ Database updated

**Verification:**
```powershell
# Check favorites in database
sqlite3 ./data/hartsy.db "SELECT Id, Title, IsFavorite FROM Items WHERE IsFavorite = 1;"
```

---

### Test 6: Bulk Edit - Add Tags to Multiple Items

**Steps:**
1. Load dataset
2. Select multiple images (checkbox)
3. (Future: Use bulk edit toolbar)
4. Add tag "bulk-test" to all selected

**Expected Results:**
- ✅ All selected items receive new tag
- ✅ Bulk update completes quickly
- ✅ Success notification shows count
- ✅ Database updated for all items

---

### Test 7: Bulk Edit - Set Favorite

**Steps:**
1. Select 5 images
2. Set all as favorite

**Expected Results:**
- ✅ All items marked as favorite
- ✅ Star icons update on all cards
- ✅ Database updated

---

### Test 8: Dirty State Tracking

**Steps:**
1. Edit an image title but don't save (future feature)
2. Check `ItemEditService.DirtyItemIds`

**Expected Results:**
- ✅ Item ID added to dirty set
- ✅ Visual indicator shows unsaved changes
- ✅ Prompt before navigating away

---

### Test 9: Edit with Network Failure

**Steps:**
1. Stop API server
2. Try to edit image title
3. Save changes

**Expected Results:**
- ✅ Error notification appears
- ✅ Changes remain in UI (not lost)
- ✅ Retry option available

---

### Test 10: Concurrent Edits

**Steps:**
1. Open same dataset in two browser tabs
2. Edit different fields in each tab
3. Save in sequence

**Expected Results:**
- ✅ Both edits persist
- ✅ No data loss
- ✅ UpdatedAt timestamp shows latest edit

---

## Performance Tests

### Test 1: Upload Performance

**Objective:** Measure upload and merge performance

**Test Matrix:**

| File Size | Records | Enrichments | Expected Time |
|-----------|---------|-------------|---------------|
| 1 MB | 1,000 | 0 | < 5 seconds |
| 10 MB | 10,000 | 2 | < 15 seconds |
| 50 MB | 50,000 | 3 | < 45 seconds |

**Metrics to Track:**
- File read time
- Detection time
- Parse time
- Merge time
- Database insert time

---

### Test 2: Edit Performance

**Objective:** Measure edit response time

**Test Cases:**
- Single field update: < 500ms
- Bulk update (10 items): < 2 seconds
- Bulk update (100 items): < 10 seconds

---

## Error Handling Tests

### Test 1: Invalid File Format

**Steps:**
1. Upload `.txt` file without CSV structure
2. Upload `.xlsx` file

**Expected Results:**
- ✅ Error message displayed
- ✅ Upload rejected gracefully

---

### Test 2: Missing Foreign Key

**Steps:**
1. Upload enrichment file with wrong ID column

**Expected Results:**
- ✅ Enrichment skipped or error logged
- ✅ Primary dataset still loads

---

### Test 3: Duplicate IDs

**Steps:**
1. Upload files with duplicate IDs

**Expected Results:**
- ✅ Handled gracefully
- ✅ Last record wins or error reported

---

## Browser Compatibility

Test all features in:
- ✅ Chrome (latest)
- ✅ Firefox (latest)
- ✅ Edge (latest)
- ✅ Safari (if available)

---

## Accessibility Tests

1. **Keyboard Navigation:**
   - Tab through forms
   - Enter to save
   - Escape to cancel

2. **Screen Reader:**
   - Verify labels read correctly
   - Error messages announced

3. **Contrast:**
   - Check text readability
   - Verify color contrast ratios

---

## Test Data Cleanup

After testing:
```powershell
# Delete test database
Remove-Item ./data/hartsy.db

# Clear uploads
Remove-Item ./uploads/* -Recurse

# Restart services to recreate fresh database
```

---

## Automated Test Execution

Run all unit tests:
```powershell
cd tests/HartsysDatasetEditor.Tests
dotnet test
```

Expected output:
```
Passed! - Failed: 0, Passed: 50+, Skipped: 0
```

---

## Known Issues / Limitations

1. **Phase 3:**
   - ZIP files not yet implemented (manual extraction required)
   - Large files (>100MB) rejected
   - Only CSV/TSV formats supported

2. **Phase 4:**
   - Delete functionality not yet implemented
   - Download functionality not yet implemented
   - Bulk edit toolbar UI pending
   - Undo/redo not available

---

## Success Criteria

✅ **Phase 3 Complete:**
- All enrichment types detected correctly
- Color, tag, and collection data merges successfully
- Performance acceptable for datasets up to 50K records

✅ **Phase 4 Complete:**
- All CRUD operations work
- Edits persist to database
- UI updates in real-time
- No data loss on errors

---

## Reporting Issues

When reporting integration test failures, include:
1. Test number and name
2. Steps to reproduce
3. Expected vs actual result
4. Console logs (browser and server)
5. Database state (if relevant)
6. Screenshots

---

## Next Steps

After successful integration testing:
1. Address any found issues
2. Update documentation
3. Proceed to Phase 5 (future phases)
4. Deploy to staging environment
