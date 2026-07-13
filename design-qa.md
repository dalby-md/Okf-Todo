**Design QA**

- Source visual truth: `C:\Users\soere\AppData\Local\Temp\codex-clipboard-64c5bff9-efbc-43ad-a4cf-23f75186accf.png`
- Implementation screenshot: `C:\git\Okf-Todo\artifacts\design-qa\theme-native.png`
- Combined comparison: `C:\git\Okf-Todo\artifacts\design-qa\theme-comparison.png`
- Viewport: 1920 x 1200 desktop
- State: Active task selected with HTML editor initialized

**Full-View Comparison Evidence**

The reference and implementation were compared together in `theme-comparison.png`. The implementation carries over the requested palette rather than the reference page composition: near-black page surfaces, neutral charcoal controls, thin gray borders, off-white text, and amber primary actions and selection indicators. Existing semantic task colors remain visible.

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

1. Initial native capture: P1, TinyMCE document canvas remained white. Fixed by loading the dark TinyMCE skin and dark content styling.
2. Second native capture: P2, TinyMCE used a blue-black tint inconsistent with the reference's neutral charcoal. Fixed with neutral toolbar, status bar, iframe, and editor-content colors.
3. Final native capture: editor chrome and content now use neutral charcoal/near-black surfaces with readable off-white controls and text.

**Follow-up Polish**

No blocking follow-up. Lookup-defined badge colors intentionally differ from amber so task classifications preserve their meaning.

final result: passed
