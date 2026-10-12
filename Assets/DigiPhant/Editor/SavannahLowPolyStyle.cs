using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace DigiPhant.Editor
{
    // Builds an N64-style (Super Mario 64 inspired) look of the savannah course in a copy of the open scene.
    // The original scene and its course are left untouched; in the copy the original course is only deactivated.
    public static class SavannahLowPolyStyle
    {
        const string SourceRoot = "Savannah Course [layout 4]";
        const string RootName = "Savannah Course Low-Poly (N64 style)";
        const string AssetFolder = "Assets/DigiPhant/LowPoly";
        const uint Seed = 20261005;
        static Shader lit;

        [MenuItem("DigiPhant/Savannah/Create Low-Poly (N64 Style) Scene Copy")]
        public static void CreateCopyOfOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var source = SceneManager.GetActiveScene();
            string sourcePath = source.path;
            if (string.IsNullOrEmpty(source.path)) throw new InvalidOperationException("Save the elephant scene first.");
            if (source.isDirty && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("Save scene changes first.");
            if (!source.GetRootGameObjects().Any(g => g.name == SourceRoot))
                throw new InvalidOperationException("This scene has no savannah course. Inject it first.");
            string copyPath = Path.ChangeExtension(source.path, null) + "_LowPoly.unity";
            // Regenerating replaces only the generated copy; the source scene is never written.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(copyPath)) AssetDatabase.DeleteAsset(copyPath);
            if (!AssetDatabase.CopyAsset(source.path, copyPath)) throw new IOException("Could not copy " + source.path);
            var scene = EditorSceneManager.OpenScene(copyPath, OpenSceneMode.Single);
            Build(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save " + copyPath);
            AssetDatabase.SaveAssets();
            Debug.Log("DIGIPHANT_LOWPOLY_CREATED " + copyPath + " | original course kept (inactive) | source scene unchanged: " + sourcePath);
        }

        static void Build(Scene scene)
        {
            lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) throw new InvalidOperationException("URP Lit shader not found.");
            Directory.CreateDirectory(AssetFolder);
            var original = scene.GetRootGameObjects().Single(g => g.name == SourceRoot).transform;
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName)) Object.DestroyImmediate(old);
            var root = new GameObject(RootName).transform;
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            root.SetPositionAndRotation(original.position, original.rotation);
            root.SetSiblingIndex(original.GetSiblingIndex() + 1);

            // Palette: saturated, flat colours with tiny tiled textures, as on the N64.
            var grass = TexturedMaterial("N64 Grass", Checker(32, new Color(.62f, .82f, .24f), new Color(.50f, .72f, .17f)));
            var dirt = TexturedMaterial("N64 Dirt Path", Pebbles(32, new Color(.88f, .66f, .38f), new Color(.74f, .52f, .29f), new Color(.97f, .80f, .52f)));
            var startCheck = TexturedMaterial("N64 Start Checker", Checker(16, Color.white, new Color(.08f, .08f, .08f)), new Vector2(7, 1));
            var finishCheck = TexturedMaterial("N64 Finish Checker", Checker(16, Color.white, new Color(.92f, .12f, .10f)), new Vector2(8, 1));
            var plains = FlatMaterial("N64 Plains", new Color(.56f, .74f, .26f));
            var trunk = FlatMaterial("N64 Trunk", new Color(.52f, .31f, .13f));
            var leafA = FlatMaterial("N64 Leaves", new Color(.18f, .62f, .16f));
            var leafB = FlatMaterial("N64 Leaves Light", new Color(.32f, .76f, .20f));
            var hill = FlatMaterial("N64 Hills", new Color(.36f, .70f, .22f));
            var hillDark = FlatMaterial("N64 Hills Dark", new Color(.27f, .58f, .17f));
            var mesa = FlatMaterial("N64 Mesa", new Color(.88f, .52f, .26f));
            var stone = FlatMaterial("N64 Stone", new Color(.70f, .70f, .76f));
            var water = FlatMaterial("N64 Water", new Color(.16f, .66f, 1f), .35f);
            var wood = FlatMaterial("N64 Fence Wood", new Color(.66f, .42f, .19f));
            var yellow = FlatMaterial("N64 Marker Yellow", new Color(1f, .84f, .10f), .25f);
            var gold = FlatMaterial("N64 Star Gold", new Color(1f, .82f, .05f), .6f);
            var red = FlatMaterial("N64 Red", new Color(.92f, .12f, .10f));
            var cloud = FlatMaterial("N64 Cloud", Color.white, .5f);

            var box = MeshAsset("N64 Box", BoxWithUv);
            var cylinder = MeshAsset("N64 Cylinder 6", () => Cylinder(6));
            var ball = MeshAsset("N64 Ball", () => Ball(7, 5));
            var cone = MeshAsset("N64 Cone 7", () => Cone(7));
            var star = MeshAsset("N64 Star", Star);

            // Ground and path follow the original layout exactly. Surfaces sit at the original ground's top (2 cm)
            // and above, so the scene's stage floor underneath never shows through.
            var ground = original.Find("Ground");
            float top = ground.localPosition.y + ground.localScale.y * .5f;
            Part(root, "Grass ground", MeshAsset("N64 Ground", () => Plane(60, 30, 2)), grass,
                new Vector3(0, top, 0), Vector3.one).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            Part(root, "Outer plains", MeshAsset("N64 Plains", () => Plane(400, 8, 400)), plains,
                new Vector3(0, top - .08f, 0), Vector3.one).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            var road = original.Find("Open path");
            Part(root, "Dirt path", MeshAsset("N64 Path", () => PlanarUv(road.GetComponent<MeshFilter>().sharedMesh, 3)), dirt,
                new Vector3(road.localPosition.x, top + .015f, road.localPosition.z), Vector3.one);
            var startLine = original.Find("Start line");
            Part(root, "Start line", box, startCheck, new Vector3(startLine.localPosition.x, top + .03f, startLine.localPosition.z), new Vector3(4.2f, .02f, .6f));

            var markers = Object.Instantiate(original.Find("Route markers").gameObject, root, false);
            markers.name = "Route markers";
            markers.transform.localPosition += Vector3.up * (top + .02f);
            foreach (var r in markers.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = yellow;
            foreach (var t in markers.GetComponentsInChildren<Transform>().Where(t => t.name == "Chevron"))
                t.localScale = new Vector3(.18f, .05f, .8f); // chunkier chevrons

            uint random = Seed;
            float Next() { random = unchecked(random * 1664525u + 1013904223u); return (random >> 8) / 16777216f; }

            // Round lollipop trees in the original tree spots.
            var grove = Group(root, "Trees (18)");
            foreach (Transform acacia in original.Find("Acacia trees (18)"))
            {
                var canopy = acacia.Cast<Transform>().First(c => c.name == "Flat canopy");
                float height = canopy.localPosition.y, width = canopy.localScale.x;
                var tree = Group(grove, acacia.name.Replace("Acacia", "Tree"));
                tree.localPosition = acacia.localPosition;
                tree.localRotation = acacia.localRotation;
                Part(tree, "Trunk", cylinder, trunk, new Vector3(0, height * .45f, 0), new Vector3(.55f, height * .9f, .55f));
                var leaves = Next() < .5f ? leafA : leafB;
                Part(tree, "Canopy", ball, leaves, new Vector3(0, height + .3f, 0), new Vector3(width * .95f, 2.2f, width * .95f));
                Part(tree, "Canopy top", ball, leaves == leafA ? leafB : leafA, new Vector3(.3f, height + 1.4f, -.2f), new Vector3(width * .55f, 1.6f, width * .55f));
            }

            // Landmarks keep their places, restyled.
            var originalLandmarks = original.Find("Landmarks (4, decorative)");
            var landmarks = Group(root, "Landmarks (4, decorative)");
            Transform Spot(string name)
            {
                var from = originalLandmarks.Find(name);
                var to = Group(landmarks, name);
                to.localPosition = from.localPosition; to.localRotation = from.localRotation;
                return to;
            }
            var bush = Spot("01 Feeding bush");
            Part(bush, "Bush", ball, leafA, new Vector3(0, .9f, 0), new Vector3(2.6f, 1.8f, 2.4f));
            Part(bush, "Bush top", ball, leafB, new Vector3(.4f, 1.6f, .1f), new Vector3(1.6f, 1.2f, 1.6f));
            var basin = Spot("02 Water basin");
            Part(basin, "Stone ring", cylinder, stone, new Vector3(0, .25f, 0), new Vector3(3.2f, .5f, 2.7f));
            Part(basin, "Water", cylinder, water, new Vector3(0, .51f, 0), new Vector3(2.7f, .03f, 2.2f));
            // A round log with pale cut ends and growth rings, so it reads as a log rather than a beam.
            var log = Spot("03 Fallen log");
            var round = MeshAsset("N64 Cylinder 10", () => Cylinder(10));
            var endWood = FlatMaterial("N64 Cut Wood", new Color(.95f, .78f, .50f));
            var ring = FlatMaterial("N64 Wood Ring", new Color(.72f, .48f, .22f));
            Part(log, "Log", round, trunk, new Vector3(0, .5f, 0), new Vector3(1f, 3.2f, 1f)).transform.localRotation = Quaternion.Euler(0, 0, 90);
            for (int side = -1; side <= 1; side += 2)
            {
                Part(log, "Cut end", round, endWood, new Vector3(side * 1.61f, .5f, 0), new Vector3(.9f, .03f, .9f)).transform.localRotation = Quaternion.Euler(0, 0, 90);
                Part(log, "Growth ring", round, ring, new Vector3(side * 1.63f, .5f, 0), new Vector3(.5f, .03f, .5f)).transform.localRotation = Quaternion.Euler(0, 0, 90);
                Part(log, "Heart", round, endWood, new Vector3(side * 1.645f, .5f, 0), new Vector3(.22f, .03f, .22f)).transform.localRotation = Quaternion.Euler(0, 0, 90);
            }
            Part(log, "Branch stub", round, trunk, new Vector3(.5f, 1.05f, .15f), new Vector3(.32f, .7f, .32f)).transform.localRotation = Quaternion.Euler(20, 0, -30);
            var finish = Spot("04 Finish marker");
            for (int side = -1; side <= 1; side += 2)
                Part(finish, "Pole", cylinder, red, new Vector3(side * 3.2f, 3f, 0), new Vector3(.35f, 6f, .35f));
            // High and wide enough for the elephant, its ears and its cap to pass underneath.
            Part(finish, "Checkered banner", box, finishCheck, new Vector3(0, 5.3f, 0), new Vector3(6.8f, .8f, .25f));
            var starPart = Part(finish, "Power star", star, gold, new Vector3(0, 6.8f, 0), new Vector3(1.8f, 1.8f, 1.8f));

            // A wooden fence on the stage edge, rolling hills, distant mesas and clouds.
            var fence = Group(root, "Fence");
            for (int edge = 0; edge < 4; edge++)
            {
                var side = Group(fence, "Side " + (edge + 1));
                side.localRotation = Quaternion.Euler(0, edge * 90, 0);
                for (float x = -30; x <= 30.01f; x += 3)
                    Part(side, "Post", box, wood, new Vector3(x, .6f, 30), new Vector3(.3f, 1.2f, .3f));
                Part(side, "Rail high", box, wood, new Vector3(0, .9f, 30), new Vector3(60, .16f, .12f));
                Part(side, "Rail low", box, wood, new Vector3(0, .45f, 30), new Vector3(60, .16f, .12f));
            }
            var hills = Group(root, "Rolling hills");
            for (int i = 0; i < 18; i++)
            {
                float angle = i / 18f * 360 + Next() * 12, distance = 38 + Next() * 10;
                float size = 12 + Next() * 8;
                var position = Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
                Part(hills, "Hill", ball, i % 2 == 0 ? hill : hillDark, position + Vector3.down * 1.5f,
                    new Vector3(size, 5 + Next() * 5, size * (.8f + Next() * .4f)));
            }
            var mesas = Group(root, "Distant mesas");
            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * 360 + 20 + Next() * 30, distance = 80 + Next() * 15;
                var position = Quaternion.Euler(0, angle, 0) * Vector3.forward * distance;
                // Broad, flat-topped buttes sitting on the outer plains.
                float height = 8 + Next() * 6;
                Part(mesas, "Mesa", cylinder, mesa, position + Vector3.up * height * .5f, new Vector3(30 + Next() * 14, height, 24 + Next() * 12));
            }
            var clouds = Group(root, "Clouds");
            for (int i = 0; i < 8; i++)
            {
                float angle = i / 8f * 360 + Next() * 25, distance = 35 + Next() * 40;
                var puff = Group(clouds, "Cloud " + (i + 1));
                puff.localPosition = Quaternion.Euler(0, angle, 0) * Vector3.forward * distance + Vector3.up * (22 + Next() * 10);
                for (int k = 0; k < 4; k++)
                    Part(puff, "Puff", ball, cloud, new Vector3((k - 1.5f) * 2.4f, (k % 2) * 1.0f, Next() * 1.5f),
                        Vector3.one * (3.2f + Next() * 1.6f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            // Bright sky and flat, even light, set on this scene copy only.
            var sky = AssetDatabase.LoadAssetAtPath<Material>(AssetFolder + "/N64 Sky.mat");
            if (!sky)
            {
                sky = new Material(Shader.Find("Skybox/Procedural")) { name = "N64 Sky" };
                AssetDatabase.CreateAsset(sky, AssetFolder + "/N64 Sky.mat");
            }
            sky.SetFloat("_SunDisk", 0);
            sky.SetFloat("_AtmosphereThickness", .55f);
            sky.SetColor("_SkyTint", new Color(.25f, .55f, 1f));
            sky.SetColor("_GroundColor", new Color(.55f, .75f, .95f));
            sky.SetFloat("_Exposure", 1.35f);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.72f, .74f, .80f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.70f, .85f, 1f);
            RenderSettings.fogStartDistance = 120;
            RenderSettings.fogEndDistance = 300;

            original.gameObject.SetActive(false);
        }

        static Transform Group(Transform parent, string name)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
        }

        static GameObject Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position, Vector3 scale)
        {
            var t = Group(parent, name); t.localPosition = position; t.localScale = scale;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            t.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return t.gameObject;
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

        static Material FlatMaterial(string name, Color color, float glow = 0) => MaterialAsset(name, color, null, glow);
        static Material TexturedMaterial(string name, Texture2D texture, Vector2? tiling = null)
        {
            var material = MaterialAsset(name, Color.white, texture, 0);
            material.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
            return material;
        }

        static Material MaterialAsset(string name, Color color, Texture2D texture, float glow)
        {
            string path = AssetFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(lit) { name = name }; AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Smoothness", 0);
            material.SetFloat("_SpecularHighlights", 0);
            material.SetFloat("_EnvironmentReflections", 0);
            material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
            if (glow > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * glow);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // Tiny textures with bilinear filtering give the soft N64 look.
        static Texture2D TextureAsset(string name, int size, Func<int, int, Color> pixel)
        {
            string path = AssetFolder + "/" + name + ".png";
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) texture.SetPixel(x, y, pixel(x, y));
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 64;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Checker(int size, Color a, Color b) =>
            TextureAsset("Checker " + ColorUtility.ToHtmlStringRGB(a) + ColorUtility.ToHtmlStringRGB(b), size,
                (x, y) => ((x < size / 2) ^ (y < size / 2)) ? a : b);

        static Texture2D Pebbles(int size, Color baseColor, Color dark, Color light)
        {
            uint random = Seed;
            var spots = new Dictionary<int, Color>();
            for (int i = 0; i < size * size / 9; i++)
            {
                random = unchecked(random * 1664525u + 1013904223u);
                spots[(int)(random >> 8) % (size * size)] = (random & 1) == 0 ? dark : light;
            }
            return TextureAsset("Pebbles", size, (x, y) => spots.TryGetValue(y * size + x, out var c) ? c : baseColor);
        }

        // ---------- Flat-shaded meshes (duplicated vertices keep faces crisp) ----------

        class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default)
            {
                int n = vertices.Count;
                vertices.AddRange(new[] { a, b, c }); uvs.AddRange(new[] { ua, ub, uc });
                triangles.AddRange(new[] { n, n + 1, n + 2 });
            }
            public Mesh Mesh()
            {
                var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Mesh Plane(float size, int cells, float tile)
        {
            var b = new Builder();
            float step = size / cells, half = size / 2;
            Vector2 Uv(Vector3 p) => new Vector2(p.x, p.z) / tile;
            for (int i = 0; i < cells; i++) for (int j = 0; j < cells; j++)
            {
                var p00 = new Vector3(-half + i * step, 0, -half + j * step);
                var p10 = p00 + new Vector3(step, 0, 0); var p01 = p00 + new Vector3(0, 0, step); var p11 = p00 + new Vector3(step, 0, step);
                b.Tri(p00, p01, p11, Uv(p00), Uv(p01), Uv(p11));
                b.Tri(p00, p11, p10, Uv(p00), Uv(p11), Uv(p10));
            }
            return b.Mesh();
        }

        static Mesh PlanarUv(Mesh source, float tile)
        {
            var mesh = Object.Instantiate(source);
            mesh.uv = mesh.vertices.Select(v => new Vector2(v.x, v.z) / tile).ToArray();
            return mesh;
        }

        static Mesh BoxWithUv()
        {
            var b = new Builder();
            void Face(Vector3 normal, Vector3 up)
            {
                Vector3 right = Vector3.Cross(up, normal), c = normal * .5f;
                Vector3 p0 = c - right * .5f - up * .5f, p1 = c + right * .5f - up * .5f, p2 = c + right * .5f + up * .5f, p3 = c - right * .5f + up * .5f;
                b.Tri(p0, p1, p2, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1));
                b.Tri(p0, p2, p3, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0, 1));
            }
            Face(Vector3.forward, Vector3.up); Face(Vector3.back, Vector3.up); Face(Vector3.left, Vector3.up);
            Face(Vector3.right, Vector3.up); Face(Vector3.up, Vector3.forward); Face(Vector3.down, Vector3.back);
            return b.Mesh();
        }

        static Mesh Cylinder(int sides)
        {
            var b = new Builder();
            Vector3 P(int s, float y) { float a = s * Mathf.PI * 2 / sides; return new Vector3(Mathf.Cos(a) * .5f, y, Mathf.Sin(a) * .5f); }
            for (int s = 0; s < sides; s++)
            {
                b.Tri(P(s, -.5f), P(s + 1, .5f), P(s + 1, -.5f));
                b.Tri(P(s, -.5f), P(s, .5f), P(s + 1, .5f));
                b.Tri(new Vector3(0, .5f, 0), P(s + 1, .5f), P(s, .5f));
                b.Tri(new Vector3(0, -.5f, 0), P(s, -.5f), P(s + 1, -.5f));
            }
            return b.Mesh();
        }

        static Mesh Cone(int sides)
        {
            var b = new Builder();
            Vector3 P(int s) { float a = s * Mathf.PI * 2 / sides; return new Vector3(Mathf.Cos(a) * .5f, 0, Mathf.Sin(a) * .5f); }
            for (int s = 0; s < sides; s++)
            {
                b.Tri(P(s), new Vector3(0, 1, 0), P(s + 1));
                b.Tri(Vector3.zero, P(s), P(s + 1));
            }
            return b.Mesh();
        }

        static Mesh Ball(int longitude, int latitude)
        {
            var b = new Builder();
            Vector3 P(int lon, int lat)
            {
                float theta = lat * Mathf.PI / latitude, phi = lon * Mathf.PI * 2 / longitude;
                return new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)) * .5f;
            }
            for (int lat = 0; lat < latitude; lat++) for (int lon = 0; lon < longitude; lon++)
            {
                Vector3 a = P(lon, lat), c = P(lon + 1, lat + 1), d = P(lon, lat + 1), e = P(lon + 1, lat);
                if (lat > 0) b.Tri(a, e, c);
                if (lat < latitude - 1) b.Tri(a, c, d);
            }
            return b.Mesh();
        }

        static Mesh Star()
        {
            var b = new Builder();
            const int points = 5; const float outer = .5f, inner = .22f, depth = .14f;
            Vector3 Rim(int i) { float r = i % 2 == 0 ? outer : inner, a = Mathf.PI / 2 + i * Mathf.PI / points; return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0); }
            var front = new Vector3(0, 0, -depth); var back = new Vector3(0, 0, depth);
            for (int i = 0; i < points * 2; i++)
            {
                b.Tri(front, Rim(i + 1), Rim(i));
                b.Tri(back, Rim(i), Rim(i + 1));
            }
            return b.Mesh();
        }

        // Batch entry: -executeMethod DigiPhant.Editor.SavannahLowPolyStyle.CreateFromCommandLine -digiphantScene Assets/...unity
        public static void CreateFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-digiphantScene");
            if (index < 0 || index + 1 == args.Length) throw new ArgumentException("Supply -digiphantScene Assets/path/to/Scene.unity");
            EditorSceneManager.OpenScene(args[index + 1], OpenSceneMode.Single);
            CreateCopyOfOpenScene();
        }
    }
}
