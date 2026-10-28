# ERPFlow frontend design system

## 1. Atmosphere & identity
Preserve the existing quiet operational interface: warm paper surfaces, deep-teal actions,
compact readable tables, Geist typography, and restrained feedback. Product images extend
Reference Data without changing its navigation or information hierarchy.

## 2. Color
Use the existing `src/styles.scss` variables: `--bg`, `--surface`, `--tint`, `--ink`,
`--muted`, `--line`, `--primary`, `--primary-strong`, `--primary-soft`, `--on-primary`,
`--success`, `--success-soft`, `--danger-soft`, `--danger-ink`, `--warn-soft`, `--warn-ink`.
Do not introduce a second accent or hard-coded component colors. Existing application
theme behavior is preserved; this increment does not create a new theme.

## 3. Typography
Use inherited Geist Variable body type and `--mono` for identifiers. Existing sizes are
1rem body, .875rem supporting text/labels, .75rem captions, and 1.125rem dialog headings.
Product names wrap rather than shrinking or silently truncating in the preview.

## 4. Spacing & layout
Reuse the 4px rhythm: 8px tight gaps, 12px controls, 16px sections, 20px page gaps,
24px dialog padding, and 32px viewport allowance. Buttons have a 44px minimum target.
Image thumbnails occupy 48px square; the full preview uses a stable 4:3 frame with
`object-fit: contain`. Dialog width is at most 560px and viewport width minus 32px;
the dialog owns vertical overflow within the viewport. Existing responsive table-card
layout remains the listing's small-screen contract.

## 5. Components
- Existing `.btn`/`.btn-primary`/`.btn-ghost`/`.btn-danger`: retain hover, press, focus,
  disabled, and busy states. Use text labels for mutation actions.
- Existing Icon, Pill, Select, Pagination, and native-dialog conventions remain reusable
  primitives. Native dialogs provide focus trapping, Escape, and focus restoration.
- Product image: thumbnail button opens a named preview dialog. Empty, loading, failed,
  and loaded states retain the same geometry. A package glyph is the fallback, not a
  broken image. Admins receive a labelled real file input, selected-file preview,
  explicit save, and an inline delete-confirmation step. Read-only users see preview only.
- Errors live next to the affected image; success is announced with `role=status`.
  Refresh after conflicts never silently retries a mutation.
- Catalog forms reuse `.card`, `.stack`, `.form-grid`, `.field`, and standard buttons.
  Status actions use the existing confirmation dialog. A conflict preserves the draft
  and offers an explicit reload; forms never silently overwrite newer catalog values.
  Read-only detail routes share the same fields and image component without save actions.

## 6. Motion & interaction
Reuse global dialog transitions (200ms enter, 120ms exit) and button press feedback
(160ms `--ease-out`, scale .97). Add no ornamental motion. Honor existing reduced-motion
styles. Changing an image preserves preview dimensions and immediately removes stale bytes.

## 7. Depth & surface
Use existing bordered surfaces (`1px solid var(--line)`), `--radius` (12px), tint washes,
and native dialog backdrop. No new shadows, gradients, or accent borders for selection.

## 8. Accessibility constraints & accepted debt
New controls require visible focus, 44px targets, explicit names, file-format/size help,
descriptive image alt text, live operation feedback, and keyboard-accessible confirmation.
Busy mutations prevent duplicate actions and dismissal until completion. No new design
debt is accepted; unrelated existing visual styles are outside this increment.
