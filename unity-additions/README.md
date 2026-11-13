# Unity additions

Drop-in scripts for the Unity project. They sit outside `Assets/` on purpose, so a project that pulls this repository does not start compiling them before someone has looked at them. They use the game's public API only and do not change any existing script, scene or prefab.

## StansGame.Additions

| Script | What it does |
|---|---|
| `AdditionsBootstrap.cs` | Starts itself when the game loads (`RuntimeInitializeOnLoadMethod`) and adds the components below to the local player (`MissionDirector.Instance.player`), also after a respawn. |
| `PlayerView.cs` | First-person view: camera at the head bone (eyes), head hidden for the local player only, body turns with the view, near clip 0.05. **V** switches first / third person. Applies the field of view (hip and aimed) and mouse sensitivity settings in both views. |
| `HitFeedback.cs` | When the local player loses health: a hit sound (generated, no asset), a red flash at the screen edges, a short camera flinch, and a marker pointing to where the shot came from. Below 35 % health: heartbeat (faster as health drops) and a pulsing red edge. Detects damage from `Health.Current`, so it also works for co-op clients. |
| `HealthHud.cs` | Health bar bottom left: colour by health, a light segment for the damage just taken, numbers, and a key hint line with the player's own keyboard labels (ZQSD on AZERTY). |
| `SettingsPanel.cs` | **F10**: view, field of view, aim field of view, mouse sensitivity, hit effects, volume, heartbeat. Pauses the local player's controller while open. Saved in `PlayerPrefs` (`stans.*`). |
| `GameSettings.cs` | The saved settings. |
| `KeyLabel.cs` | Key names in the active keyboard layout (`Keyboard.current[key].displayName`). |

### Install

1. Copy the `StansGame.Additions` folder into `Assets/` (for example `Assets/Scripts/StansGame.Additions/`).
2. Let Unity compile. Nothing else to set up: no prefab or scene changes.
3. Optional clean-ups in the existing scripts, now that the new HUD shows the same information:
   - remove the `"Health {0}    <size=13>{1}  (C kneel, Z prone)</size>"` line from `MissionDirector.OnGUI` (it shows the wrong key on AZERTY);
   - remove the hard-coded 60 / 45 field of view lerp at the end of `SoldierController.LateUpdate` (`PlayerView` overrides it every frame anyway).

To remove the additions again, delete the folder.

### Tested

There was no Unity source or editor on the testing machine, so the scripts were tested against the demo 0.1 build (Unity 6000.6.8f1):

- compiled without warnings against the build's own `Assembly-CSharp.dll` and Unity modules;
- added to a copy of the build and run in the real game with a test probe (not in this repository) that started "Play alone", waited for the landing, and checked: components attached to the local player; first-person camera 0.13 m from the head bone with the body facing the view; third person back at 2.7 m with the head shown; a 30 % hit gave the flash, the sound and a direction marker on the correct side; at 15 % health the heartbeat played; the F10 panel paused and released the controller. No exceptions in the log.

Not tested yet: a real play session with mouse and keyboard, co-op with two PCs, and every animation pose in first person (crouch, prone, swimming, climbing out of the canal). In first person the weapon is held where the third-person animation holds it; aiming down the sights would need a sight point per weapon.

![First person with the health bar](screenshots/first_person.png)

![Hit: red flash and direction marker](screenshots/hit.png)
