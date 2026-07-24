**Design QA**


**Full-View Comparison Evidence**

The reference and implementation were compared together in `theme-comparison.png`. The optional Dark color scheme carries over the requested palette rather than the reference page composition: near-black page surfaces, neutral charcoal controls, thin gray borders, off-white text, and amber primary actions and selection indicators. Existing semantic task colors remain visible. The original Light color scheme remains the application default.

**Focused Region Evidence**

A separate crop was not required because the native 1920 x 1200 capture clearly resolves the task list, form controls, action buttons, focus state, badges, and complete editor surface. The editor region received an additional focused review during the two correction passes below.

**Required Fidelity Surfaces**

- Fonts and typography: Existing compact system font and hierarchy are retained. Text remains legible and wrapping behavior is unchanged.
- Spacing and layout rhythm: Existing application density, split layout, field grid, and control dimensions are preserved.
- Colors and visual tokens: Neutral near-black surfaces and amber accents match the reference palette. Focus, selected, danger, waiting, and lookup badge states remain distinguishable.
- Image quality and asset fidelity: No image assets were required for this color-scheme adaptation. Existing application icons remain unchanged.
- Copy and content: Existing task application copy is unchanged.

**Findings**

No actionable P0, P1, or P2 visual differences remain for the requested color-scheme adaptation.

**Comparison History**

1. Initial native capture: P1, TinyMCE document canvas remained white. Fixed by applying the selected color scheme to the TinyMCE document canvas.
2. Second native capture: P2, TinyMCE used a blue-black tint inconsistent with the reference's neutral charcoal. Fixed with neutral toolbar, status bar, iframe, and editor-content colors.
3. Final native capture: in Dark mode, editor chrome and content use neutral charcoal/near-black surfaces with readable off-white controls and text.

**Follow-up Polish**

No blocking follow-up. Lookup-defined badge colors intentionally differ from amber so task classifications preserve their meaning.

final result: passed

---

# Design QA — lifecycle destination reveal, option 3

## Evidence

- Source visual truth:
  `C:\Users\soere\.codex\generated_images\019f64d1-7fc6-7900-afb4-ee49130ff5eb\call_jvwEauJzqVzmN5fM9v8gx96f.png`
- Browser-rendered implementation:
  `artifacts/design-qa-lifecycle/lifecycle-destination-completed.png`
- Same-input comparison:
  `artifacts/design-qa-lifecycle/lifecycle-option3-comparison.png`
- Viewport: 1487 × 1058.
- State: a newly created task has just been completed; Completed is the active
  destination view and the changed task remains selected, revealed, focused,
  and open in task details.

## Full-view comparison evidence

The source and browser capture were placed together in the same comparison
image. Both show the same three-way confirmation: the Completed view is active,
the exact moved task is emphasized in the queue, and task details remain on
that task with a `COMPLETED · VIEWING IN COMPLETED` context badge and Reopen
action. The implementation preserves the product's real filters, sort controls,
editor, and lifecycle actions.

## Focused region comparison evidence

A separate crop was not required. At the native viewport, the view rail,
selected task treatment, “Just moved” label and Fluent status icon, task title,
header context, lifecycle action, and focus outline are all legible in the
combined image.

## Required fidelity surfaces

- Fonts and typography: the existing system typography, compact queue hierarchy,
  uppercase context badge, and task-title weight match the selected direction.
- Spacing and layout rhythm: the established rail, queue, resizer, and detail
  proportions remain intact. The temporary selected card adds emphasis without
  changing surrounding controls.
- Colors and visual tokens: Completed uses the existing semantic green; Cancelled
  uses the existing rose family; reopened Active uses teal. Selection remains
  understandable without relying on color because it also has a label, icon,
  border, and retained focus.
- Image quality and asset fidelity: no raster assets were required. The status
  glyph comes from the application's existing Fluent icon font.
- Copy and content: `Just moved` and
  `<STATUS> · VIEWING IN <VIEW>` make the navigation consequence explicit
  without adding a modal or persistent notification.

## Functional and accessibility review

- Complete switches to Completed; cancel switches to All; reopen switches to
  Active.
- The lifecycle response task ID is retained through the destination reload.
  The code never selects the first task as a fallback for this transition.
- The exact row is selected, scrolled fully into the queue viewport, and receives
  keyboard focus. The context badge is an ARIA live status.
- A status-based search that would hide the moved task is cleared first; other
  filters are cleared only as a defensive fallback.
- The temporary rail glow, row card, and context badge fade after 4.2 seconds,
  leaving the normal selected row. Reduced-motion users receive the same state
  without animation.
- The compact/stacked breakpoint reduces the reveal shadow and retains the
  existing no-horizontal-overflow layout.

## Comparison history

1. First browser capture: P2, the global save-status repeated the destination
   message already present in the detail context badge. It was returned to
   `Loaded`, and the context badge became the live announcement.
2. First browser capture: P3, the “Just moved” icon was visually weak. It was
   refined into a small semantic status disc using the existing Fluent icon.
3. Final browser capture: no actionable P0, P1, or P2 differences remain.

## Verification

- `node --check Okf-Todo/wwwroot/js/app.js`
- Focused Edge/Playwright contract:
  `LifecycleActions_SwitchViewAndKeepChangedTaskSelectedRevealedAndFocused`
- The contract exercises complete, reopen, and cancel and verifies destination
  view, retained task, one selected row, reveal class, full row visibility,
  keyboard focus, task details, and lifecycle button state.

final result: passed

---

# Design QA — Triage Command refinement

## Evidence

- Selected reference:
  `docs/images/design-options/okf-todo-workspace-option-2-triage-command.png`
- User-reported mismatch:
  `artifacts/design-audit-v2/00-user-reported-rail.png`
- Final large implementation:
  `artifacts/design-qa-v2-final/triage-command-large.png`
- Final compact implementation:
  `artifacts/design-qa-v2-final/triage-command-compact.png`
- Final small-window implementation:
  `artifacts/design-qa-v2-final/triage-command-small.png`
- Final explicit Stacked implementation:
  `artifacts/design-qa-v2-final/triage-command-stacked.png`
- Same-input comparison:
  `artifacts/design-qa-v2-final/triage-command-comparison.png`
- Reference and large implementation viewport: 1487 × 1058.

## Source-to-build review

- The implementation now preserves the reference's three clearly separated
  zones: persistent task-view navigation, focused triage list, and spacious
  task-detail work surface.
- Queue navigation now uses stable semantic colors for Active, Urgent, Waiting,
  Overdue, Completed, and All. Labels, distinct icons, tooltips, and current
  state remain available so color is supplementary.
- Full-row waiting tint was removed. Waiting uses an amber rail and pill while
  teal remains the selected-row signal.
- The title, metadata, editor, checklist, and attachments retain the real
  application's controls rather than copying simplified mock controls.
- The final same-input comparison was visually inspected after the responsive
  contract passed.

## Responsive review

- Large desktop keeps the labelled rail and all three work zones.
- Compact desktop keeps a color-coded icon rail with accessible labels.
- Automatic small-window mode removes the rail and stacks list over details.
- Explicit Stacked mode now caps the list according to screen height, reserves
  detail space, and shows the body editor without an initial resize or scroll.
- No tested state introduces horizontal page overflow.

## Findings

- P0: none.
- P1: none remaining.
- P2: none remaining.

## Verification

- `node --check Okf-Todo/wwwroot/js/app.js`
- Focused Playwright contract:
  `TriageCommandWorkspace_AdaptsAcrossLargeCompactAndSmallWindows`
- Source and final large build inspected together in one comparison image.

final result: passed

# Design QA — Preferences option 1

## Evidence

- Selected reference: `C:\Users\soere\.codex\generated_images\019f64d1-7fc6-7900-afb4-ee49130ff5eb\exec-4e6cf8a6-e63d-44e8-a837-3c44eea3e138.png`
- Light implementation: `artifacts/design-qa/preferences-option-1-implementation.png`
- Dark implementation: `artifacts/design-qa/preferences-dark-implementation.png`
- Side-by-side comparison: `artifacts/design-qa/preferences-comparison.png`
- Browser QA viewport: 1280 × 720. The reference and implementation were placed together in the same 1280 × 720 comparison canvas.

## Visual review

- P0: none.
- P1: none.
- P2: none remaining. The dialog matches the selected direction's spacious modal, persistent left rail, restrained teal selection treatment, row-based settings, segmented choices, switches, dividers, and subdued desktop backdrop.
- P3: the rail uses text labels without the reference's decorative icons. The repository has no icon library, and no handcrafted replacement assets were introduced.

## Functional review

- Section navigation updates the active rail item and panel title, then shows only the selected page.
- Editor mode, color scheme, and task layout segmented controls remain wired to the existing persisted preferences.
- Source-field and relationship switches remain wired to the existing persisted task-detail visibility settings.
- Database backup preserves its working state and reports the saved filename without replacing the row markup.
- Lookup and tag management retain their existing secondary dialogs and now expose item counts in the action rows.
- The obsolete numeric editor-height field is absent; the persisted editor height remains controlled by the drag bar below the editor.
- Light and dark themes were exercised. Dark mode uses the existing off-white and amber system, avoiding low-contrast black-on-dark-green states.
- The dialog retains semantic headings, dialog/navigation landmarks, `aria-pressed` segmented controls, switch semantics, keyboard focus styles, and an internal scroll area for shorter screens.

## Verification

- `node --check Okf-Todo/wwwroot/js/app.js`
- `dotnet build Okf-Todo.slnx -c Release --no-restore --verbosity minimal`
- In-app browser interaction checks for navigation, theme switching, and database backup status.

Result: passed

---

# Preferences isolated-page follow-up

- General exposes only Editor mode.
- Appearance exposes only Color scheme.
- Task details exposes only layout and task-detail visibility controls.
- Data & values exposes only lookup and tag management.
- Backup exposes only the database backup workflow.
- Backup success status remains visible without replacing the page structure.
- Browser evidence: `artifacts/design-qa/preferences-backup-page.png`.

Result: passed

---

# Design QA — compact task browsing

## Evidence

- Source of truth: `C:\Users\soere\.codex\generated_images\019f64d1-7fc6-7900-afb4-ee49130ff5eb\exec-0c5a47a1-1c76-435c-bac4-f9ced1a22678.png`
- Implementation capture: `artifacts/design-qa/compact-task-controls-implementation.png`
- Same-input comparison: `artifacts/design-qa/compact-task-controls-comparison.png`
- Comparison layout: selected design on the left, running Photino implementation on the right.
- Viewport and state: 1584 × 933, Active view, Smart priority, `incident` and `servicedesk` tag filters selected.

## Findings

- P0: none.
- P1: none.
- P2: none in the redesigned browse controls.
- View, unified task-or-tag search, Filter, and Sort form one compact row at the tested sidebar width.
- Selected tags appear as removable contextual chips; the Filter badge reports two selections; Clear is available without reopening the filter.
- The live OR filter reports `3 of 30 tasks`. The selected concept displayed `30 tasks` despite active filters; the implementation intentionally reports the accurate filtered count.
- The controls reflow into two or three rows only at narrower sidebar container widths.
- The comparison preserves the user's saved task-list divider width. Existing detail-header and editor-toolbar responsive behavior is outside this change.

final result: passed

---

# Design QA — task type and priority filters

## Evidence

- Source visual truth: `C:\Users\soere\AppData\Local\Temp\codex-clipboard-e8748cb3-fff9-43ec-8224-d05a48d21250.png`
- Implementation screenshot: unavailable; the browser screenshot operation timed out and the Photino launch approval service returned an infrastructure error.
- Viewport: 1560 × 876.
- State: Light theme, Active view, responsive browse header.
- Full-view comparison evidence: blocked because no implementation screenshot could be captured.
- Focused-region comparison evidence: blocked for the same reason.

## Verified structure and behavior

- The live DOM exposes `Search tasks`, `Tags`, `Filter by task type`, `Filter by priority`, the sort field, and the ascending/descending control with distinct accessible names.
- View and search are grouped in the primary row; tag, type, priority, and sort controls are grouped in the secondary row.
- JavaScript syntax validation passed, the Release solution build passed, and all 18 focused bridge tests passed.
- Task type and priority filtering, removable summary chips, result counts, and the shared Clear action are implemented in the client filtering path.

## Findings

- [Blocked] Visual fidelity and responsive spacing cannot be approved without a captured implementation image from the Photino runtime.
- No code-level P0 or P1 issue was found in the implemented filter behavior.

## Comparison history

1. The reference screenshot was opened and reviewed at original resolution.
2. The implementation DOM was captured immediately after page load and confirmed the intended hierarchy and accessible controls.
3. Browser screenshots at the target viewport and a focused sidebar crop both timed out.
4. Direct Photino launch could not be approved because the approval service was temporarily unavailable.

## Next verification step

- Capture the running Photino window at approximately 1560 × 876 and compare the browse header at its normal and narrow sidebar widths.

final result: blocked

---

# Design QA — main workspace option 2

## Evidence

- Selected reference: `docs/images/design-options/okf-todo-workspace-option-2-triage-command.png`
- Large implementation: `artifacts/design-qa/triage-command-large.png`
- Compact implementation: `artifacts/design-qa/triage-command-compact.png`
- Small-window implementation: `artifacts/design-qa/triage-command-small.png`
- Same-input comparison: `artifacts/design-qa/triage-command-comparison.png`
- Reference and large implementation viewport: 1487 × 1058.

## Visual review

- P0: none.
- P1: none.
- P2: none remaining.
- The implementation preserves the selected three-zone hierarchy: task-view rail,
  triage list, and document-like detail workspace.
- The unified application bar keeps product identity, save state, setup, creation,
  lifecycle, and save actions stable.
- The reference's simplified example controls were adapted to the existing
  product contract: Tags, Type, Priority, grouped sort options, sort direction,
  result counts, waiting indicators, and lookup-defined badge colours remain
  available.
- The task list uses flat rows, separators, restrained state rails, and a quiet
  selected surface instead of independent cards.
- The detail area uses a stronger title hierarchy, more whitespace, reduced
  borders, and a contained editor surface.
- The Light and Dark styles both define the new top bar, navigation rail, list,
  selection, fields, and editor surfaces.

## Responsive review

- At 1487 px, the full labelled rail, resizable task list, and detail workspace
  remain visible.
- At 1100 px, the rail collapses to icons with accessible names and tooltips.
- At 820 px, the rail is removed, the task-view select is restored, and the list
  stacks above the detail editor.
- The initial split calculation now ignores incomplete first-frame dimensions,
  so the small-window task list retains enough height to show actual task rows.
- Browser contracts verify that all three modes avoid horizontal page overflow.

## Functional review

- Task-view rail buttons use the same unsaved-change guard, loading path, current
  view state, sort state, and list rendering as the existing select.
- The resizer measures from the task-list edge after the navigation rail was
  introduced.
- Existing new-task, editor-focus, checklist, attachment, comment, relationship,
  ownership visibility, search, and preference behavior remains covered by the
  UI test suite.

## Verification

- `node --check Okf-Todo/wwwroot/js/app.js`
- `dotnet test Okf-Todo.UiTests/Okf-Todo.UiTests.csproj -c Release --no-restore`
- `dotnet build Okf-Todo.slnx -c Release --no-restore`
- Visual comparison of the selected reference and implementation in the same
  image.

final result: passed
