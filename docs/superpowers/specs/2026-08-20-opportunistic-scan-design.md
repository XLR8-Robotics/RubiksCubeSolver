# Opportunistic Cube Scan Design

## Goal

Scan the cube faster by filling stickers only when they are visible, instead of completing each equatorial face with a dual-hold merge. Keep the existing one-direction yaw loop Front → Right → Back → Left → Front. Photograph Front/Back stickers 2 and 8 during gripper moves that already retract Top/Bottom.

## Sticker numbering

Row-major on the camera face:

```
1 2 3
4 5 6
7 8 9
```

Zero-based sample indices: sticker N is index N − 1. Sticker 2 is index 1. Sticker 8 is index 7.

## Hold visibility

On the face pointing at the camera:

| Hold | Obstructed | Write |
|---|---|---|
| Top/Bottom in, Left/Right out | 2 and 8 | 1, 3, 4, 5, 6, 7, 9 |
| Left/Right in, Top/Bottom out | 4 and 6 | 2 and 8 only |

Right and Left are fully visible in the Top/Bottom-hold photo. Store all nine. Do not take a second photo for those faces.

Top and Bottom remain unobstructed pitch photographs of all nine stickers. That motion does not change.

## Sequence

1. Front at camera. Top/Bottom hold, Left/Right out. Photograph Front. Write 1, 3, 4, 5, 6, 7, 9.
2. Yaw 90° to Right. Same hold. Photograph Right. Write all 9.
3. Left/Right in, Top/Bottom rewind/home, Top/Bottom in, Left/Right out. No photo.
4. Yaw 90° to Back. Top/Bottom hold. Photograph Back. Write 1, 3, 4, 5, 6, 7, 9.
5. Left/Right in, Top/Bottom retract. Photograph Back. Write only 2 and 8. Then rewind yaw turners, Top/Bottom in, Left/Right out.
6. Yaw 90° to Left. Top/Bottom hold. Photograph Left. Write all 9.
7. Rewind/home as in step 3. No photo.
8. Yaw 90° to Front. Left/Right in, Top/Bottom retract, rewind yaw, **keep** Left/Right holding and Top/Bottom clear. Photograph Front. Write only 2 and 8.
9. Pitch Top, full 1–9, return. Pitch Bottom, full 1–9, return. Same as current pitch steps.
10. Finish hug.

Keep yaw-home after each 90° scan turn. Do not chain 180°/360° yaw without rewind.

A photo never overwrites stickers that were obstructed in that frame. Front 2 and 8 stay empty on the live net until step 8.

## Architecture

- `ScanStickerMask` holds the Top/Bottom-hold, Left/Right-hold, and all-nine index lists.
- `ScanFaceBuffer` accumulates samples per face and records which indices have been written.
- `IScanSession.CaptureMaskedAsync` photographs the current camera face and writes only the given indices.
- Sequence steps call existing expose/turn/home/pitch commands, then masked capture.
- After Back’s 2/8 photo, use the existing yaw-home that restores Top/Bottom hold.
- After returning to Front, yaw-home **without** restoring Top/Bottom hold so the Front 2/8 photo and the following pitches share the Left/Right-hold pose.

## Out of scope

- Changing pitch geometry or Top/Bottom photograph motion.
- Chaining multiple yaw turns without homing.
- Per-face scan-grid layouts.
- Automatic claw detection.
