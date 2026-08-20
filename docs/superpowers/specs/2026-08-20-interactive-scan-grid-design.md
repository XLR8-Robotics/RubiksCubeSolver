# Interactive Scan Grid Design

## Goal

Allow the operator to position the Rubik's Cube sampling rectangles directly on the live camera feed. The controls must support coarse whole-grid adjustment and precise per-sticker adjustment when a close camera, perspective, or robot claws prevent a regular grid from aligning correctly.

## User Experience

The Scan Grid tab retains the live camera image, sticker-color preview, automatic calibration, reset, sliders, and save controls. A small toolbar above the live feed adds four mutually exclusive edit modes:

1. **Move Grid** — dragging anywhere inside the grid moves all nine sampling rectangles together.
2. **Resize Grid** — dragging a corner handle proportionally scales all nine rectangles around the layout's center.
3. **Move Boxes** — clicking selects one numbered sampling rectangle; dragging moves only that rectangle.
4. **Resize Boxes** — clicking selects one numbered sampling rectangle; dragging its corner handle resizes only that rectangle.

The active mode has a distinct selected appearance. The selected sampling rectangle has a brighter outline and displays its sticker number from 1 through 9. Cursor shapes indicate whether a drag will move or resize.

The current sliders remain available for whole-grid size, horizontal position, vertical position, and default box inset. Slider, auto-calibration, and reset operations regenerate a regular 3×3 layout and clear any individual box adjustments. This rule prevents hidden offsets from surviving after the user intentionally requests a new base layout.

“Keep these settings” saves the displayed rectangles. Restarting the application restores the same layout.

## Coordinate Model

Each sampling rectangle is stored as normalized camera-frame coordinates:

- `X` and `Width` are fractions of frame width.
- `Y` and `Height` are fractions of frame height.
- Values are independent of WPF window size and display scaling.

The nine rectangles are ordered row-major from top-left to bottom-right, matching the existing sample indices.

The live image uses `Stretch="Uniform"`, which can introduce letterboxing. Pointer coordinates are translated through the actual rendered image bounds before conversion to normalized frame coordinates. Pointer events outside those bounds do not modify the layout.

Rectangles are constrained to the camera frame and have a safe minimum normalized width and height equivalent to at least four source pixels. Whole-grid movement preserves spacing and sizes. Whole-grid resizing proportionally scales current positions and sizes, including individual adjustments.

## Settings and Compatibility

`AppSettings` gains a persisted collection of nine normalized rectangle records. Absence of this collection means the existing `FaceMargin`, `FaceOffsetX`, `FaceOffsetY`, and `FaceSampleInset` values generate the regular layout, preserving existing settings files.

Auto-calibration and reset replace the custom collection with a newly generated regular layout. Saving writes both the existing base values and the normalized rectangles. The base values remain useful for sliders, compatibility, and regeneration.

Automatic face detection continues using its perspective-warped regular grid. Custom rectangles apply to the manual-grid path because they are calibrated against the unwarped camera feed. The UI communicates this when “Auto-find face during scans” is enabled.

## Components

### Scan rectangle model

A small data type represents one normalized rectangle and validates finite, bounded coordinates and dimensions.

### Layout calculator

A UI-independent component:

- Generates nine rectangles from the existing grid settings.
- Converts normalized rectangles to source-frame pixel rectangles.
- Moves or resizes a full layout.
- Moves or resizes an individual rectangle.
- Clamps all changes to valid bounds.

This component is used by both preview drawing and image sampling so the yellow boxes always represent the exact sampled pixels.

### Interactive camera overlay

The Scan Grid camera image is hosted in a WPF grid with a transparent interaction layer above it. The layer renders selection and drag handles and handles pointer capture for smooth dragging. The OpenCV preview continues drawing the ordinary yellow sample rectangles; the WPF layer provides edit-state emphasis and hit targets.

### View model

The view model exposes:

- Current edit mode.
- Selected sticker index.
- Current normalized rectangles.
- Commands for selecting edit modes and resetting individual adjustments.
- Methods that apply normalized move and resize deltas.

Changes update the live preview immediately but are only persisted when “Keep these settings” is pressed.

### Scanner

`FaceScanner` accepts the normalized custom layout for manual scans. It converts that layout to pixel rectangles using the actual frame dimensions, samples those exact regions, and draws those same regions in the preview.

## Data Flow

1. A camera frame supplies its source dimensions.
2. Settings provide either nine saved normalized rectangles or values used to generate a regular layout.
3. The layout calculator converts normalized coordinates to pixel rectangles.
4. `FaceScanner` samples and draws those pixel rectangles.
5. The WPF overlay maps the same normalized coordinates into the rendered image bounds.
6. A pointer drag is converted from rendered-image pixels to normalized deltas.
7. The view model updates the in-memory layout.
8. The next live-preview frame immediately uses the changed layout.
9. Saving persists the normalized rectangles.

## Error Handling

- Invalid or incomplete saved layouts fall back to the regular grid rather than preventing camera use.
- NaN, infinity, negative dimensions, and out-of-frame coordinates are rejected or clamped.
- Dragging outside the rendered image is ignored before a drag begins and clamped after pointer capture begins.
- If frame dimensions are unavailable, the overlay remains visible but editing is disabled.
- Switching to auto-find warns that custom manual rectangles are not used by perspective-warped scans.

## Testing

Unit tests cover:

- Regular layout generation from margin, offset, and inset settings.
- Normalized-to-pixel conversion at multiple camera resolutions.
- Whole-grid movement and resizing.
- Individual movement and resizing.
- Minimum size and frame-boundary clamping.
- Invalid saved-layout fallback.
- Saving and loading all nine rectangles.
- Reset and auto-calibration clearing individual adjustments.
- Agreement between rectangles used for preview drawing and sampling.
- Letterboxed `Uniform` image coordinate conversion.

Manual verification covers:

- Dragging each mode on a live 1280×720 camera feed.
- Window resizing and DPI scaling without layout drift.
- Restarting and restoring a saved layout.
- Confirming robot claws can be excluded from all nine sample boxes.
- Confirming scan captures use the same rectangles shown in the live feed.

## Out of Scope

- Separate layouts for each cube face.
- Automatic claw detection.
- Per-camera color calibration.
- Changing the automatic perspective-warp algorithm.
