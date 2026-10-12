using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DigiPhant.Editor
{
    // Puts a red "M" cap on the big elephant and a green "L" cap on the calf, attached to their head bones.
    public static class DigiPhantHats
    {
        const string AssetFolder = "Assets/DigiPhant/Hats";
        const string HatName = "Cap";

        [MenuItem("DigiPhant/Add Hats To Open Scene")]
        public static void AddToOpenScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before adding hats."); return; }
            var leader = Object.FindObjectsByType<DigiPhantLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single();
            Directory.CreateDirectory(AssetFolder);
            var crown = MeshAsset("Cap crown", Crown);
            var brim = MeshAsset("Cap brim", Brim);
            var badge = MeshAsset("Cap badge", Badge);
            var white = Material("Cap badge rim", Color.white, null, .2f);
            Fit(leader.elephantAnimator, crown, brim, badge, Material("Cap red", new Color(.86f, .07f, .07f), null, .35f),
                Material("Cap badge M", Color.white, LetterTexture("M", new Color(.86f, .07f, .07f), MPolygon), .2f), white);
            foreach (var calf in Object.FindObjectsByType<DigiPhantCalf>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Fit(calf.calfAnimator, crown, brim, badge, Material("Cap green", new Color(.10f, .62f, .16f), null, .35f),
                    Material("Cap badge L", Color.white, LetterTexture("L", new Color(.10f, .62f, .16f), LPolygon), .2f), white);
            EditorSceneManager.MarkSceneDirty(leader.gameObject.scene);
            AssetDatabase.SaveAssets();
        }

        // Finds the top of the head from the skinned mesh and parents a cap there, facing the elephant's forward.
        static void Fit(Animator elephant, Mesh crownMesh, Mesh brimMesh, Mesh badgeMesh, Material cloth, Material badgeFace, Material rim)
        {
            var head = elephant.GetComponentsInChildren<Transform>(true).First(t => t.name == "elephant_Head_bone");
            foreach (var old in head.Cast<Transform>().Where(t => t.name == HatName).ToArray()) Undo.DestroyObjectImmediate(old.gameObject);
            var skin = elephant.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var baked = new Mesh();
            skin.BakeMesh(baked); // already carries the elephant's scale through its bones; apply only position and rotation
            var place = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
            var world = baked.vertices.Select(v => place.MultiplyPoint3x4(v)).ToArray();
            Object.DestroyImmediate(baked);
            Vector3 forward = Vector3.ProjectOnPlane(elephant.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float length = world.Max(p => Vector3.Dot(p, forward)) - world.Min(p => Vector3.Dot(p, forward));
            float size = length * .22f; // cap width relative to the elephant's body length
            // Top of the head: highest skin point near the head bone, close to the centre line.
            var near = world.Where(p =>
            {
                Vector3 d = p - head.position;
                return Mathf.Abs(Vector3.Dot(d, right)) < size * .35f && Mathf.Abs(Vector3.Dot(d, forward)) < size * .8f;
            }).ToArray();
            Vector3 top = near.Length > 0 ? near.OrderByDescending(p => p.y).First() : head.position + Vector3.up * size * .5f;
            var cap = new GameObject(HatName).transform;
            Undo.RegisterCreatedObjectUndo(cap.gameObject, "Add cap");
            cap.SetPositionAndRotation(top - Vector3.up * size * .12f, Quaternion.LookRotation(forward, Vector3.up));
            cap.localScale = Vector3.one * size;
            cap.SetParent(head, true);
            Part(cap, "Crown", crownMesh, cloth, Vector3.zero, Quaternion.identity, Vector3.one);
            Part(cap, "Brim", brimMesh, cloth, Vector3.zero, Quaternion.identity, Vector3.one);
            // Badge on the front of the crown, tilted to sit on the curve.
            var badgeRotation = Quaternion.Euler(72, 0, 0); // face forward and slightly up
            Part(cap, "Badge rim", badgeMesh, rim, new Vector3(0, .19f, .505f), badgeRotation, new Vector3(.3f, 1, .3f));
            Part(cap, "Badge", badgeMesh, badgeFace, new Vector3(0, .19f, .518f), badgeRotation, new Vector3(.26f, 1, .26f));
            Debug.Log($"DIGIPHANT_CAP {elephant.name}: size {size:0.00} at {top} on {head.name}");
        }

        static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position; t.localRotation = rotation; t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>(); r.sharedMaterial = material;
        }

        // ---------- Meshes (cap space: y up, +z toward the brim, unit width) ----------

        static Mesh Crown()
        {
            // A puffy dome, taller and rounder at the front like the reference cap.
            const int around = 24, up = 10;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int j = 0; j <= up; j++)
            {
                float phi = j / (float)up * Mathf.PI / 2; // 0 at the rim, 90 degrees at the top
                for (int i = 0; i <= around; i++)
                {
                    float theta = i / (float)around * Mathf.PI * 2;
                    float x = Mathf.Sin(theta) * Mathf.Cos(phi) * .5f, z = Mathf.Cos(theta) * Mathf.Cos(phi) * .56f;
                    float y = Mathf.Sin(phi) * .42f * (1 + .25f * Mathf.Max(0, z / .56f));
                    vertices.Add(new Vector3(x, y, z + .03f * Mathf.Sin(phi)));
                }
            }
            for (int j = 0; j < up; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); // outward-facing
                }
            return Finish(vertices, triangles, null);
        }

        static Mesh Brim()
        {
            // A curved visor in front of the crown, with a top, bottom and edge.
            const int steps = 16; const float thickness = .035f;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            Vector3 Point(float t, bool outer, bool lower)
            {
                float angle = Mathf.Lerp(-75, 75, t) * Mathf.Deg2Rad;
                float reach = outer ? .36f * Mathf.Pow(Mathf.Cos(angle), 1.5f) : 0; // longest at the front, none at the sides
                var rim = new Vector3(Mathf.Sin(angle) * .5f, 0, Mathf.Cos(angle) * .56f);
                var p = rim + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * reach;
                p.y = (outer ? -.06f * Mathf.Cos(angle) : 0) + (lower ? -thickness : 0) + .01f;
                return p;
            }
            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps, t1 = (i + 1) / (float)steps;
                void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
                {
                    int n = vertices.Count; vertices.AddRange(new[] { a, b, c, d });
                    triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
                }
                Quad(Point(t0, false, false), Point(t0, true, false), Point(t1, true, false), Point(t1, false, false));   // top
                Quad(Point(t0, false, true), Point(t1, false, true), Point(t1, true, true), Point(t0, true, true));      // bottom
                Quad(Point(t0, true, false), Point(t0, true, true), Point(t1, true, true), Point(t1, true, false));      // edge
            }
            return Finish(vertices, triangles, null);
        }

        static Mesh Badge()
        {
            // A thin disc in the XZ plane with UVs for the letter texture; +y is its face.
            const int sides = 32;
            var vertices = new List<Vector3> { Vector3.up * .02f }; var uvs = new List<Vector2> { new Vector2(.5f, .5f) };
            var triangles = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2;
                vertices.Add(new Vector3(Mathf.Cos(a) * .5f, .02f, Mathf.Sin(a) * .5f));
                // Once tilted onto the cap, local -z points up and -x is the viewer's right.
                uvs.Add(new Vector2(.5f - Mathf.Cos(a) * .5f, .5f - Mathf.Sin(a) * .5f));
            }
            for (int i = 1; i <= sides; i++) triangles.AddRange(new[] { 0, i + 1, i });
            return Finish(vertices, triangles, uvs);
        }

        static Mesh Finish(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh MeshAsset(string name, Func<Mesh> build)
        {
            string path = AssetFolder + "/" + name + ".asset";
            var mesh = build(); mesh.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); return existing; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Material Material(string name, Color color, Texture2D texture, float smoothness)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Cull", 0); // double-sided, so the open crown and thin brim show from any angle
            EditorUtility.SetDirty(material);
            return material;
        }

        // ---------- Badge letters ----------

        static readonly Vector2[] MPolygon = {
            new Vector2(-.56f, -.42f), new Vector2(-.56f, .44f), new Vector2(-.32f, .44f), new Vector2(0, .06f),
            new Vector2(.32f, .44f), new Vector2(.56f, .44f), new Vector2(.56f, -.42f), new Vector2(.34f, -.42f),
            new Vector2(.34f, .08f), new Vector2(.06f, -.24f), new Vector2(-.06f, -.24f), new Vector2(-.34f, .08f), new Vector2(-.34f, -.42f) };
        static readonly Vector2[] LPolygon = {
            new Vector2(-.34f, .46f), new Vector2(-.09f, .46f), new Vector2(-.09f, -.2f), new Vector2(.4f, -.2f),
            new Vector2(.4f, -.44f), new Vector2(-.34f, -.44f) };

        static Texture2D LetterTexture(string letter, Color ink, Vector2[] polygon)
        {
            string path = AssetFolder + "/Badge " + letter + ".png";
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Badge space: -1..1 across the disc; the letter fills most of it, like the reference cap.
                    var p = new Vector2(x + .5f, y + .5f) / size * 2 - Vector2.one;
                    bool inLetter = Inside(p / .8f, polygon);
                    texture.SetPixel(x, y, inLetter ? ink : (p.magnitude > .9f ? new Color(.82f, .82f, .84f) : Color.white));
                }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static bool Inside(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                if ((polygon[i].y > p.y) != (polygon[j].y > p.y) &&
                    p.x < (polygon[j].x - polygon[i].x) * (p.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x)
                    inside = !inside;
            return inside;
        }

        // Batch entry: -executeMethod DigiPhant.Editor.DigiPhantHats.AddFromCommandLine -digiphantScene Assets/...unity
        public static void AddFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-digiphantScene");
            var scene = EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            AddToOpenScene();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + scene.path);
        }
    }
}
