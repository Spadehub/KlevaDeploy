# Script Editor Overhaul Plan For Trae IDE Agent

## Important Terminology

In this document:

- `Trae IDE mode` means the execution environment for the AI agent doing the work.
- `Solo mode` means Trae's standalone high-token mode, which should not be used for this task unless explicitly requested.
- `script editor` means the detached editor window in KlevaDeploy.

This document does **not** propose adding a new "IDE mode" feature inside KlevaDeploy.

The product surface being overhauled is the existing detached script editor. The phrase `IDE mode` refers only to how the AI agent should execute the work inside Trae.

## Execution Requirement

This overhaul should be implemented by an AI agent running in `Trae IDE mode`, not `Solo mode`.

Reason:

- The work needs repeated file inspection, incremental edits, validation, and architectural continuity.
- IDE mode is more efficient for this repository-sized task and avoids unnecessary token burn.
- The agent must operate with persistent workspace context and file-level control.

## Purpose

This document is the implementation handoff for a full overhaul of the detached script editor in KlevaDeploy.

The goal is to transform the current editor into a polished, production-ready experience comparable to leading commercial code editors. The scope is the editor window, its layout, its resizing behavior, its persistence model, and its visual system.

This plan is written so a Trae agent in IDE mode can execute the work without ambiguity.

## WPF Execution Context

This handoff applies to the current `WPF desktop application`.

It must not be translated into web assumptions such as:

- CSS
- browser rendering
- browser compatibility matrices
- mobile web layouts
- rem-based viewport zoom logic

All implementation and validation work must target:

- `WPF`
- `AvalonEdit`
- `GridSplitter`
- Windows desktop window resizing
- DPI scaling
- keyboard accessibility
- WPF automation properties
- persisted layout state

## Product Scope

### In Scope

- The detached script editor window and its related behavior.
- Editor layout architecture, panel sizing, resizing, persistence, and responsiveness.
- Editor visual redesign, accessibility improvements, and interaction polish.
- Editor validation, QA, and release hardening.
- Density tuning through WPF spacing, sizing, and chrome optimization.
- Icon standardization and tooltip/accessibility coverage for icon-only actions.

### Out Of Scope

- Creating any new in-app feature called `IDE mode`.
- Solo mode changes inside Trae.
- The inline process form editing experience except where launch points reference the detached editor.
- Unrelated app-wide redesign work outside shared tokens directly reused by the editor.
- Browser-specific requirements such as Chrome, Firefox, Safari, or Edge testing.
- CSS zoom, rem scaling, or browser viewport behavior.
- Mobile-first redesign work.

## Primary Files

### Editor Surface

- `Views/ScriptEditorWindow.xaml`
- `Views/ScriptEditorWindow.xaml.cs`
- `ViewModels/ScriptEditorViewModel.cs`
- `Themes/EditorStyles.xaml`
- `Docs/ScriptEditorUxAudit.md`

### Integration And Persistence

- `Models/UserPreferences.cs`
- `Services/PreferencesService.cs`
- `Views/CreateProcessDialog.xaml.cs`
- `ViewModels/CreateProcessViewModel.cs`
- `MainWindow.xaml.cs`

## Current-State Summary

The detached script editor already contains the right building blocks for a serious editor:

- Process navigator
- Center code editor
- File explorer
- Diagnostics list
- Terminal output
- Document tabs
- Run / stop / restart tooling

However, the implementation is not yet production-ready because the shell still behaves like a collection of panels rather than a cohesive, editor-first layout system.

## Critical Problems To Solve

### 1. Visual Redesign

The current UI still feels unpolished and fragmented. The redesign must:

- Remove the current inconsistent visual feel.
- Establish one coherent design system for the entire editor surface.
- Use consistent spacing, typography, elevation, borders, iconography, and interaction states.
- Improve contrast and focus treatment to meet WCAG accessibility expectations.
- Make the code editor visually dominant over surrounding chrome.

### 2. Code Editor Vertical Space Optimization

The editor does not currently prioritize code space aggressively enough. The overhaul must:

- Reallocate screen real estate toward the code surface.
- Reduce chrome bloat in the header, toolbar, diagnostics, and terminal areas.
- Support collapsing or minimizing non-primary panels.
- Guarantee a default layout where the editing workspace receives at least 75% of usable viewport height.

### 3. Drag-And-Drop Panel Resizing Fix

The current resizing behavior is too basic and not production-safe. The overhaul must:

- Rebuild the resize model for all editor panes.
- Ensure resizing is smooth and accurate.
- Add visible feedback for hover and active drag states.
- Enforce minimum dimensions so the UI cannot collapse into broken layouts.
- Persist user layout preferences across sessions.

### 4. Icon And Action Consistency

The editor must replace prototype-level mixed button patterns with a disciplined, app-aligned action system. The overhaul must:

- Standardize icon dimensions and button containers.
- Use semantically correct icons for close, clear, save, run, undo, and redo.
- Replace remaining text-heavy action chrome with compact icon-first controls where appropriate.
- Ensure grouped actions use consistent spacing and predictable placement.

### 5. Header, Tag, And Density Cleanup

The editor still contains redundant state chrome and avoidable vertical waste. The overhaul must:

- Merge redundant header and topbar responsibilities.
- Eliminate duplicate rendering of document state, language, ready/running state, and diagnostics counts.
- Fix badge truncation behavior and keep ellipsis rendering visually clean.
- Increase code-space priority through denser, better-structured chrome rather than unreadably small typography.

### 6. Undo/Redo And Interaction Completeness

The editor must provide complete editing affordances expected from a production tool. The overhaul must:

- Add real undo and redo behavior with per-document state tracking.
- Surface undo and redo through dedicated icon buttons.
- Expose tooltips and automation names on every icon-only control.
- Preserve keyboard-friendly interaction patterns.

## Current Technical Baseline

### Layout

The current shell uses fixed default dimensions:

- Left navigator: `280`
- Center editor: `*`
- Right explorer: `320`
- Bottom terminal: `260`

This creates a functional but rigid structure.

### Compact Layout

Compact behavior is currently threshold-based and driven by window width. It mainly toggles explorer visibility instead of implementing a true responsive layout model.

### Resizing

Resizing is currently handled by standard WPF `GridSplitter` controls with minimal styling and no advanced resize contract.

### Persistence

There is no dedicated editor layout persistence model for:

- Left pane width
- Right pane width
- Terminal height
- Collapsed or expanded state
- Window geometry
- Active layout profile

## Overhaul Objectives

The finished editor must satisfy all of the following:

- Visually matches the quality level of a professional code editor.
- Prioritizes the code workspace over secondary chrome and panels.
- Provides reliable panel resizing with no broken states.
- Persists layout preferences across sessions.
- Feels intentional and polished in all states, including empty, loading, compact, and edge conditions.
- Uses one consistent icon and badge system with correct semantics.
- Avoids duplicated state chrome across topbar and document header surfaces.
- Remains stable across supported desktop window sizes and Windows DPI scales.

## Architecture Direction

### 1. Replace Ad Hoc Layout Rules With A Pane-State Model

The current layout relies too heavily on fixed dimensions and direct view logic. Replace this with a dedicated layout state model that controls:

- Left pane width
- Right pane width
- Bottom pane height
- Pane collapsed states
- Last active layout mode
- Window size and position
- Breakpoint-derived layout behavior

This state should be persisted through editor-specific preference fields, not scattered UI-only assumptions.

### 2. Treat The Editor As Its Own Layout System

Do not continue expanding the current show or hide approach. Instead:

- Define explicit desktop-large, desktop-standard, and compact layouts.
- Change actual grid measurements when panes collapse.
- Ensure hidden panes truly release space back to the editor.
- Keep the editor usable and visually stable at every breakpoint.

### 3. Centralize Design Tokens

The editor must have a dedicated design token system. Expand `Themes/EditorStyles.xaml` into a complete editor token layer covering:

- Spacing scale
- Typography scale
- Surface hierarchy
- Semantic colors
- Splitter states
- Tab states
- Toolbar states
- Focus ring and accessibility states
- Motion timing

## Implementation Roadmap

## Phase 1: Audit And Documentation Review

### Goal

Build a precise dependency map and create a complete implementation brief before touching core behavior.

### Required Review

The implementing agent must review these files first:

- `Docs/ScriptEditorUxAudit.md`
- `Views/ScriptEditorWindow.xaml`
- `Views/ScriptEditorWindow.xaml.cs`
- `ViewModels/ScriptEditorViewModel.cs`
- `Themes/EditorStyles.xaml`
- `Models/UserPreferences.cs`
- `Services/PreferencesService.cs`

### Tasks

- Inventory all visible editor panels and their responsibilities.
- Map every existing resize boundary and splitter.
- Identify every place where layout is controlled by fixed dimensions or visibility toggles.
- Identify all UI chrome that reduces usable code space.
- Identify which editor resources are editor-specific versus inherited from general app styling.
- Document all current persistence gaps.
- Confirm that no work is required in Solo mode and no new in-app mode is being introduced.

### Deliverables

- A dependency map for the editor.
- A layout debt report.
- A visual debt report.
- A persistence schema proposal.
- A list of all files that must change.
- A wireframe or structural layout definition for the target editor shell.

### Phase 1 Acceptance Criteria

- The implementing agent can explain the current layout without guessing.
- All current sizing and collapse rules are documented.
- The future-state layout model is defined before coding starts.

## Phase 2: Core Layout Overhaul

### Goal

Rebuild the editor shell so panel behavior is robust, responsive, and code-first.

### Core Work

- Replace rigid layout assumptions with a pane-state-driven model.
- Rebuild the workspace so the center editor is the primary region by default.
- Implement true collapsible left, right, and bottom panes.
- Ensure collapsed panes return space to the code area immediately.
- Establish safe minimum sizes for all resizable regions.
- Persist layout changes after resize and collapse or expand actions.
- Add responsive breakpoints for large, standard, and compact editor usage.

### Mandatory Layout Rules

- Default layout must allocate at least 75% of usable viewport height to the editing workspace.
- The bottom terminal must not permanently steal excessive space.
- Diagnostics should not consume major vertical space unless needed.
- Right explorer and left navigator must be collapsible without breaking navigation.
- Compact layout must be a real layout profile, not just a visibility toggle.
- Restored pane widths must be clamped safely against current window size.
- The center code editor must retain a protected minimum width.

### Recommended Structural Changes

- Move layout values into a dedicated `EditorLayoutState` model or equivalent.
- Bind row and column sizes to state instead of hard-coded values.
- Introduce explicit collapse commands and restore commands for each secondary pane.
- Add resize-complete hooks that trigger persistence writes.
- Support layout reset to sane defaults.

### Deliverables

- New layout state model.
- Refactored editor shell with state-driven grid behavior.
- Resizable and collapsible pane system.
- Persisted layout restore logic on reopen.

### Phase 2 Acceptance Criteria

- The editor remains stable at all supported window sizes.
- No collapsed pane leaves behind dead space.
- No drag action can break the layout.
- Reopening the editor restores the prior layout accurately.
- Code space is materially larger than before.

## Phase 3: UI And UX Visual Refresh

### Goal

Apply a full professional visual redesign once the underlying shell is stable.

### Core Work

- Build a cohesive visual system for the editor.
- Reduce visual noise and card fragmentation.
- Give the code surface higher visual priority than surrounding chrome.
- Improve tab appearance, hierarchy, and discoverability.
- Redesign splitters so they are visible, discoverable, and polished.
- Standardize iconography for editor actions.
- Improve hover, focus, active, selected, and disabled states across the editor surface.
- Ensure text hierarchy is clear and consistent.
- Improve semantic styling for diagnostics, execution state, and terminal information.
- Eliminate duplicate status badges across the topbar and document header unless they serve distinct scopes.
- Normalize icon sizes:
  - inline action icons `16x16`
  - toolbar and navigation icons `20x20`
- Keep icon-only button containers at a consistent accessible size.
- Normalize inter-button spacing to `8px` for grouped controls.

### Accessibility Requirements

- Meet WCAG-compliant contrast targets for text and UI controls.
- Provide strong focus-visible treatment for keyboard use.
- Ensure all controls have meaningful automation names.
- Improve hit targets for glyph buttons and splitter handles.
- Validate keyboard-only usability across the full editor shell.
- Ensure every icon-only button exposes a clear tooltip and focus-accessible description.

### Visual Quality Bar

The final result must feel comparable to a modern professional code editor, not a generic app dialog with editor features added onto it.

### Deliverables

- Expanded editor token system.
- Updated editor shell styles.
- Updated pane, tab, toolbar, diagnostics, and terminal visuals.
- Updated interaction states for all major controls.

### Phase 3 Acceptance Criteria

- The editor reads as one consistent system.
- The code surface is clearly the visual focal point.
- Splitters are easy to discover and interact with.
- The interface no longer feels unfinished or visually flat.

## Phase 4: Validation And Testing

### Goal

Treat the overhaul as a production release, not a styling pass.

### Validation Work

- Perform editor layout regression testing.
- Stress-test all resizable panes at extreme sizes.
- Validate layout persistence across reopen and app restart.
- Validate compact and standard layout breakpoints.
- Validate high-DPI and multi-monitor desktop behavior.
- Validate keyboard accessibility and focus order.
- Validate contrast and screen-reader readability where applicable.
- Run task-based user testing to confirm the editor feels finished and intuitive.
- Validate icon sharpness and alignment at `100%`, `125%`, `150%`, and `200%` Windows scaling.
- Validate undo and redo behavior across multiple open documents.
- Validate tooltip coverage for every icon-only button.

### Required Test Areas

- Left, center, and right pane resizing
- Bottom terminal resizing
- Collapse and expand behavior
- Layout restore behavior
- Window maximize and restore behavior
- Extreme drag positions
- Open documents with different content sizes
- Diagnostics visible versus hidden states
- Empty explorer and terminal states
- Duplicate badge and tag elimination
- Badge truncation and ellipsis behavior
- Icon-only button tooltip and automation coverage
- Undo and redo history behavior per document

### Deliverables

- Regression checklist
- Accessibility checklist
- Manual QA matrix
- Release gate report

### Phase 4 Acceptance Criteria

- No layout corruption remains.
- No resize path creates unusable geometry.
- Persisted layouts restore correctly.
- Accessibility review passes.
- User testing confirms the editor feels polished and production-ready.

## Actionable Work Breakdown

### Workstream A: Layout Engine

- Refactor the editor shell away from fixed-size assumptions.
- Introduce a dedicated editor layout state model.
- Convert visibility-based collapse into true space-reclaiming collapse behavior.
- Add layout reset capability.

### Workstream B: Persistence

- Extend `UserPreferences` with editor-only layout fields.
- Save layout state on drag completion, collapse, expand, and window close.
- Restore saved layout safely on editor startup.
- Guard against invalid or outdated saved values.

### Workstream C: Resize Interaction

- Replace passive splitters with polished resize affordances.
- Add hover and active drag visual states.
- Enforce pane minimums and sensible maximums.
- Prevent broken panel configurations.
- Ensure live resizing feels immediate rather than preview-only.
- Clamp restored widths and heights against the current window geometry.

### Workstream D: Space Optimization

- Reduce unnecessary header and toolbar height.
- Minimize redundant captions and status chrome.
- Make diagnostics collapsible or less intrusive when not actively needed.
- Keep terminal useful without allowing it to dominate the window.
- Remove duplicated state surfaces that consume height without adding information.
- Increase effective content density by tightening spacing rather than shrinking text excessively.

### Workstream E: Visual System

- Expand `EditorStyles.xaml` into a complete editor token and control-style layer.
- Standardize color roles, typography, spacing, and icon usage.
- Unify tab, toolbar, tree, diagnostics, and terminal styling.
- Standardize badge sizing, padding, corner radius, and truncation behavior.
- Standardize icon sizing and icon-button container styles.
- Normalize primary, secondary, support, and pane-local action placement.

### Workstream F: Editor Actions

- Implement real undo and redo behavior with per-document state tracking.
- Add dedicated undo and redo buttons in the persistent editor action group.
- Ensure save/apply/open/new actions remain context-aware by document type.
- Ensure all icon-only controls expose tooltips and automation names.

### Workstream G: Validation

- Add focused tests for persistence and layout safety.
- Run manual desktop QA at supported sizes and display scales.
- Validate accessibility behavior before release.

## File-Level Execution Guidance

### `Views/ScriptEditorWindow.xaml`

Primary shell refactor target.

Expected changes:

- Grid structure
- Pane collapse mechanics
- Bound row and column sizing
- Splitter placements
- Header and toolbar density
- Diagnostics and terminal allocation
- Badge placement and deduplication
- Icon-button conversion
- Tooltip coverage

### `Views/ScriptEditorWindow.xaml.cs`

Reduce this file to strictly view-specific interaction logic wherever possible.

Expected changes:

- Layout event wiring
- Resize-complete persistence hooks
- Compact-layout handling updates
- Optional drag-state visual coordination

### `ViewModels/ScriptEditorViewModel.cs`

Primary state and behavior coordination target.

Expected changes:

- Layout state exposure
- Collapse and expand commands
- Layout mode calculation
- Persistence coordination
- Removal of simplistic compact-layout assumptions
- Undo and redo state tracking
- Badge visibility logic and deduplication support

### `Themes/EditorStyles.xaml`

Primary visual redesign target.

Expected changes:

- New design tokens
- Splitter styles
- Tab redesign
- Toolbar refinement
- Tree and explorer polish
- Diagnostics and terminal visual hierarchy improvements
- Icon size and button-container standards
- Badge truncation and density rules

### `Themes/Icons.xaml`

Primary icon standardization target.

Expected changes:

- Add missing semantic icons where needed
- Ensure close is distinct from delete
- Ensure clear is distinct from error
- Keep icon geometry appropriate for `16x16` and `20x20` usage

### `Models/UserPreferences.cs`

Primary persistence target.

Expected changes:

- Add editor layout fields
- Support safe load and save defaults
- Preserve backward compatibility for existing preference files

### `Services/PreferencesService.cs`

Use as the persistence access point if needed for cleaner editor-state save and restore flows.

## Non-Negotiable Constraints

- The work targets the detached script editor only.
- No new in-app feature called `IDE mode` should be introduced.
- Solo mode in Trae is not part of the product scope.
- The work should be executed in Trae IDE mode for efficiency.
- Layout state persistence must tolerate missing or old preference data.
- Accessibility is part of the definition of done, not a later enhancement.
- Resize behavior must never produce dead space, invisible active panels, or unrecoverable layouts.
- Do not reinterpret requirements into browser or CSS terminology.
- Density improvements must come from WPF layout cleanup, not artificial application zoom.

## Definition Of Done

The overhaul is complete only when all of the following are true:

- The script editor is visually polished and cohesive.
- The code workspace clearly dominates the available space.
- Default layout gives at least 75% of usable viewport height to editing-focused work.
- Left, right, and bottom panes resize smoothly and safely.
- Pane states persist across sessions.
- Compact layout works as a true layout profile.
- Accessibility checks pass.
- User testing confirms the editor feels finished.
- No accidental work was done on unrelated app surfaces.
- Duplicate document-state and diagnostics badges are removed or intentionally scoped.
- Icon sizing is consistent and visually correct across the editor.
- All icon-only buttons expose clear tooltips and automation names.
- Undo and redo work as real stateful editor actions.
- The editor remains stable at supported Windows DPI scales.

## Recommended Execution Order

1. Complete the audit and future-state spec.
2. Implement the pane-state model and persistence.
3. Rebuild collapse and resize behavior.
4. Reclaim code space and reduce chrome bloat.
5. Apply the full visual redesign.
6. Run validation, fix edge cases, and release only after the full QA gate passes.

## Final Instruction To The Implementing Agent

Run this task in `Trae IDE mode`, not `Solo mode`.

Do not begin with cosmetic styling alone.

The correct order is:

1. layout architecture
2. resize reliability
3. persistence
4. space optimization
5. visual polish
6. validation

If the shell is not structurally correct first, the visual redesign will only hide the underlying problems temporarily.
