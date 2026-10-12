using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DigiPhant.Editor
{
    public static class DigiPhantCalfSetup
    {
        const string CalfName = "Elephant Calf";
        const string PrefabPath = "Assets/Elephant/Prefabs/Elephant.prefab";
        const string TexturePath = "Assets/Elephant/Textures/Elephant_D_Calf.png";
        const string MaterialPath = "Assets/Elephant/Materials/M_Elephant_Calf.mat";

        [MenuItem("DigiPhant/Add Calf To Open Scene")]
        public static void AddToOpenScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before adding the calf."); return; }
            var leaders = UnityEngine.Object.FindObjectsByType<DigiPhantLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (leaders.Length != 1) throw new InvalidOperationException("Open a DigiPhant scene with exactly one locomotion component first.");
            var leader = leaders[0];
            if (!leader.travelRoot || !leader.elephantAnimator) throw new InvalidOperationException("Run DigiPhant/Add Locomotion To Open Scene first.");
            var existing = UnityEngine.Object.FindObjectsByType<DigiPhantCalf>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (existing.Length > 0) { Selection.activeGameObject = existing[0].gameObject; Debug.Log("DIGIPHANT_CALF_PRESENT " + existing[0].name); return; }

            var calf = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), leader.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(calf, "Add elephant calf");
            calf.name = CalfName;
            var adult = leader.elephantAnimator.transform;
            var follower = Undo.AddComponent<DigiPhantCalf>(calf);
            Vector3 forward = Vector3.ProjectOnPlane(leader.travelRoot.forward, Vector3.up).normalized;
            calf.transform.SetPositionAndRotation(adult.position - forward * follower.followDistance, adult.rotation);
            calf.transform.localScale = adult.lossyScale * .5f;
            // The vendor keyboard controller would otherwise drive the calf with W/A/S/D.
            foreach (var vendor in calf.GetComponentsInChildren<global::Elephant>(true)) vendor.enabled = false;
            var material = CalfMaterial(leader.elephantAnimator.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial);
            foreach (var renderer in calf.GetComponentsInChildren<SkinnedMeshRenderer>(true)) renderer.sharedMaterial = material;
            follower.leader = leader;
            follower.calfAnimator = calf.GetComponentInChildren<Animator>(true);
            follower.motionRoot = calf.GetComponentsInChildren<Transform>(true).Single(t => t.name == "elephant_Main_ctrl");
            EditorSceneManager.MarkSceneDirty(calf.scene);
            Selection.activeGameObject = calf;
            Debug.Log("DIGIPHANT_CALF_ADDED " + calf.scene.path + " | half size | no tusks | follows " + leader.travelRoot.name);
        }

        // Same pink skin as the adult; the tusk areas of the texture are transparent and clipped away.
        static Material CalfMaterial(Material adult)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material) return material;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (!texture) throw new FileNotFoundException("Missing calf texture", TexturePath);
            material = new Material(adult) { name = "M_Elephant_Calf" };
            material.SetTexture("_BaseMap", texture);
            material.SetTexture("_MainTex", texture);
            material.SetFloat("_AlphaClip", 1);
            material.SetFloat("_Cutoff", .5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        // Batch entry: -executeMethod DigiPhant.Editor.DigiPhantCalfSetup.AddFromCommandLine -digiphantScene Assets/...unity
        public static void AddFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-digiphantScene");
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Supply -digiphantScene Assets/path/to/Scene.unity");
            var scene = EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            AddToOpenScene();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
            AssetDatabase.SaveAssets();
        }
    }
}
