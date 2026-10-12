using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DigiPhant.Editor
{
    // Puts three cartoon banana peels (upright banana with a face, three curled peel flaps) beside the
    // feeding bush of the active course, on the side facing the path. Decoration only.
    public static class DigiPhantBananas
    {
        const string AssetFolder = "Assets/DigiPhant/Bananas";
        const string GroupName = "Banana peels (3)";
        const float Height = .9f; // world height of one banana, in Unity units

        [MenuItem("DigiPhant/Add Bananas To Bush")]
        public static void AddToOpenScene()
        {
            if (Application.isPlaying) { Debug.LogWarning("Stop Play before adding bananas."); return; }
            var scene = SceneManager.GetActiveScene();
            var course = scene.GetRootGameObjects().Where(g => g.activeInHierarchy && g.name.StartsWith("Savannah Course"))
                .Select(g => g.transform).FirstOrDefault() ?? throw new InvalidOperationException("No active savannah course in this scene.");
            var bush = course.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "01 Feeding bush")
                ?? throw new InvalidOperationException("The course has no feeding bush.");
            foreach (var old in bush.Cast<Transform>().Where(t => t.name == GroupName).ToArray()) Undo.DestroyObjectImmediate(old.gameObject);

            Directory.CreateDirectory(AssetFolder);
            var body = MeshAsset("Banana body", Body);
            var flap = MeshAsset("Banana peel flap", Flap);
            var ball = MeshAsset("Banana ball", () => Ball(16, 10));
            var smile = MeshAsset("Banana smile", Smile);
            var peel = Material("Banana peel", new Color(1f, .84f, .12f), null, .35f);
            var skin = Material("Banana body", Color.white, Gradient(), .4f);
            var brown = Material("Banana tip", new Color(.33f, .17f, .07f), null, .2f);
            var black = Material("Banana eye", new Color(.04f, .04f, .05f), null, .8f);
            var white = Material("Banana eye shine", Color.white, null, .5f);
            var mouth = Material("Banana mouth", new Color(.55f, .38f, .05f), null, .2f);

            // Face the path: aim from the bush toward the nearest point of the path's centre line.
            Vector3 toPath = DirectionToPath(course, bush.position);
            var group = new GameObject(GroupName).transform;
            Undo.RegisterCreatedObjectUndo(group.gameObject, "Add bananas");
            group.SetParent(bush, false);
            float ground = course.position.y + .02f; // the low-poly grass sits 2 cm up
            float[] angles = { -38, 0, 34 }, distances = { 1.75f, 1.95f, 1.7f }, turns = { 25, -10, -40 }, sizes = { 1, 1.1f, .92f };
            for (int i = 0; i < 3; i++)
            {
                Vector3 dir = Quaternion.Euler(0, angles[i], 0) * toPath;
                var banana = new GameObject("Banana peel " + (i + 1)).transform;
                banana.SetParent(group, false);
                banana.position = new Vector3(bush.position.x, ground, bush.position.z) + dir * distances[i];
                // Face looks out toward the path, turned a little differently each time.
                banana.rotation = Quaternion.LookRotation(Quaternion.Euler(0, turns[i], 0) * dir, Vector3.up);
                banana.localScale = Vector3.one * Height * sizes[i] / bush.lossyScale.y;
                Build(banana, body, flap, ball, smile, peel, skin, brown, black, white, mouth);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"DIGIPHANT_BANANAS added 3 banana peels beside '{bush.name}' facing the path ({toPath})");
        }

        static Vector3 DirectionToPath(Transform course, Vector3 from)
        {
            var path = course.GetComponentsInChildren<MeshFilter>().FirstOrDefault(f => f.name == "Dirt path" || f.name == "Open path");
            if (!path) return Vector3.forward;
            var v = path.sharedMesh.vertices;
            var route = new List<Vector3>();
            for (int i = 0; i + 1 < v.Length; i += 2) route.Add(path.transform.TransformPoint((v[i] + v[i + 1]) / 2));
            Vector3 best = route[0]; float bestDistance = float.MaxValue;
            for (int i = 1; i < route.Count; i++)
            {
                Vector3 a = route[i - 1], b = route[i], ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(from - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                Vector3 p = a + ab * t;
                float d = Vector3.Distance(Flat(p), Flat(from));
                if (d < bestDistance) { bestDistance = d; best = p; }
            }
            Vector3 dir = Flat(best - from);
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        // One banana in unit space: 1 unit tall, face toward +z.
        static void Build(Transform banana, Mesh body, Mesh flap, Mesh ball, Mesh smile,
            Material peel, Material skin, Material brown, Material black, Material white, Material mouth)
        {
            Part(banana, "Body", body, skin, Vector3.zero, Quaternion.identity, Vector3.one);
            Part(banana, "Stem cap", ball, brown, new Vector3(0, .955f, 0), Quaternion.identity, new Vector3(.12f, .07f, .1f));
            for (int k = 0; k < 3; k++)
            {
                // Three flaps 120 degrees apart, one to each back side and one at the front-ish, like the reference.
                var rotation = Quaternion.Euler(0, 60 + k * 120, 0);
                Part(banana, "Peel flap", flap, peel, Vector3.zero, rotation, Vector3.one);
                Part(banana, "Peel tip", ball, brown, rotation * new Vector3(0, .17f, .9f), rotation, new Vector3(.1f, .09f, .1f));
            }
            // Face on the front of the body.
            foreach (float side in new[] { -1f, 1f })
            {
                Part(banana, "Eye", ball, black, new Vector3(side * .062f, .56f, .178f), Quaternion.identity, new Vector3(.055f, .085f, .03f));
                Part(banana, "Eye shine", ball, white, new Vector3(side * .062f + .012f, .58f, .193f), Quaternion.identity, new Vector3(.018f, .025f, .01f));
                Part(banana, "Cheek", ball, skin, new Vector3(side * .085f, .445f, .175f), Quaternion.identity, new Vector3(.07f, .06f, .04f));
            }
            Part(banana, "Smile", smile, mouth, new Vector3(0, .465f, .2f), Quaternion.identity, Vector3.one);
        }

        static void Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position; t.localRotation = rotation; t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        // ---------- Meshes ----------

        // Upright banana: a softly six-sided body, widest low down, narrowing to a neck under the stem.
        static Mesh Body()
        {
            float[] ys = { .08f, .18f, .32f, .5f, .64f, .74f, .82f, .88f, .93f, .96f };
            float[] rs = { .15f, .19f, .2f, .19f, .165f, .13f, .085f, .06f, .05f, .045f };
            const int around = 24;
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            for (int j = 0; j < ys.Length; j++)
                for (int i = 0; i <= around; i++)
                {
                    float a = i * Mathf.PI * 2 / around;
                    float ridge = 1 + .06f * Mathf.Cos(a * 6); // gentle banana ridges
                    vertices.Add(new Vector3(Mathf.Sin(a) * rs[j] * ridge, ys[j], Mathf.Cos(a) * rs[j] * ridge));
                    uvs.Add(new Vector2(i / (float)around, ys[j]));
                }
            for (int j = 0; j + 1 < ys.Length; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); // outward-facing
                }
            int bottom = vertices.Count; vertices.Add(new Vector3(0, ys[0], 0)); uvs.Add(new Vector2(.5f, 0));
            for (int i = 0; i < around; i++) triangles.AddRange(new[] { bottom, i + 1, i });
            return Finish(vertices, triangles, uvs);
        }

        // One peel flap in its own plane (+z outward): leaves the body low down, arches out and down to the
        // ground, and curls up slightly at the tip. It is a shallow trough, widest near the body.
        static Mesh Flap()
        {
            const int along = 18, across = 6;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int j = 0; j <= along; j++)
            {
                float t = j / (float)along;
                float z = .12f + .78f * t;
                float y = .3f * Mathf.Pow(1 - t, 1.5f) + .04f + .12f * t * t * t; // down to the ground, tip lifts
                float width = .17f * (1 - .75f * t) + .02f;
                for (int i = 0; i <= across; i++)
                {
                    float s = i / (float)across * 2 - 1;
                    vertices.Add(new Vector3(s * width, y + .05f * s * s * (1 - t * .5f), z - .03f * s * s));
                }
            }
            for (int j = 0; j < along; j++)
                for (int i = 0; i < across; i++)
                {
                    int a = j * (across + 1) + i, b = a + across + 1;
                    triangles.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            return Finish(vertices, triangles, null);
        }

        // A small smile: a thin curved tube.
        static Mesh Smile()
        {
            const int along = 12, around = 6;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int j = 0; j <= along; j++)
            {
                float a = Mathf.Lerp(-60, 60, j / (float)along) * Mathf.Deg2Rad;
                // Bends back with the body's curve so the whole smile sits on the surface.
                var centre = new Vector3(Mathf.Sin(a) * .06f, -Mathf.Cos(a) * .03f, -.06f * (1 - Mathf.Cos(a)));
                for (int i = 0; i <= around; i++)
                {
                    float b = i * Mathf.PI * 2 / around;
                    vertices.Add(centre + new Vector3(0, Mathf.Cos(b), Mathf.Sin(b)) * .011f);
                }
            }
            for (int j = 0; j < along; j++)
                for (int i = 0; i < around; i++)
                {
                    int a = j * (around + 1) + i, b = a + around + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            return Finish(vertices, triangles, null);
        }

        static Mesh Ball(int longitude, int latitude)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int lat = 0; lat <= latitude; lat++)
                for (int lon = 0; lon <= longitude; lon++)
                {
                    float theta = lat * Mathf.PI / latitude, phi = lon * Mathf.PI * 2 / longitude;
                    vertices.Add(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)) * .5f);
                }
            for (int lat = 0; lat < latitude; lat++)
                for (int lon = 0; lon < longitude; lon++)
                {
                    int a = lat * (longitude + 1) + lon, b = a + longitude + 1;
                    triangles.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            return Finish(vertices, triangles, null);
        }

        static Mesh Finish(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            var mesh = new Mesh(); mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            if (uvs != null) mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // ---------- Assets ----------

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
            material.SetFloat("_Cull", 0); // double-sided: the thin peel flaps show from above and below
            EditorUtility.SetDirty(material);
            return material;
        }

        // Yellow body turning lime green toward the stem, like the reference.
        static Texture2D Gradient()
        {
            string path = AssetFolder + "/Banana body gradient.png";
            var texture = new Texture2D(4, 64, TextureFormat.RGBA32, false);
            for (int y = 0; y < 64; y++)
            {
                float v = y / 63f;
                float t = Mathf.InverseLerp(.66f, .88f, v); // green only toward the stem
                var c = Color.Lerp(new Color(1f, .83f, .1f), new Color(.78f, .9f, .1f), t * t * (3 - 2 * t));
                for (int x = 0; x < 4; x++) texture.SetPixel(x, y, c);
            }
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Batch entry: -executeMethod DigiPhant.Editor.DigiPhantBananas.AddFromCommandLine -digiphantScene Assets/...unity
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
