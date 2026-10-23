# UI design system

Direction: **modern · minimal · technical · clean**, following the spirit of Windows 11 Fluent. Framework: WPF on
.NET 10 with the built-in Fluent theme (`Application.ThemeMode`) for standard controls, plus custom vector-drawn
controls for everything controller-specific. WPF was kept: it renders vector geometry with hardware acceleration,
is per-monitor-DPI aware, and the existing architecture (MVVM, services) carried over without a framework migration.

## Tokens (`Themes/Colors.Dark.xaml`, `Themes/Colors.Light.xaml`)

All colors are resource tokens referenced with `DynamicResource`; `ThemeManager` swaps the dictionary at runtime
(Dark / Light / follow Windows). Custom-drawn controls derive from `ThemedElement`, which resolves the same tokens
and re-renders on theme change.

| Token | Dark | Light | Use |
|---|---|---|---|
| `Bg.App` / `Bg.Nav` / `Bg.Card` | #111317 / #0C0E11 / #181B20 | #F3F4F6 / #EAECEF / #FFFFFF | window, sidebar, cards |
| `Text.Primary` / `Secondary` / `Tertiary` | #E8EAED / #A3AAB4 / #6C747F | #1B1F24 / #55606B / #87909A | text hierarchy |
| `Accent` (Xbox green) | #4FB547 | #107C10 | pressed inputs, primary actions, live state |
| `Output` | #5AA7FF | #0F6CBD | *processed* values (always blue, everywhere) |
| `Warning` (amber) / `Danger` (red) | #F0A830 / #EF5D5D | #A86200 / #C42B1C | stale data, thresholds / errors, deadzones |
| `Pad.*` | Carbon Black finish | Robot White finish | controller rendering |

The Fluent theme's own accent keys (`SystemAccentColor*`, `AccentFillColor*Brush`, `SystemColors.AccentColor*`) are
overridden so checkboxes, sliders and list selections also use Xbox green instead of the Windows accent color.

Color rules: green means "this input is active / this is the primary action"; blue means "after processing";
amber means "attention / threshold / stale"; red means "error / deadzone". Nothing glows; accents are used sparingly.

## Typography & layout

- Segoe UI Variable (falls back to Segoe UI); Cascadia Mono/Consolas for numbers so columns do not jitter.
- Scale: page title 26 SemiBold · card title 14 SemiBold · body 13 · caption 12 · labels 12 tertiary · metric 32.
- 4 px spacing grid (4/8/12/16/24); cards: 8 px radius, 1 px border, 16 px padding, 12 px gaps.
- `AdaptivePanel` lays cards out in as many equal columns as fit a minimum width (with optional column spans), so
  narrow windows reflow to fewer columns instead of clipping. Verified at 980 × 720, 1400 × 900 and maximized 1936 × 1048.

## Components (`Controls/`, `Themes/Styles.xaml`)

| Component | Purpose |
|---|---|
| `Card` | Header (icon, title, subtitle, actions) + content; the only container style used on pages. |
| `StatusPill` | Connection / stream / driver state chips (Ok, Info, Warning, Error, Neutral). |
| `IconBlock` | Renders Fluent UI System Icons (MIT) geometry in the inherited text color. One family, 20 px grid. |
| `NavItem` (sidebar) | Icon + label, hover / pressed / selected (accent bar) / keyboard-focus states. |
| `Btn`, `IconBtn`, `AccentBtn`, `ToolBtn` | Fluent buttons with consistent spacing; icon variants. |
| `SliderRow` | Label + slider + exact numeric entry for every tuning parameter (keyboard editable). |
| `TriggerGauge` | Raw depth bar (green), processed output line (blue), activation threshold tick (amber). |
| `BatteryGauge` | Real percentage fill, *or* 0–3 coarse segments for XInput levels, *or* an empty outline — never a fake %. |
| `StickPlot` | Unit circle + square gate, ±0.5 grid, axis labels, deadzone (red) / outer deadzone (amber) rings, raw (neutral) and processed (blue) points and trails, optional zoom range and circularity outline, legend. |
| `TimeSeriesPlot` | Value axis with labels, time axis (−window … 0 s), legend, theme series colors. |
| `CurvePlot` | Input → output mapping with labelled axes, identity reference, shaded dead region, live marker. |
| `ControllerVisual` | The interactive Xbox Series X\|S controller (below). |

Pages fade/slide in over 180 ms (`Page` style). Tooltips share one style (wrapping, max 340 px).

## Controller drawing (`Controls/ControllerVisual.cs`)

Original vector artwork in a fixed 640 × 450 design space, scaled uniformly (aspect ratio never distorted).
Positions were measured from a front product photo (pixel coordinates − 85 px on Y):

| Element | Center (design units) | Size |
|---|---|---|
| Left stick | (170, 140) | collar r 38, opening r 31, cap r 26, concave dish r 17, 12 units travel |
| D-pad (hybrid dish + cross) | (245, 240) | dish r 46, cross 76 × 26 |
| Right stick | (396, 231) | as left stick |
| ABXY center | (468, 151) | spacing 36: A below (green), B right (red), X left (blue), Y above (yellow); r 16.5 |
| Xbox button | (320, 93) | r 21 (neutral mark, no logo) |
| View / Menu | (278, 150) / (362, 150) | r 11.5 with glyphs |
| Share | (320, 181) | 32 × 17 pill with upload glyph |
| LB / RB | shoulder bands | wrap over the shoulders, overlap the body edge |
| LT / RT | behind the bumpers | visible sliver fills upward with analog depth |

Body outline: 10 cubic Bézier segments for the left half (top center → bottom center), mirrored for the right half,
so both grips are identical and smooth. Depth comes from a 4-layer soft shadow, a darker lower rim, a top-to-bottom
body gradient, a faint top sheen and a 1 px highlight inside the upper edge — no glow effects.

Interaction: pressed parts light up immediately (accent fill; ABXY fill with their own color) and fade out over
120 ms on release; sticks move in full 2-D proportional to the raw values; optional blue rings show the processed
stick positions; triggers fill with analog depth (0 / 50 / 100 %). When the controller disconnects or input stops
for more than 0.5 s, every live value is cleared, the drawing dims and shows a status overlay — nothing stays pressed.

## Performance

- Input sampling stays on its own thread (250 Hz default); the UI only reads immutable snapshots.
- UI frames are throttled to the configured FPS; only the visible page updates.
- Plots redraw only while input is still changing within their visible time span; text layouts and pens are cached
  per theme; rate texts update once per second (frequently changing text would re-layout the page every frame).
- Measured on the development machine (16 logical CPUs), idle controller: Dashboard 3.1 % and Input Monitor 2.0 % of
  one core (down from 47 % before the redraw throttling); 1.9 % with the window minimized (sampler only).

## Accessibility

- All navigation, buttons, sliders, combo boxes and check boxes are keyboard reachable; nav items show a focus border.
- Icon-only buttons and custom-drawn controls have `AutomationProperties.Name` and tooltips.
- Color is never the only signal: pressed chips also change text color, states have text labels, battery shows text.
- Per-monitor DPI v2 manifest; all custom drawing is vector (verified renders at 1.0×, 1.25×, 1.5×, 2.0×).

## Files changed in the UI redesign

New: `Controls/ThemedElement.cs`, `Controls/UiControls.cs` (Ui, IconBlock, Card, StatusPill, AdaptivePanel,
TriggerGauge, BatteryGauge), `Services/ThemeManager.cs`, `Themes/Colors.Dark.xaml`, `Themes/Colors.Light.xaml`,
`Themes/Icons.xaml`, `build/gen-icons.sh`, `docs/UI_DESIGN.md`, `docs/screenshots/*`.

Rewritten: `Controls/ControllerVisual.cs`, `Controls/StickPlot.cs`, `Controls/TimeSeriesPlot.cs`, `Controls/CurvePlot.cs`,
`Themes/Styles.xaml`, `MainWindow.xaml`, all seven `Views/*.xaml`, `ViewModels/LiveViewModel.cs`.

Modified: `App.xaml`, `App.xaml.cs` (theme at startup), `Controls/Converters.cs` (ResourceKeyConverter),
`ViewModels/MainViewModel.cs` (stream status, theme toggle, redraw invalidation), `ViewModels/ControllerItemViewModel.cs`
(battery segments, transport chip), `ViewModels/DiagnosticsViewModel.cs` (before/after comparison, drift scatter,
center offset), `ViewModels/MappingViewModel.cs`, `ViewModels/VirtualControllerViewModel.cs`, `ViewModels/SettingsViewModel.cs`
(theme), `Localization/Strings.cs` (EN + 繁體中文), `Core/Profiles/AppSettings.cs` (`Theme`), `tests/.../ProfileTests.cs`.

Removed: `Controls/Palette.cs` (hard-coded colors).

Input processing, battery logic, drift/deadzone math, mapping, virtual controller and profiles were **not** changed
functionally.
