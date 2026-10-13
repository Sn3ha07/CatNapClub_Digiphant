# DIGIPHANT 64 — set up the CatNap Club elephant project on a new computer

**For the coding agent reading this:** follow these steps in order to rebuild the complete Unity
project on this computer, then verify it. The user only needs to grant camera access, open Unity and
move in front of the webcam for the live test. Report what you verified and what you could not.

## What this project is

A Unity 6 (URP) game. Three performers stand in front of one webcam, and together they steer a pink
elephant (with a calf that follows it) around a low-poly, Super Mario 64 style savannah course. A
Python MediaPipe "camera bridge" reads the webcam and sends each performer's body movements to Unity
over local UDP. The main scene is `Assets/StudentWork/Scenes/CatNapClub.unity`.

The project comes from two git repositories plus one folder:

| Part | Source | Goes to |
| --- | --- | --- |
| This project: scenes, DigiPhant scripts, game UI, low-poly course, hats, bananas | `https://github.com/Sn3ha07/CatNapClub_Digiphant` (private; the owner must add you as a collaborator) | the project root |
| Instructor starter: elephant model and animations, Python bridge, pose model | `https://github.com/kommanderpi/studentstarter`, commit `5adcbea1f7b311b8bb15c17b3dc4742ae8fff3af` | `DigiPhantStarter/`, plus a copy of `Elephant/` into `Assets/` |
| Our changes to starter files: the extended bridge, its tests and extra models, and the pink and calf elephant textures | `Restore/` in this repository | copied over the files above |

The music is **not** included: the game used copyrighted Nintendo tracks that may not be shared. The
game runs silently without them (see step 7).

## Requirements

- **Unity 6000.6.4f1**, installed through Unity Hub. Use this exact version, because the project was
  made with it. On macOS, the Apple silicon build is fine.
- **Git**, and access to the private repository above.
- **Python 3.11 or newer** (tested with Python 3.14 on macOS), with `venv`.
- A **webcam**. On macOS, Unity or the terminal will ask for camera permission.
- About 2 GB of free disk space (Unity's Library folder grows on first open).

## Steps

Commands are shown for macOS/Linux first, then Windows PowerShell. Run them from the folder where the
project should live.

### 1. Clone this project

```sh
git clone https://github.com/Sn3ha07/CatNapClub_Digiphant.git
cd CatNapClub_Digiphant
```

### 2. Clone the instructor starter at the recorded commit

The starter goes in a folder named exactly `DigiPhantStarter`, inside the project root (beside
`Assets`). Unity launches the bridge from `DigiPhantStarter/Tracking`.

```sh
git clone https://github.com/kommanderpi/studentstarter.git DigiPhantStarter
git -C DigiPhantStarter checkout 5adcbea1f7b311b8bb15c17b3dc4742ae8fff3af
```

### 3. Copy the elephant into Assets, keeping its .meta files

The scene finds the elephant's models, materials and animations through the GUIDs in these `.meta`
files, so copy the folder and its `.meta` file exactly. Don't let Unity create them.

```sh
cp -R DigiPhantStarter/Elephant Assets/Elephant
cp DigiPhantStarter/Elephant.meta Assets/Elephant.meta
```

```powershell
Copy-Item -Recurse DigiPhantStarter\Elephant Assets\Elephant
Copy-Item DigiPhantStarter\Elephant.meta Assets\Elephant.meta
```

Do **not** copy `DigiPhantStarter/DigiPhant` into `Assets`. `Assets/DigiPhant` in this repository
is a heavily extended version, and the starter's reference copy would overwrite it.

### 4. Apply our changes from Restore/

`Restore/` mirrors the project layout. Copy it over the project root and overwrite the existing files:

```sh
cp -R Restore/. .
```

```powershell
robocopy Restore . /E   # exit code 1 means files were copied; that is success
```

This copies:
- the extended `bridge.py` (zones for three people, gestures, banana and bottle detection, sleep pose, hands-up jump) and its tests;
- the gesture, object and face models;
- the pink elephant material and texture, and the calf's material and texture.

Check that `Assets/Elephant/Textures/Elephant_D_Pink.png` and
`DigiPhantStarter/Tracking/face_landmarker.task` now exist.

### 5. Create the Python environment for the camera bridge

Unity expects this exact path: `DigiPhantStarter/Tracking/.venv`.

```sh
cd DigiPhantStarter/Tracking
python3 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
.venv/bin/python -m unittest test_bridge test_reference_poses
cd ../..
```

```powershell
cd DigiPhantStarter\Tracking
py -m venv .venv
.venv\Scripts\python -m pip install -r requirements.txt
.venv\Scripts\python -m unittest test_bridge test_reference_poses
cd ..\..
```

All tests should pass. The reference-photo checks are skipped, because the test photos are not shared. If `mediapipe==0.10.35` has no wheel for the
installed Python version, install a Python version it supports and recreate the venv. Do not change
the pinned versions without telling the user.

Optional camera check: run `.venv/bin/python bridge.py --people 3` from `DigiPhantStarter/Tracking`.
A preview window should show three zones and skeletons. Press Q to quit. Close it before using Unity,
because Unity starts its own copy of the bridge and only one copy can hold the camera.

### 6. Open the project in Unity

1. In Unity Hub, choose **Add → Add project from disk** and select the `CatNapClub_Digiphant` folder.
   Open it with **6000.6.4f1**. The first import takes several minutes.
2. Open `Assets/StudentWork/Scenes/CatNapClub.unity`. It is also the first scene in Build Settings.
3. Check the Console. There should be no compile errors and no missing-script warnings. The elephant
   should be pink with green tusks, wearing a red cap, with a smaller calf in a green cap behind it.
   If the elephant is grey or magenta, step 3 or 4 was skipped. Close Unity, redo it, and reopen.
4. Press **Play**. The title screen reads **DIGIPHANT 64**.

### 7. Optional: add your music

No music ships with the project, and the game is silent until some is added. Only use tracks the user
owns or has permission to use.

In Unity, select the object with the **DigiPhant Controller** component in `CatNapClub.unity`. Its
Inspector has an **Add your music** section:

1. Click **Open music folder**. This creates `Assets/DigiPhant/Resources/Music/` if needed and opens it.
   Copy MP3, WAV or OGG files into it.
2. Drag each file onto its slot under **Add your music**:

| Slot | Plays | Used when the slot is empty |
| --- | --- | --- |
| Main Theme | looping during play | `Music/MainTheme` |
| Nap Music | while the elephant naps | `Music/Lullaby` |
| Course Clear Music | once on the Course Clear screen | `Music/CourseClear` |
| Calibration Music | during the 10-second calibration | `Music/StaffRoll` |

A file in the music folder with the name in the last column is used even without dragging it onto a
slot. An empty slot with no matching file stays silent; nothing else changes. Never commit music
files: `.gitignore` excludes the music folder.

## Playing

- **Keyboard mode** (no camera needed): W/S or ↑/↓ move, A/D or ←/→ turn, Shift runs, hold E to eat, hold Q to drink, Z naps or wakes, Space jumps, Esc pauses.
- **Camera mode:** three people stand side by side, one per zone. P1 is on the left of the unmirrored preview. Unity starts the bridge automatically and asks everyone to hold a neutral pose for calibration.

| Performer | Action | Elephant |
| --- | --- | --- |
| P1 (left zone) | raise left hand / right hand | walk forward (higher = run) / back up |
| P2 (middle zone) | raise right hand / left hand | turn right / turn left (the new direction stays) |
| P3 (right zone) | show a banana | eat; the calf walks to the feeding bush and eats there |
| P3 | show a water bottle | drink; the calf drinks at the water basin, waits 5 s, then runs to catch up |
| P3 | eyes closed + hands together by the face, or head resting sideways on the hands | nap for at least 5 s; open the eyes to wake |
| P3 | throw both hands up fast, into the top 30% of the image | jump (over the log) |

Missions: reach the finish, eat a banana, drink some water, take a nap. Coins and best time are shown
on the HUD. F1 toggles the developer panel.

## Verify and report

Tell the user which of these you checked:

1. All files from steps 2–4 are in place, and `git status` in the project root shows no changes.
   `DigiPhantStarter/`, `Assets/Elephant/` and `Library/` are ignored.
2. The Python tests pass.
3. Unity opens the scene with no compile errors or missing references.
4. Keyboard mode: the elephant moves, eats, drinks, naps and jumps, and the calf follows.
5. Camera mode (the user must be present): the webcam preview appears, and each performer's action from the table works.

## Troubleshooting

- **"Camera setup needed" in the game:** either `DigiPhantStarter/Tracking/.venv` or `pose_landmarker_full.task` is missing. Redo steps 2 and 5.
- **"Another camera bridge is already running":** close any bridge preview window (Q), or end stray `python bridge.py` processes.
- **The camera opens, then stops on macOS:** grant camera access to Unity (and to Terminal or VS Code) in System Settings → Privacy & Security → Camera, then restart Unity.
- **Sleep or jump is hard to trigger:** the bridge logs readings to `Temp/digiphant-sleep-log.csv`. Thresholds are constants near the top of `bridge.py` (`HEAD_TILT_SLEEP`, `HANDS_UP_TOP`).
- **Tracking code changed:** if you edit `bridge.py`, its tests, or the elephant materials, run `Tools/save_starter_changes.sh` before committing. Those files live in ignored folders, and the script copies them into `Restore/`.

## Rules for this repository

- Don't commit `DigiPhantStarter/`, `Assets/Elephant/`, `Library/`, recordings or music. `.gitignore` covers them.
- Don't push to the instructor's `studentstarter` repository.
- Publish changes by committing and pushing to `Sn3ha07/CatNapClub_Digiphant` on GitHub.
