using System.Collections.Generic;
using System.IO;
using Game.App;
using Game.Features.Items;
using Game.Features.Player;
using Game.Net.Scenes;
using Game.Net.Session;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Editor
{
    /// <summary>
    /// Generates project content (settings, materials, network prefabs, scenes) from code so the whole
    /// setup is reviewable and reproducible. Menu: Tools/Coop/..., or batch mode -executeMethod.
    /// </summary>
    public static class ProjectBuilder
    {
        private const string Root = "Assets/_Project";
        private const string PrefabsDir = Root + "/Prefabs";
        private const string ScenesDir = Root + "/Scenes";
        private const string ConfigDir = Root + "/Config";
        private const string MaterialsDir = Root + "/Materials";
        private const string BuildPath = "Builds/Windows/CoopPrototype.exe";

        [MenuItem("Tools/Coop/Generate Project Content")]
        public static void GenerateAll()
        {
            ConfigurePlayerSettings();
            EnsureFolders();

            Dictionary<string, Material> materials = CreateMaterials();
            NetworkObject sessionState = CreateSessionStatePrefab();
            NetworkObject player = CreatePlayerPrefab(materials["Player"], materials["Visor"]);
            NetworkObject crate = CreateItemPrefab("Crate", PrimitiveType.Cube, new Vector3(0.5f, 0.5f, 0.5f), materials["Crate"], 2f);
            NetworkObject ball = CreateItemPrefab("Ball", PrimitiveType.Sphere, new Vector3(0.45f, 0.45f, 0.45f), materials["Ball"], 1f);

            ItemCatalog catalog = CreateOrLoad<ItemCatalog>(ConfigDir + "/ItemCatalog.asset");
            var catalogObject = new SerializedObject(catalog);
            SerializedProperty items = catalogObject.FindProperty("_items");
            items.arraySize = 2;
            items.GetArrayElementAtIndex(0).objectReferenceValue = crate;
            items.GetArrayElementAtIndex(1).objectReferenceValue = ball;
            catalogObject.ApplyModifiedPropertiesWithoutUndo();

            AppConfig config = CreateOrLoad<AppConfig>(ConfigDir + "/AppConfig.asset");
            config.SessionStatePrefab = sessionState;
            config.PlayerPrefab = player;
            config.ItemCatalog = catalog;
            EditorUtility.SetDirty(config);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            CreateBootstrapScene(config);
            CreateMenuScene();
            CreateLobbyScene(materials);
            CreateGameScene(materials);
            RegisterScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateNetworkPrefabs(sessionState, player, crate, ball);
            Debug.Log("[ProjectBuilder] Content generated.");
        }

        [MenuItem("Tools/Coop/Build Windows")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[]
                {
                    ScenesDir + "/" + SceneNames.Bootstrap + ".unity",
                    ScenesDir + "/" + SceneNames.Menu + ".unity",
                    ScenesDir + "/" + SceneNames.Lobby + ".unity",
                    ScenesDir + "/" + SceneNames.Game + ".unity"
                },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new UnityEditor.Build.BuildFailedException($"Build failed: {report.summary.result}");
            }

            // Steamworks.NET reads the AppID from steam_appid.txt next to the executable.
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(BuildPath), "steam_appid.txt"), "480");
            Debug.Log($"[ProjectBuilder] Build succeeded: {BuildPath}");
        }

        public static void GenerateAndBuild()
        {
            GenerateAll();
            BuildWindows();
        }

        private static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "ArtDockTest";
            PlayerSettings.productName = "CoopPrototype";
            PlayerSettings.bundleVersion = "0.1.1";
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

            // New Input System only (activeInputHandler: 0 = old, 1 = new, 2 = both).
            Object projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
            var serialized = new SerializedObject(projectSettings);
            SerializedProperty inputHandler = serialized.FindProperty("activeInputHandler");
            if (inputHandler != null)
            {
                inputHandler.intValue = 1;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureFolders()
        {
            foreach (string dir in new[] { PrefabsDir, ScenesDir, ConfigDir, MaterialsDir })
            {
                Directory.CreateDirectory(dir);
            }

            AssetDatabase.Refresh();
        }

        private static Dictionary<string, Material> CreateMaterials()
        {
            var colors = new Dictionary<string, Color>
            {
                { "Floor", new Color(0.35f, 0.37f, 0.4f) },
                { "Wall", new Color(0.55f, 0.57f, 0.6f) },
                { "Ramp", new Color(0.45f, 0.5f, 0.42f) },
                { "Crate", new Color(0.85f, 0.55f, 0.25f) },
                { "Ball", new Color(0.25f, 0.75f, 0.85f) },
                { "Player", Color.white },
                { "Visor", new Color(0.1f, 0.1f, 0.12f) }
            };

            var result = new Dictionary<string, Material>();
            Shader shader = Shader.Find("Standard");
            foreach (KeyValuePair<string, Color> pair in colors)
            {
                string path = $"{MaterialsDir}/{pair.Key}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }

                material.color = pair.Value;
                EditorUtility.SetDirty(material);
                result[pair.Key] = material;
            }

            return result;
        }

        private static NetworkObject CreateSessionStatePrefab()
        {
            var root = new GameObject("SessionState");
            var networkObject = root.AddComponent<NetworkObject>();
            networkObject.DontDestroyWithOwner = true;
            root.AddComponent<SessionNetworkState>();
            return SavePrefab(root, "SessionState");
        }

        private static NetworkObject CreatePlayerPrefab(Material bodyMaterial, Material visorMaterial)
        {
            var root = new GameObject("Player");
            root.AddComponent<NetworkObject>();

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.35f;

            // Owner-authoritative movement: only position + yaw are replicated (pitch is local to the camera).
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncRotAngleX = false;
            networkTransform.SyncRotAngleZ = false;
            networkTransform.SyncScaleX = networkTransform.SyncScaleY = networkTransform.SyncScaleZ = false;
            networkTransform.UseHalfFloatPrecision = true;
            networkTransform.UseUnreliableDeltas = true;
            networkTransform.Interpolate = true;
            networkTransform.PositionThreshold = 0.01f;
            networkTransform.RotAngleThreshold = 0.5f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            var head = new GameObject("Head").transform;
            head.SetParent(root.transform, false);
            head.localPosition = new Vector3(0f, 1.6f, 0f);

            GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            Object.DestroyImmediate(visor.GetComponent<Collider>());
            visor.transform.SetParent(head, false);
            visor.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            visor.transform.localScale = new Vector3(0.5f, 0.15f, 0.1f);
            visor.GetComponent<Renderer>().sharedMaterial = visorMaterial;

            var label = new GameObject("NameLabel");
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var textMesh = label.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textMesh.font = font;
            textMesh.characterSize = 0.05f;
            textMesh.fontSize = 48;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            label.GetComponent<MeshRenderer>().sharedMaterial = font.material;

            var character = root.AddComponent<PlayerCharacter>();
            SetField(character, "_head", head);

            var visual = root.AddComponent<PlayerVisual>();
            SetArray(visual, "_renderers", body.GetComponent<Renderer>());
            SetField(visual, "_nameLabel", textMesh);

            root.AddComponent<PlayerItemInteractor>();
            return SavePrefab(root, "Player");
        }

        private static NetworkObject CreateItemPrefab(string name, PrimitiveType shape, Vector3 scale, Material material, float mass)
        {
            GameObject root = GameObject.CreatePrimitive(shape);
            root.name = name;
            root.transform.localScale = scale;
            root.GetComponent<Renderer>().sharedMaterial = material;

            var networkObject = root.AddComponent<NetworkObject>();
            // Items must survive their holder's disconnect.
            networkObject.DontDestroyWithOwner = true;

            var body = root.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Owner authority: the host while free, the holder while held.
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncScaleX = networkTransform.SyncScaleY = networkTransform.SyncScaleZ = false;
            networkTransform.UseHalfFloatPrecision = true;
            networkTransform.UseUnreliableDeltas = true;
            networkTransform.Interpolate = true;
            networkTransform.PositionThreshold = 0.005f;
            networkTransform.RotAngleThreshold = 1f;

            root.AddComponent<NetworkItem>();
            return SavePrefab(root, name);
        }

        private static NetworkObject SavePrefab(GameObject root, string name)
        {
            string path = $"{PrefabsDir}/{name}.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<NetworkObject>();
        }

        private static void CreateBootstrapScene(AppConfig config)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var scopeObject = new GameObject("RootLifetimeScope");
            var scope = scopeObject.AddComponent<RootLifetimeScope>();
            CreateCamera(new Vector3(0f, 1f, -10f), Quaternion.identity);
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{SceneNames.Bootstrap}.unity");

            // Assign the config after the scene has a path, then save again (assigning it to an object of a
            // never-saved scene did not persist in batch mode).
            SetField(scope, "_config", config);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (new SerializedObject(scope).FindProperty("_config").objectReferenceValue == null)
            {
                Debug.LogError("[ProjectBuilder] AppConfig is not assigned in the Bootstrap scene.");
            }
        }

        private static void CreateMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(new Vector3(0f, 1f, -10f), Quaternion.identity);
            CreateLight();
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{SceneNames.Menu}.unity");
        }

        private static void CreateLobbyScene(Dictionary<string, Material> materials)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(new Vector3(0f, 6f, -12f), Quaternion.Euler(20f, 0f, 0f));
            CreateLight();
            CreateBlock("Platform", new Vector3(0f, -0.5f, 0f), new Vector3(12f, 1f, 12f), materials["Floor"]);
            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{SceneNames.Lobby}.unity");
        }

        private static void CreateGameScene(Dictionary<string, Material> materials)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateCamera(new Vector3(0f, 18f, -22f), Quaternion.Euler(40f, 0f, 0f));
            CreateLight();

            var level = new GameObject("Level").transform;
            CreateBlock("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(40f, 1f, 40f), materials["Floor"], level);
            CreateBlock("WallN", new Vector3(0f, 2f, 20f), new Vector3(40f, 4f, 1f), materials["Wall"], level);
            CreateBlock("WallS", new Vector3(0f, 2f, -20f), new Vector3(40f, 4f, 1f), materials["Wall"], level);
            CreateBlock("WallE", new Vector3(20f, 2f, 0f), new Vector3(1f, 4f, 40f), materials["Wall"], level);
            CreateBlock("WallW", new Vector3(-20f, 2f, 0f), new Vector3(1f, 4f, 40f), materials["Wall"], level);
            CreateBlock("Platform", new Vector3(10f, 1f, 10f), new Vector3(8f, 2f, 8f), materials["Wall"], level);
            GameObject ramp = CreateBlock("Ramp", new Vector3(10f, 0.9f, 3.2f), new Vector3(4f, 0.4f, 7f), materials["Ramp"], level);
            ramp.transform.rotation = Quaternion.Euler(-16f, 0f, 0f);
            CreateBlock("Pillar1", new Vector3(-8f, 1.5f, 6f), new Vector3(2f, 3f, 2f), materials["Wall"], level);
            CreateBlock("Pillar2", new Vector3(-6f, 1f, -8f), new Vector3(3f, 2f, 1f), materials["Wall"], level);
            GameObject slope = CreateBlock("Slope", new Vector3(-10f, 0.5f, -2f), new Vector3(6f, 0.3f, 6f), materials["Ramp"], level);
            slope.transform.rotation = Quaternion.Euler(0f, 0f, 12f);

            EditorSceneManager.SaveScene(scene, $"{ScenesDir}/{SceneNames.Game}.unity");
        }

        private static void RegisterScenes()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{ScenesDir}/{SceneNames.Bootstrap}.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/{SceneNames.Menu}.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/{SceneNames.Lobby}.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/{SceneNames.Game}.unity", true)
            };
        }

        private static void CreateCamera(Vector3 position, Quaternion rotation)
        {
            var cameraObject = new GameObject("SceneCamera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.14f, 0.18f);
            cameraObject.transform.SetPositionAndRotation(position, rotation);
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Sun");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject CreateBlock(string name, Vector3 position, Vector3 size, Material material, Transform parent = null)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = position;
            block.transform.localScale = size;
            block.isStatic = true;
            block.GetComponent<Renderer>().sharedMaterial = material;
            return block;
        }

        private static T CreateOrLoad<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            return asset;
        }

        private static void SetField(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, params Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Netcode identifies prefabs by GlobalObjectIdHash; make sure each one got a unique hash.</summary>
        private static void ValidateNetworkPrefabs(params NetworkObject[] prefabs)
        {
            var seen = new HashSet<uint>();
            foreach (NetworkObject prefab in prefabs)
            {
                uint hash = prefab.PrefabIdHash;
                if (hash == 0 || !seen.Add(hash))
                {
                    Debug.LogError($"[ProjectBuilder] Prefab {prefab.name} has invalid GlobalObjectIdHash {hash}.");
                }
                else
                {
                    Debug.Log($"[ProjectBuilder] {prefab.name}: GlobalObjectIdHash {hash}");
                }
            }
        }
    }
}
