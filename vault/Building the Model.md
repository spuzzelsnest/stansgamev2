---
title: Building the Model
tags:
  - game
  - 3d-model
  - howto
---

# Building the Model

`3d-model/build_stan_airborne.py` builds [[Stan Airborne Model]] from scratch in Blender, saves `stan_airborne.blend` and renders six PNGs (1000 x 1300, Eevee, orthographic camera).

## Requirements
- Blender 5.3
- MPFB 2.0.18 extension installed (provides the human, rig, skin, hair, eyes, eyebrows and teeth)

## Run

```bash
blender --background --python 3d-model/build_stan_airborne.py -- <output-folder>
```

## Options (environment variables)
| Variable | Default | Effect |
|---|---|---|
| `HAIR` | `short04` | MPFB hair asset |
| `ARM_DROP` | `40` | How far the arms swing down |
| `ARM_FINAL` | `13` | Final arm angle off vertical, in degrees |
| `NOPHOTO` | unset | Set to `1` to skip the photo-based face reshape |

> [!warning] Hard-coded paths
> The script still points at paths on the machine it was written on:
> - `DATA`: the MPFB data folder under `C:\Users\32477\AppData\Roaming\Blender Foundation\Blender\5.3\...`
> - `PHOTO` and `LMFILE`: `C:\Projects\AIassistant\3d-model\photos\...`
>
> Change these to your own MPFB data folder and to `3d-model/photos/` in this repo before running. If the photo files are not found, the face step is skipped silently.

Back to [[Home]].
