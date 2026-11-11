---
title: Stan Airborne Model
created: 2026-11-11
tags:
  - game
  - 3d-model
tools:
  - Blender 5.3
  - MPFB 2.0.18
---

# Stan Airborne Model

A 3D character of Stan as a Second World War Irish soldier serving in the British Airborne. Built on 2026-11-11 in Blender 5.3 with the MPFB 2.0.18 add-on (MakeHuman for Blender).

Current model: `3d-model/final/stan_airborne.blend`.

![[attachments/full_front.png|300]] ![[attachments/full_34.png|300]]

![[attachments/face_front.png|250]] ![[attachments/face_34.png|250]]

The renders in `attachments/` are copies of `3d-model/final/`; copy them again after a rebuild.

## What it is made of
- **Body:** MPFB human (male, caucasian skin), default rig, short hair, face sliders tuned toward Stan's reference photos.
- **Face:** reshaped from the landmarks in `3d-model/photos/landmarks.json`, with the reference sheet `3d-model/photos/reference_sheet.webp` projected as texture.
- **Pose:** natural stance, arms swung down at the shoulders.
- **Kit:** Denison smock (fitted shell, skirt, collar), battledress trousers, gaiters, boots, 37-pattern webbing, beret.

## Files
| File | What |
|---|---|
| `3d-model/final/stan_airborne.blend` | Full character, current version |
| `3d-model/final/*.png` | Renders: front, side, back, 3/4, face front, face 3/4 |
| `3d-model/body/body.blend` | Body only |
| `3d-model/kit/airborne_kit.blend` | Kit only |
| `3d-model/soldier.blend` | Earlier soldier version |

Rebuild steps: [[Building the Model]]. Back to [[Home]].
