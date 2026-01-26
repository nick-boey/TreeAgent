# Implementation Plan: Consolidate UI with Tailwind CSS

## Status: Phase 1-3 Completed

This plan details the migration from the current custom CSS system to Tailwind CSS for the Homespun Blazor Server application. The approach preserves existing design tokens (colors, fonts, spacing) while enabling utility-first styling and extracting reusable components from pages with inline styles.

---

## Completed Work

### Phase 1: Tailwind Foundation Setup ✅

**Files Created:**
- `/src/Homespun/package.json` - npm configuration with Tailwind
- `/src/Homespun/tailwind.config.js` - Custom theme mapping all existing design tokens
- `/src/Homespun/wwwroot/css/tailwind.css` - Input file with base/components/utilities

**Files Modified:**
- `/src/Homespun/Homespun.csproj` - Added MSBuild target for Tailwind CSS compilation
- `/src/Homespun/Components/App.razor` - Added Tailwind CSS stylesheet link
- `/.gitignore` - Added `wwwroot/css/tailwind.min.css`

### Phase 2: Bootstrap Utility Replacement ✅

**17 files updated** to replace Bootstrap utility classes with Tailwind equivalents:
- `d-flex` → `flex`
- `flex-column` → `flex-col`
- `justify-content-between` → `justify-between`
- `align-items-center` → `items-center`
- `ms-1/2` → `ml-1/2`
- `me-1/2` → `mr-1/2`
- `text-muted` → `text-text-muted`
- `bg-success/warning/danger/info` → `bg-status-*`
- `row`/`col-*` → `grid grid-cols-12`/`col-span-*`
- `small` → `text-sm`
- `visually-hidden` → `sr-only`

### Phase 3: Component Extraction - Chat UI ✅

**New Components Created** in `/Components/Shared/Chat/`:
| Component | Purpose |
|-----------|---------|
| `ChatMessage.razor` | Message container with role styling |
| `ContentBlock.razor` | Text/thinking/tool content wrapper |
| `TextBlock.razor` | Plain text content with streaming cursor |
| `ThinkingBlock.razor` | Collapsible thinking section |
| `ToolUseBlock.razor` | Tool execution display |
| `ToolResultBlock.razor` | Tool result display |
| `ChatInput.razor` | Textarea + send button |
| `ProcessingIndicator.razor` | Loading/streaming spinner |
| `ChatMessage.razor.css` | Shared styles for chat components |

**Other Components:**
- `InfoList.razor` + `InfoList.razor.css` - Reusable definition list component

**Files Updated:**
- `/Components/_Imports.razor` - Added `@using Homespun.Components.Shared.Chat`
- `Session.razor` - Updated badge classes to use Tailwind color tokens

---

## Remaining Work (Future Phases)

### Phase 4: Component Extraction - Project UI (ProjectDetail.razor)

**Goal**: Extract ~200 lines of inline CSS into reusable components

| Component | Purpose |
|-----------|---------|
| `ProjectInfoCard.razor` | Project details card |
| `TimelineSectionLabel.razor` | Section header with graph line |
| `TimelineActionButton.razor` | Load more / add item buttons |

### Phase 5: Migrate Component CSS Files

| File | Action |
|------|--------|
| `NavMenu.razor.css` | Convert to Tailwind classes |
| `NotificationBanner.razor.css` | Convert to Tailwind classes |
| `ModelSelector.razor.css` | Convert to Tailwind classes |
| `WorkItem.razor.css` | Partial convert (keep animations) |
| `MainLayout.razor.css` | **Keep** (complex grid layout) |
| `ReconnectModal.razor.css` | **Keep** (complex animations) |

### Phase 6: Final Cleanup

1. Extract `ActionsCell.razor` from `Home.razor`
2. Remove inline `<style>` blocks from all pages
3. Audit `app.css`:
   - **Keep**: CSS custom properties, base styles, typography, scrollbar, animations
   - **Remove**: Redundant utility classes, migrated component classes
4. Remove unused `/wwwroot/lib/bootstrap/` directory
5. Final visual regression testing

---

## Current Directory Structure

```
/Components/Shared/
├── Chat/
│   ├── ChatMessage.razor
│   ├── ChatMessage.razor.css
│   ├── ChatInput.razor
│   ├── ContentBlock.razor
│   ├── TextBlock.razor
│   ├── ThinkingBlock.razor
│   ├── ToolUseBlock.razor
│   ├── ToolResultBlock.razor
│   └── ProcessingIndicator.razor
├── InfoList.razor
├── InfoList.razor.css
└── [existing components...]
```

---

## Tailwind Configuration Summary

The `tailwind.config.js` maps all existing design tokens:

**Colors:**
- Brand: dark-background, basalt, basalt-light, red, outback, sand, wattle, lagoon, ocean, gum
- Semantic: bg-primary/secondary/tertiary, text-primary/secondary/muted
- Status: status-success, status-warning, status-error, status-info

**Typography:**
- Font: Figtree with weights normal/medium/semibold

**Spacing:**
- xs (0.125rem), sm (0.25rem), md (0.5rem), lg (0.75rem), xl (1rem)

**Component Classes** (via @layer components):
- `.btn`, `.btn-primary`, `.btn-secondary`, `.btn-danger`, `.btn-outline`
- `.form-control`, `.form-select`, `.form-label`
- `.card`, `.card-header`, `.card-body`, `.card-footer`
- `.table`, `.badge-*`, `.alert-*`
- `.spinner-border`

---

## Verification Status

- [x] `dotnet build` succeeds (0 errors)
- [x] Tailwind CSS compiles during build
- [x] npm install works
- [ ] Visual regression testing (manual verification recommended)
- [ ] Theme toggle verification
- [ ] Docker build verification
