# DIGIPHANT 64 — CatNap Club

Three people, one webcam, one pink elephant. Each performer controls part of the elephant with their
body, tracked by Google's MediaPipe, and together they guide it (and its calf) around a Super Mario 64
style savannah course in Unity 6.

## Set it up

Follow **[DIGIPHANT_MARIO_SETUP.md](DIGIPHANT_MARIO_SETUP.md)**. Part 1 is for you: download the
programs and open this project in VS Code. Part 2 is for your coding agent (Claude Code, Codex or
another agent in VS Code), which builds the full Unity project for you.

You will need:

| Program | Download |
| --- | --- |
| VS Code | [code.visualstudio.com/download](https://code.visualstudio.com/download) |
| A coding agent: Claude Code or Codex | [Claude Code for VS Code](https://marketplace.visualstudio.com/items?itemName=anthropic.claude-code) · [Codex for VS Code](https://marketplace.visualstudio.com/items?itemName=openai.chatgpt) |
| Unity Hub + Unity 6000.6.4f1 | [Unity Hub](https://unity.com/download) · [editor archive](https://unity.com/releases/editor/archive) |
| Git | [git-scm.com/downloads](https://git-scm.com/downloads) |
| Python 3.11+ | [python.org/downloads](https://www.python.org/downloads/) |
| MediaPipe | installed by your agent during setup ([about MediaPipe](https://ai.google.dev/edge/mediapipe/solutions/guide)) |

Plus a webcam.

## Play

- **1 person:** use the keyboard. Click **PLAY WITH KEYBOARD**. W/S move, A/D turn, Shift runs, hold
  E to eat, hold Q to drink, Z naps, Space jumps, Esc pauses.
- **3 people:** use the camera (computer vision). Click **PLAY WITH CAMERA**, stand side by side, one
  person per third of the picture.

| Performer | Does | Elephant |
| --- | --- | --- |
| P1 (left) | raise left hand / right hand | walks forward (higher = run) / backs up |
| P2 (middle) | raise right hand / left hand | turns right / turns left |
| P3 (right) | hold up a banana | eats |
| P3 | hold up a water bottle | drinks |
| P3 | close eyes with hands together by the face, or rest the head sideways on the hands | naps; open eyes to wake |
| P3 | throw both hands up fast | jumps over the log |

**Print before playing with the camera:** a colour picture of a banana and one of a water bottle
(about A4 / Letter). Real ones work too.

Missions: reach the finish, eat a banana, drink some water, take a nap.

## Music

The game was designed around the Super Mario 64 soundtrack. **That music is copyrighted by Nintendo,
so it is not included or linked in this repository**, and your coding agent will point this out too.
The game runs silently without it. To add music you own or have permission to use, select the
**DigiPhant Controller** in Unity and use its **Add your music** section (setup guide, Part 2, step 7).

## Credits

- Elephant model and starter tracking code: instructor starter,
  [kommanderpi/studentstarter](https://github.com/kommanderpi/studentstarter).
- Tracking: [MediaPipe](https://ai.google.dev/edge/mediapipe/solutions/guide) pose, gesture, object
  and face models.
- Font: Luckiest Guy (Apache License 2.0, included in `Assets/DigiPhant/Resources`).
