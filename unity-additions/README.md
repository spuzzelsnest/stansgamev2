# Unity additions

Drop-in scripts for the Unity project (first-person view, hit feedback, health bar, settings, climbing over low obstacles, and AI that no longer gets stuck behind the gliders). They sit outside `Assets/` on purpose, so a project that pulls this repository does not start compiling them before someone has looked at them. They do not change any existing script, scene or prefab. Most of them use only the game's public API; `Mantle` and `AiUnstick` also read and set a few private fields of `SoldierController` and `SoldierAI` by name (`climbing`, `climbFrom`, `climbTo`, `climbT`, `vel`, `target`, `StartClimb`), so renaming those in the game scripts disables the climbing and unstick features quietly. Once the scripts are in `Assets/`, the cleaner way is to make those members `internal` or add small public methods for them.

## StansGame.Additions

| Script | What it does |
|---|---|
| `AdditionsBootstrap.cs` | Starts itself when the game loads (`RuntimeInitializeOnLoadMethod`) and adds the components below to the local player (`MissionDirector.Instance.player`), also after a respawn. |
| `PlayerView.cs` | First-person view: camera at the head bone (eyes), head hidden for the local player only, body turns with the view, near clip 0.05. **V** switches first / third person. Applies the field of view (hip and aimed) and mouse sensitivity settings in both views. |
| `HitFeedback.cs` | When the local player loses health: a hit sound (generated, no asset), a red flash at the screen edges, a short camera flinch, and a marker pointing to where the shot came from. Below 35 % health: heartbeat (faster as health drops) and a pulsing red edge. Detects damage from `Health.Current`, so it also works for co-op clients. |
| `HealthHud.cs` | Health bar bottom left: colour by health, a light segment for the damage just taken, numbers, and a key hint line with the player's own keyboard labels (ZQSD on AZERTY). |
| `SettingsPanel.cs` | **F10**: view, field of view, aim field of view, mouse sensitivity, hit effects, volume, heartbeat. Pauses the local player's controller while open. Saved in `PlayerPrefs` (`stans.*`). |
| `Mantle.cs` | Climbing over low obstacles (0.45 to 1.7 m: wreckage, crates, wire, low walls) for the local player: press **Space** against one, or keep walking into it for a moment, in any direction (relative to the camera, like the game's own movement). Uses the game's own climb movement (the canal bank one). Never climbs onto a glider. Also holds the shared `Obstacle` test. |
| `AiUnstick.cs` | Gets AI soldiers moving again when they walk into something (the AI has no pathfinding, so the glider sections stayed stuck behind the wreckage). After about a second without progress: climb a low obstacle; or, when advancing or following, plan a route out with `LocalPath` and lead the soldier along it; or sidestep when in a fight. After 8 s still in the same place: put the soldier on the nearest free ground towards where it is going. Only runs where the AI is simulated (offline or co-op host). |
| `FriendlyPassThrough.cs` | The local player and British soldiers no longer block each other's movement (`Physics.IgnoreCollision` between their CharacterControllers; shots are raycasts and still hit as before). A glider section of five could box the player in against the wreckage. |
| `LocalPath.cs` | Small grid pathfinder (0.7 m cells, 44 m square around the soldier) against the real colliders, at most one plan per frame. |
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

Second round (scripted play run, intro not skipped, keys and mouse simulated through the Input System on an AZERTY layout): the key labelled Z walked 6.7 m forward, strafe, mouse look, V, C, fire, reload and F10 all worked.

Glider exit: without these scripts, 4 of the 5 British soldiers of glider 1 never moved and the 5th stopped after 6.7 m: they spawn behind the glider and walk straight into invisible 1.1 to 1.4 m high boxes around the wreckage (`Environment/Details from the 1944 photos/Collider`). With `AiUnstick`, all 5 were out and in the fight within 40 s (7 to 50 m from the spawn) in both runs with the final version. The player, walking into the same wreckage, climbed over it and reached the canal bank 21 m on. A play-test build with a movement trace caught the player boxed in between the glider, the wreckage and two British soldiers (a strafe moved 0.1 m); with `FriendlyPassThrough` and climbing in every direction the same strafe moved 3.5 m and the trace logged no more jams.

Without the Unity source, the real fix belongs in the scene as well: a NavMesh for the AI, and section spawn points on the open side of each glider.

Not tested yet: a real play session with mouse and keyboard, co-op with two PCs, and every animation pose in first person (crouch, prone, swimming, climbing out of the canal). In first person the weapon is held where the third-person animation holds it; aiming down the sights would need a sight point per weapon.

![First person with the health bar](screenshots/first_person.png)

![Hit: red flash and direction marker](screenshots/hit.png)
