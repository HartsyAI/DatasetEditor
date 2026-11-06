# Dataset Studio — Design System

> **Purpose:** one visual language for the whole client, so every page is built from the same
> tokens and the same shared components. If a page needs something this document doesn't
> cover, add it here first, then build it. Drift starts the moment a page invents its own
> card, its own status colour, or its own `<style>` block.
>
> **Scope:** `src/ClientApp` (Blazor WebAssembly + MudBlazor 7). The API has no UI.
>
> **Status:** this is the *target*. The gap between it and today's code is itemised in
> [docs/refactor-audit.md](docs/refactor-audit.md) §3 (Client UI). Rules marked **(gap)** are
> not met yet.

---

## 1. Principles

1. **MudBlazor is the component kit; we don't re-implement it.** Use MudBlazor components and
   their `Variant`/`Color`/`Size` parameters. Custom markup is for the two things MudBlazor
   can't do well: the image card and the virtualised grid. We are on **MudBlazor 7.8** —
   use its parameter names (§1.1); a misspelled parameter is silently ignored, not an error.
2. **Tokens, not literals.** Colours, radii, spacing and shadows come from the theme (`MudTheme`
   → `--mud-palette-*`) or from the `--ds-*` custom properties in `app.css`. No hex codes, no
   `px` colours, no `rgba()` in `.razor` files. **(gap)**
3. **One component per concept.** A dataset card, a status chip, an empty state, a page header
   each exist exactly once under `Shared/Components` or `Features/*/Components` and are reused.
   Two pages rendering the same concept with different markup is a bug. **(gap)**
4. **Every data view has four states**: loading, empty, error, loaded. Each uses the shared
   component for that state. Partial states (buffering, refreshing) are inline and non-blocking.
5. **Dark is the default theme; light must be complete.** Anything that only looks right in
   dark mode is wrong. The only sanctioned fixed-dark surface is the scrim over an image
   (see §4.5).
6. **Accessible by construction.** Icon-only buttons carry `aria-label`, interactive cards are
   keyboard reachable, focus is visible, motion respects `prefers-reduced-motion`.
7. **Sentence case, plain language.** Labels and copy are short, sentence case, no trailing
   periods on labels, periods on full sentences. The vocabulary in §7 is fixed.

### 1.1 MudBlazor 7.8 facts (the build's MUD0001/MUD0002/RZ10012 warnings are all this)

| Don't write | Write | Why |
|---|---|---|
| `MudStepper`, `MudStep` | `MudTabs` or a hand-rolled stepper | Don't exist in 7.8; render as raw unknown HTML today (`CreateDataset.razor`). |
| `MudSegmentedControl` | `MudToggleGroup<T>` + `MudToggleItem` | Doesn't exist. |
| `MudSwitch`/`MudCheckBox` `Checked`/`CheckedChanged` | `Value`/`ValueChanged` or `@bind-Value` | v7 rename; the old names never fire (IndexedDB toggle, tag filters, settings are dead today). |
| `MudToggleIconButton` `CheckedIcon`/`UncheckedIcon`/`Checked` | `Icon`/`ToggledIcon`/`Toggled`/`ToggledChanged` | v7 rename (ThemeSelector renders empty). |
| `MudIconButton Title="…"` | wrap in `MudTooltip` **and** set `aria-label` | No `Title` parameter in v7; tooltip ≠ accessible name. |
| `MudGrid GutterSize` | `Spacing` | v7 rename. |
| `MudStack Direction="Direction.Row"` | `Row="true"` | v7 rename. |
| `MudExpansionPanel IsInitiallyExpanded` | `Expanded` | v7 rename. |
| `MudTextField Dense` | `Margin="Margin.Dense"` | v7 rename. |
| `MudSlider Label` | caption `MudText` above | No `Label` parameter. |
| `MudChip Closable` | `OnClose` alone | `OnClose` shows the icon. |
| `MudSelect … ValueExpression` | `@bind-Value` + `@bind-Value:after` | Not a MudSelect parameter. |
| `Class="h-100"` | `mud-height-full` | Bootstrap class, not Mud. |

---

## 2. Tokens

### 2.1 Where tokens live

| Layer | File | Role |
|---|---|---|
| Theme | `src/ClientApp/Shared/Theme/AppTheme.cs` **(gap: today inline in `MainLayout.razor.cs`)** | Single `MudTheme` instance: both palettes, typography, layout, shape. Nothing else constructs a `MudTheme`. |
| Custom properties | `src/ClientApp/wwwroot/css/app.css` | `--ds-*` tokens that MudBlazor doesn't own (radii, scrims, card shadows, z-index, transitions). |
| Component styles | `*.razor.css` (CSS isolation) | Component-scoped rules that reference tokens. **No `<style>` blocks inside `.razor` files** — a `<style>` in a component is emitted once per instance, so a 300-card grid emits 300 copies. **(gap: 8 components do this)** |

`wwwroot/css/themes/light.css` and `dark.css` are not loaded by `index.html` and define a
palette that disagrees with the theme; delete them. **(gap)**

### 2.2 Colour

Palette values (MudBlazor `PaletteLight` / `PaletteDark`). These are the current values,
kept, with the light-mode background corrected so surfaces separate from the page.

| Token | Light | Dark | Use |
|---|---|---|---|
| `Primary` | `#2563EB` | `#3B82F6` | The one accent. Primary buttons, active nav, focus ring, selection ring, links. |
| `Secondary` | `#64748B` | `#64748B` | Muted actions (cancel, tertiary), secondary chips. Never for emphasis. |
| `Success` | `#10B981` | `#10B981` | `Completed` status, confirmations. |
| `Warning` | `#F59E0B` | `#F59E0B` | Favourite star, "needs attention" status. |
| `Error` | `#EF4444` | `#EF4444` | `Failed` status, destructive actions, validation. |
| `Info` | `#06B6D4` | `#06B6D4` | `Processing` status, informational chips (item counts). |
| `Background` | `#F8FAFC` | `#0F172A` | Page background (slate-50 / slate-900). |
| `Surface` | `#FFFFFF` | `#1E293B` | Cards, papers, dialogs, toolbars (white / slate-800). |
| `BackgroundGray` | `#F1F5F9` | `#334155` | Image placeholders, skeleton bases (slate-100 / slate-700). Today left at Mud's neutral default, which clashes with the slate surfaces. |
| `DrawerBackground` | `#F9FAFB` | `#0F172A` | Navigation drawer (today dark uses Tailwind *gray* `#111827`, mixing two neutral families). |
| `AppbarBackground` | `#FFFFFF` | `#1E293B` | Reserved; there is no app bar today. |
| `TextPrimary` / `TextSecondary` | Mud defaults | Mud defaults | Use `Class="mud-text-secondary"` for secondary text, never `Color="Color.Secondary"` on text and never a custom grey. |

Rules:

- **One accent.** `Color.Primary` is the only emphasis colour. `Color.Secondary` is for
  de-emphasis. Don't use `Color.Tertiary`, `Color.Dark`, `Color.Surface` on text or icons;
  if a white icon is needed over an image scrim use the `.ds-on-scrim` class (§4.5).
- **Status colours are a function, not a choice.** `StatusChip` maps
  `IngestionStatusDto` → colour once (§5.3). Pages never pick a status colour themselves.
- **`body` has no hard-coded background.** `MudThemeProvider` sets it. **(gap: `app.css:15`
  forces `#020617`, which is neither palette's background.)**

### 2.3 Typography

Font: Roboto 300/400/500/700 (Google Fonts, loaded in `index.html`). Fallback Helvetica, Arial.

| Role | `Typo` | Where |
|---|---|---|
| Page title | `h4` | Exactly one per page, inside `PageHeader`. |
| Section title | `h6` | Toolbar titles (dataset name in the viewer), section papers. |
| Card title | `subtitle1` | `DatasetCard`, dashboard cards, settings panels. |
| Eyebrow | `overline` + `mud-text-secondary` | Above a page title or stat group. |
| Body | `body1` | Prose paragraphs, dialog messages. |
| Secondary body | `body2` + `mud-text-secondary` | Descriptions, helper text. |
| Meta | `caption` + `mud-text-secondary` | Counts, timestamps, "Updated 3 min ago". |
| Card overlay title | `body2` weight 600 in `.ds-on-scrim` | Image card only. |

Don't use `h5` for page titles (login/register do today) and don't use `h6` for card titles
(`DatasetLibrary` does today). **(gap)**

### 2.4 Spacing and layout

MudBlazor's 4 px scale (`pa-1` = 4 px … `pa-6` = 24 px). Use the utility classes; never
`style="padding: …"`.

| Token | Value | Use |
|---|---|---|
| Page gutter | `px-4 py-4` (16 px) | `MudContainer` on every page. |
| Page max width | `MaxWidth.Large` | All pages except the viewer, which is `MaxWidth.False` + `pa-0` (full bleed). |
| Section gap | `Spacing="3"` / `mb-4` (12–16 px) | Between papers and groups. |
| Card padding | `pa-4` | Every card/paper body. |
| Toolbar padding | `pa-3` | Toolbars and status bands. |
| Grid gap | `MudGrid Spacing="3"` / `--ds-grid-gap: 16px` (8 px below 600 px) | Dataset grid and image grid. |
| Drawer width | 240 px (Mud default) | Persistent ≥ `md`, temporary below. |

### 2.5 Shape, elevation, motion

| Token | Value | Use |
|---|---|---|
| `--ds-radius-sm` | 4 px | Inputs, chips, list thumbnails (Mud default `BorderRadius`). |
| `--ds-radius-md` | 8 px | Papers, dataset cards, dialogs. |
| `--ds-radius-lg` | 12 px | Image cards. |
| Elevation 0 | — | Flat panels sitting on the page background (empty state, filter panel, hero paper). |
| Elevation 1 | — | Cards, toolbars, settings panels. |
| Elevation 2 | — | Drawer, dialogs, dashboard feature cards, hover lift. |
| `--ds-shadow-card` | `0 1px 3px rgb(0 0 0 / .12)` | Image card at rest. |
| `--ds-shadow-card-hover` | `0 8px 16px rgb(0 0 0 / .16)` | Image card hover. |
| `--ds-transition` | `200ms cubic-bezier(.4,0,.2,1)` | All hover/selection transitions. |
| Hover lift | `translateY(-4px)` | Dataset cards and image cards only. |

Every animation is disabled under `prefers-reduced-motion: reduce` (already in `app.css`).

### 2.6 Iconography

Material Icons via `MudBlazor.Icons.Material` (inline SVG). **Filled** is the default set.
**Outlined** is used only for the "off" half of a toggle (`StarBorder` vs `Star`). Don't mix
within one control group. **(gap: `ImageCard` hover actions use Outlined, the rest of the app
Filled.)** The Material Icons *webfont* link in `index.html` is unused and should be removed;
string icon names like `"mdi-view-grid"` (`LayoutSwitcher`) are not valid here.

Fixed icon vocabulary (so the same concept always has the same glyph):

| Concept | Icon |
|---|---|
| Dashboard | `Dashboard` |
| Datasets / library | `Collections` |
| Create / import | `AddBox` |
| Upload (file) | `CloudUpload` |
| HuggingFace | `Cloud` |
| Local folder | `Folder` |
| S3 bucket | `Storage` |
| AI tools | `Bolt` |
| Settings | `Settings` |
| Open / view | `Visibility` |
| Delete | `Delete` |
| Edit | `Edit` |
| Favourite | `Star` / `StarBorder` |
| Filter | `FilterList` |
| Grid / list / gallery | `GridView` / `ViewList` / `ViewModule` |
| Details panel | `Info` |
| Refresh | `Refresh` |
| Status: pending / processing / completed / failed | `Schedule` / `Sync` / `CheckCircle` / `Error` |

---

## 3. Page anatomy

Every routed page follows this skeleton. The viewer is the one full-bleed exception.

```
<PageTitle>{Title} - Dataset Studio</PageTitle>
<MudContainer MaxWidth="MaxWidth.Large" Class="px-4 py-4">
    <PageHeader Title="…" Subtitle="…">          ← h4 + optional body2, right-aligned actions
        <Actions>…primary action…</Actions>
    </PageHeader>
    <Toolbar>…search / filters…</Toolbar>       ← optional, Elevation 1, pa-3
    @if loading   → <LoadingState />
    else if error → <ErrorState Message=… OnRetry=… />
    else if empty → <EmptyState Headline=… Description=…><ActionContent>…</ActionContent></EmptyState>
    else          → content
</MudContainer>
```

Rules:

- Title text, nav label and route agree (§7). Only one `h4` per page, rendered as an `<h1>`
  element (`MudText Typo="Typo.h4" HtmlTag="h1"`) so `FocusOnNavigate Selector="h1"` in
  `App.razor` actually moves focus on navigation — today no page renders an `h1`.
- The primary action for the page (e.g. "New dataset") lives in `PageHeader.Actions` as a
  `Filled Primary` button. It does not also appear inside the toolbar or the empty state in
  a different style — the empty state may repeat it with the same style.
- Loading uses a skeleton that resembles the content (grid of card skeletons for grids, a
  `MudProgressLinear` for a refresh of already-shown content). A centred spinner is only for
  the first paint of the viewer.
- Error display: inline `MudAlert Severity.Error` with a retry action for recoverable
  failures; `Snackbar` for transient failures of an action the user just took; the
  `ErrorBoundary` component for render crashes. Never both an alert and a snackbar for the
  same failure.
- Nothing is placed with `Style="height: 80vh"` or similar; use layout classes (`ds-fill`,
  `ds-center`). **(gap: viewer loading/empty states)**

### 3.1 The viewer (full bleed)

```
┌────────────────────────────────────────────────┬──────────────┐
│ StatusBand   (ingestion status, last updated)  │              │
│ ViewerToolbar (filter toggle · name · counts · │ Detail panel │
│                view mode · details · cache)    │ (md=3)       │
│ FilterPanel  (collapsible)                     │              │
│ ViewerContainer → VirtualizedItemView          │              │
└────────────────────────────────────────────────┴──────────────┘
```

Three columns at `md` and up (9/3 with the detail panel, 12 without). Below `md` the detail
panel becomes a bottom sheet/dialog, not a stacked column. **(gap)**

---

## 4. Component library

Location: `Shared/Components` for cross-feature components, `Features/<Feature>/Components`
for feature-specific ones. Each component has a `.razor`, a `.razor.cs` code-behind when it
has logic, and a `.razor.css` when it has styles. Parameters are documented with `///`.

### 4.1 Status

| Component | Status | Notes |
|---|---|---|
| `PageHeader` | **missing** | Title (`h4`), optional subtitle (`body2` secondary), `Actions` fragment. Replaces hand-rolled `MudText Typo.h4 Class="mb-4"` on every page. |
| `Toolbar` | **missing** | `MudPaper Elevation=1 Class="pa-3"` with a row stack; left/right fragments. Used by library search bar and viewer toolbar. |
| `StatusChip` | **missing** | `IngestionStatusDto` → chip with fixed colour + icon (§5.3). Replaces `GetStatusColor` in `DatasetLibrary` and `GetStatusSeverity` in `DatasetViewer`. |
| `SourceBadge` | **missing** | `DatasetSourceType` → label + icon (§5.4). Replaces `GetSourceLabel`. |
| `DatasetCard` | **missing (extract)** | The card in `DatasetLibrary.razor:75-133`, with `StatusChip`, `SourceBadge`, Open/Delete actions, "Updated … ago". |
| `EmptyState` | exists (`Shared/Components/EmptyState.razor`) | Use it. `DatasetLibrary` and `DatasetViewer` hand-roll their own instead. **(gap)** |
| `LoadingState` | exists as `LoadingIndicator` | Rename to `LoadingState`; add `Variant` = `Spinner | CardGrid | Rows`. |
| `ErrorState` | **missing** | Inline alert + retry. `ErrorBoundary` stays for render crashes. |
| `ConfirmDialog` | exists, **unused** | Use for every destructive action (dataset delete currently has no confirmation at all). Confirm button is `Filled Error` for deletes (today `Filled Primary`). |
| `IngestionProgressPanel` | **missing (extract)** | The upload/import progress block duplicated four times in `DatasetUploader.razor`. |
| `SourceOptionCard` | **missing (extract)** | The four source cards on `CreateDataset.razor`. |
| `TagChipList` | **missing (extract)** | Tags are rendered four different ways today (detail panel, lightbox, list row, add-tag dialog). |
| `InlineEditableText` | **missing (extract)** | Inline title edit is implemented twice (`ImageCard`, `ImageDetailPanel`). |
| `StatCard` | **missing (extract)** | The "Current session" numbers on the dashboard. |
| `SectionCard` | **missing (extract)** | `MudPaper Elevation=1 pa-4` with a `subtitle1` title; dashboard and settings panels. |
| `ImageCard` | exists | Grid item. Move `<style>` to `ImageCard.razor.css`; icons to Filled; add keyboard support. |
| `ImageListRow` | exists | List item. Same fixes; wire the Download/More buttons or remove them (today they do nothing). |
| `ImageDetailPanel` | exists | Right-hand detail/edit panel. |
| `ImageLightbox` | exists | Full-screen view. |
| `VirtualizedItemView` / `ViewerContainer` | exist | Row-virtualised grid/list. |
| `FilterPanel`, `DateRangeFilter` | exist | Filter inputs. Tag checkboxes use v6 names and never fire (§1.1). |
| `SearchBar`, `FilterChips`, `DatasetInfo`, `DatasetStats`, `LanguageSelector` | exist, **unused** | `SearchBar` duplicates the field inside `FilterPanel`; `DatasetInfo`/`DatasetStats` are skeletons with TODO text. Delete or adopt. |
| `DatasetUploader` + `HuggingFaceDatasetOptions` | exist | 1,500 lines across four tabs; split into one component per source (§6). |
| `DatasetSwitcher`, `LayoutSwitcher` | exist, **unused** | Delete or mount them. `LayoutSwitcher` depends on `Core.LayoutRegistry`, which nothing else uses. |
| `ThemeSelector`, `ViewPreferences`, `LanguageSelector`, `ApiKeySettingsPanel` | exist | Settings panels. Only `ApiKeySettingsPanel` is wired; the others are no-ops. **(gap)** |

### 4.2 Buttons

| Role | Markup | Count per view |
|---|---|---|
| Primary action | `Variant.Filled` `Color.Primary` | One. |
| Secondary action | `Variant.Outlined` `Color.Primary` | Any. |
| Tertiary / inline | `Variant.Text` `Color.Primary` | Any (card actions, "Open"). |
| Destructive inline | `Variant.Text` `Color.Error` | Card "Delete". |
| Destructive confirm | `Variant.Filled` `Color.Error` | Dialog confirm only. |
| Cancel / dismiss | `Variant.Text` `Color.Secondary` | Dialogs, alerts. |
| Icon button | `MudTooltip` wrapping `MudIconButton` + `aria-label` | Toolbars, cards. (`Title=` is not a v7 parameter; the 11 current uses are ignored.) |

`FullWidth="true"` only in auth forms and empty-state CTAs. A `MudButton` never carries a
hard-coded `Class="mt-3"` to make room for itself — the parent stack spaces children.

### 4.3 Forms

- `MudTextField Variant.Outlined`, `Immediate="true"` for fields that gate a button.
- Labels, not placeholders, carry the field name. Helper text uses `HelperText`.
- Validation: `MudForm` with `Validation` on fields, errors shown inline; a page-level
  `MudAlert Severity.Error` only for server errors.
- Submit on Enter: use `MudForm`'s submit, not an `@onkeyup` lambda on the last field. **(gap)**
- Submitting state: button `Disabled` + label change ("Creating account…").

### 4.4 Chips

- Status: `StatusChip` only.
- Metadata (format, modality, counts): `Size.Small` `Color.Default` `Variant.Outlined`.
- Counts in toolbars: `Size.Small` `Color.Info`.
- "Coming soon" / beta: `Size.Small` `Color.Default` `Variant.Text`.

### 4.5 Image scrims (`.ds-on-scrim`)

The only fixed-colour surface: the gradient over an image card's lower edge and the hover
overlay. Both are `--ds-scrim` (`rgb(0 0 0 / .6)` → `.8`) with white text and icons via
`.ds-on-scrim { color: #fff }`. This is theme-independent by design because it sits over an
unknown image, not over a themed surface.

### 4.6 Dialogs

`MudDialog` through `IDialogService`. Title `h5`, message `body1` secondary, actions right-
aligned, cancel left of confirm. Width `MaxWidth.Small` for confirmations, `Medium` for forms.
Pass results via `DialogResult`, not callbacks and results both.

### 4.7 Notifications

`NotificationService` wraps `ISnackbar` and is the **only** way to show a snackbar
(`DatasetLibrary` and `ImageDetailPanel` inject `ISnackbar` directly today). Severity mirrors
MudBlazor: success for completed actions the user initiated, error for failed ones, info for
background progress ("Import started"). Snackbars are not used for validation. A failure is
reported once: either an inline alert or a snackbar, never both (the viewer does both today).

---

## 5. Shared semantics

### 5.1 Four states

| State | Component | Trigger |
|---|---|---|
| Loading (first) | `LoadingState Variant=CardGrid` | No data yet. |
| Refreshing | `MudProgressLinear Indeterminate` above content | Data shown, new request in flight. |
| Empty | `EmptyState` | Request done, zero items. Copy says why (no datasets / no matches / no images in this dataset). |
| Error | `ErrorState` | Request failed. Always has retry. |

### 5.2 Selection

Selected cards get a 3 px `Primary` ring (`--ds-selection-ring`), never a different
background. Selection count is a `Primary` chip in the toolbar. Bulk actions appear in the
toolbar only while a selection exists.

### 5.3 Ingestion status → presentation

| `IngestionStatusDto` | Colour | Icon | Label |
|---|---|---|---|
| `Pending` | `Default` | `Schedule` | Pending |
| `Processing` | `Info` | `Sync` | Processing |
| `Completed` | `Success` | `CheckCircle` | Ready |
| `Failed` | `Error` | `Error` | Failed |

Alerts for the same statuses use `Severity.Normal / Info / Success / Error`.

### 5.4 Source → presentation

| `DatasetSourceType` | Label | Icon |
|---|---|---|
| `LocalUpload` | Uploaded file | `CloudUpload` |
| `LocalFolder` | Server folder | `Folder` |
| `HuggingFaceStreaming` | HuggingFace (streaming) | `Cloud` |
| `HuggingFace` / `HuggingFaceDownload` | HuggingFace (imported) | `Cloud` |
| `ExternalS3Streaming` | S3 bucket | `Storage` |
| `WebUrl` | URL | `Link` |

### 5.5 Relative time

"Updated just now / 3 min ago / 2 h ago / yesterday / 12 Nov 2026" via one helper
(`RelativeTime.Format`). Absolute timestamps use `ToLocalTime().ToString("g")` in a tooltip.

---

## 6. Composition rules (keeping files small)

- A page file (`*.razor`) is markup and routing only; logic lives in `*.razor.cs`. Target
  ≤ 200 lines of markup, ≤ 300 lines of code-behind.
- A component does one thing. `DatasetUploader` (419 + 1,117 lines) becomes
  `UploadSourceForm`, `HuggingFaceSourceForm`, `FolderSourceForm`, `S3SourceForm` plus a thin
  `CreateDataset` page that picks one. Shared upload progress/ingestion polling becomes
  `IngestionProgress`.
- State flows down through parameters; events flow up through `EventCallback`. Components
  don't reach into `DatasetState` unless they are the page or a top-level panel.
- A component that subscribes to a state `OnChange` event implements `IDisposable` and
  unsubscribes.
- No raw `HttpClient` in components; go through `DatasetApiClient` / `AuthApiClient`.
  **(gap: `DatasetSwitcher` uses raw `HttpClient`.)**
- `@key` on every item in a rendered collection.

---

## 7. Vocabulary and copy

| Concept | Word | Not |
|---|---|---|
| The collection of a user's datasets | **Datasets** (nav), page title "My Datasets" / "Public Datasets" | Library, Saved Datasets |
| Adding a dataset | **New dataset** (button), page "Create dataset" | Upload New Dataset, Open Creator, Use Upload |
| One row of a dataset | **Item** | Image (unless specifically an image), Entry |
| Ingestion finished | **Ready** (chip) / "Completed" (API enum) | Done |
| HuggingFace | **HuggingFace** | Hugging Face, HF (UI) |
| Product name | **Dataset Studio** | DatasetStudio, Hartsy's Dataset Editor |

Routes: `/`, `/datasets` (**gap:** today `/my-datasets`; three links point at a non-existent
`/upload`), `/datasets/create`, `/datasets/{id}` (**gap:** today `/dataset-viewer?id=`),
`/settings`, `/login`, `/register`, `/ai-tools`.

Copy rules: sentence case; buttons are verbs ("Open", "Delete", "New dataset"); no "TODO"
text in shipped UI (**gap:** `Settings.razor` shows two TODO sentences to users); "Coming
soon" features are labelled in nav and on the page, never presented as working.

---

## 8. Accessibility and responsiveness

- `index.html` viewport must allow zoom (`user-scalable=no` removed). **(gap)**
- Every `MudIconButton` has `aria-label` (MudBlazor renders `Title` as tooltip, not as an
  accessible name).
- Interactive non-button elements (`ImageCard`, `ImageListRow`) get `role="button"`,
  `tabindex="0"`, and handle Enter/Space. **(gap)**
- Lightbox traps focus and closes on Escape.
- Contrast ≥ 4.5:1 for text on both palettes; secondary text uses Mud's secondary token,
  not a custom grey.
- Breakpoints: `xs` < 600, `sm` ≥ 600, `md` ≥ 960, `lg` ≥ 1280. Dataset grid: 1 / 2 / 3
  columns. Image grid columns come from container width (`VirtualizedItemView`), min card
  width 180 px. Drawer: persistent at `md`+, temporary (overlay) below. **(gap)**
- Touch targets ≥ 40 px.

---

## 9. Checklist for a new page or component

- [ ] Uses `PageHeader`, `Toolbar`, `EmptyState`, `LoadingState`, `ErrorState` rather than local markup.
- [ ] No `<style>` block; styles in `.razor.css`; no hex/rgba/px colours; no inline `Style=` except dynamic values.
- [ ] Only `Typo` roles from §2.3; one `h4`.
- [ ] Button roles from §4.2; one `Filled Primary`.
- [ ] Status and source rendering via `StatusChip` / `SourceBadge`.
- [ ] All four data states handled.
- [ ] Icon buttons have `aria-label`; interactive cards are keyboard reachable.
- [ ] Looks right in light **and** dark.
- [ ] Copy uses §7 vocabulary; no TODO text.
- [ ] Logic in `.razor.cs`; state subscriptions disposed; `@key` on lists.
