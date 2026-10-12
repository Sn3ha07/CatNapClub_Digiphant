#!/bin/sh
# Copies our changes to files that live in the ignored starter folders (DigiPhantStarter/ and
# Assets/Elephant/) into Restore/, so they are committed. Run it before committing whenever
# bridge.py, its tests or models, or the elephant materials change.
set -e
cd "$(dirname "$0")/.."
rm -rf Restore
mkdir -p Restore/DigiPhantStarter/Tracking Restore/Assets/Elephant/Materials Restore/Assets/Elephant/Textures

T=DigiPhantStarter/Tracking
cp DigiPhantStarter/controls.md Restore/DigiPhantStarter/
for f in bridge.py test_bridge.py test_reference_poses.py requirements.txt \
         gesture_recognizer.task efficientdet_lite0.tflite face_landmarker.task; do
    cp "$T/$f" Restore/DigiPhantStarter/Tracking/
done
# reference_poses/ (test photos) is deliberately not copied: photos are not shared.

E=Assets/Elephant
for f in Materials/M_Elephant.mat Materials/M_Elephant_Calf.mat Materials/M_Elephant_Calf.mat.meta \
         Textures/Elephant_D_Calf.png Textures/Elephant_D_Calf.png.meta \
         Textures/Elephant_D_Pink.png Textures/Elephant_D_Pink.png.meta; do
    cp "$E/$f" "Restore/$E/$f"
done
echo "Restore/ updated."
