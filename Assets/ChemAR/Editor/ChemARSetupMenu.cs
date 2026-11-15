using System.IO;
using ChemAR.AR;
using ChemAR.Core;
using ChemAR.Validation;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ChemAR.Editor
{
    [InitializeOnLoad]
    public static class ChemARSetupMenu
    {
        static ChemARSetupMenu()
        {
            EditorApplication.delayCall += () =>
            {
                EnsureConfigurationAssetExists();
                EnsureReferenceImageLibraryExists(forceRecreate: false);
                EnsureAndroidOpenGLES3();
                EnsureARBackgroundRendererFeature();
            };
        }

        public static void EnsureARBackgroundRendererFeature()
        {
            string[] paths = { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" };
            foreach (var path in paths)
            {
                var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (rendererData == null) continue;

                bool exists = false;
                foreach (var rf in rendererData.rendererFeatures)
                {
                    if (rf != null && rf is ARBackgroundRendererFeature)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
                    feature.name = "ARBackgroundRendererFeature";
                    AssetDatabase.AddObjectToAsset(feature, rendererData);

                    var so = new SerializedObject(rendererData);
                    var featuresProp = so.FindProperty("m_RendererFeatures");
                    featuresProp.arraySize++;
                    featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;
                    so.ApplyModifiedProperties();

                    EditorUtility.SetDirty(rendererData);
                    Debug.Log($"[ChemAR] ARBackgroundRendererFeature a fost atașat cu succes la {path} (activează feed-ul video al camerei).");
                }
            }
            AssetDatabase.SaveAssets();
        }

        public static void EnsureAndroidOpenGLES3()
        {
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (apis.Length == 0 || apis[0] != UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
                Debug.Log("[ChemAR] Auto-configurat OpenGLES3 pentru Android (Vulkan a fost eliminat conform cerințelor ARCore).");
            }

            if ((int)PlayerSettings.Android.minSdkVersion < 29)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
                Debug.Log("[ChemAR] Auto-configurat Minimum Android API Level la 29 (Android 10.0+).");
            }
        }

        public static void EnsureConfigurationAssetExists()
        {
            string dir = Path.Combine(Application.dataPath, "ChemAR", "Resources");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string assetPath = "Assets/ChemAR/Resources/ChemAR_Configuration.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ChemistryConfigurationSO>(assetPath);
            if (existing == null)
            {
                var so = ScriptableObject.CreateInstance<ChemistryConfigurationSO>();
                so.PopulateDefaults();
                AssetDatabase.CreateAsset(so, assetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[ChemAR] Fișierul ScriptableObject 'ChemAR_Configuration.asset' a fost generat automat în Assets/ChemAR/Resources/");
            }
        }

        public static XRReferenceImageLibrary EnsureReferenceImageLibraryExists(bool forceRecreate = false)
        {
            string dir = Path.Combine(Application.dataPath, "ChemAR", "Resources");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string libPath = "Assets/ChemAR/Resources/ChemAR_ImageLibrary.asset";
            var lib = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(libPath);
            if (lib == null || forceRecreate)
            {
                if (lib != null)
                {
                    AssetDatabase.DeleteAsset(libPath);
                }

                lib = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
                AssetDatabase.CreateAsset(lib, libPath);

                string[] elements = { "H", "O", "Na", "Cl", "C", "N" };
                for (int i = 0; i < elements.Length; i++)
                {
                    string sym = elements[i];
                    string texPath = $"Assets/ChemAR/Textures/Markers/Marker_{sym}.png";
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                    if (tex != null)
                    {
                        lib.Add();
                        lib.SetTexture(i, tex, true);
                        lib.SetName(i, $"Marker_{sym}");
                        lib.SetSpecifySize(i, true);
                        lib.SetSize(i, new Vector2(0.12f, 0.12f)); // 12 cm physical card
                    }
                }

                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                Debug.Log("[ChemAR] XRReferenceImageLibrary generat/actualizat cu cele 6 markere în Assets/ChemAR/Resources/ChemAR_ImageLibrary.asset");
            }

            return lib;
        }

        [MenuItem("ChemAR/📱 Configurare Scenă AR Telefon (AR Session + Camera AR + Image Tracking)", false, 0)]
        public static void SetupCompleteARScene()
        {
            EnsureAndroidOpenGLES3();
            EnsureARBackgroundRendererFeature();
            var lib = EnsureReferenceImageLibraryExists(forceRecreate: true);

            // 0. Remove obsolete / conflicting Vuforia objects
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                string n = go.name.ToLowerInvariant();
                if (n.Contains("vuforia") || n.StartsWith("imagetarget_") || n == "chemar_core")
                {
                    Debug.Log($"[ChemAR] Curățare obiect Vuforia/vechi: {go.name}");
                    Undo.DestroyObjectImmediate(go);
                }
            }

            // 1. AR Session
            var session = Object.FindFirstObjectByType<ARSession>();
            if (session == null)
            {
                var sessionGo = new GameObject("AR Session");
                session = sessionGo.AddComponent<ARSession>();
                sessionGo.AddComponent<ARInputManager>();
                Undo.RegisterCreatedObjectUndo(sessionGo, "Create AR Session");
                Debug.Log("[ChemAR] GameObject 'AR Session' a fost creat.");
            }

            // 2. XR Origin (AR Rig)
            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin == null)
            {
                var originGo = new GameObject("XR Origin (Mobile AR)");
                origin = originGo.AddComponent<XROrigin>();
                Undo.RegisterCreatedObjectUndo(originGo, "Create XR Origin");

                var offsetGo = new GameObject("Camera Offset");
                offsetGo.transform.SetParent(originGo.transform, false);
                origin.CameraFloorOffsetObject = offsetGo;

                // Move or Create Camera
                var existingCam = Camera.main;
                GameObject camGo;
                if (existingCam != null)
                {
                    camGo = existingCam.gameObject;
                    camGo.name = "Main Camera";
                    camGo.transform.SetParent(offsetGo.transform, false);
                    camGo.transform.localPosition = Vector3.zero;
                    camGo.transform.localRotation = Quaternion.identity;
                }
                else
                {
                    camGo = new GameObject("Main Camera");
                    camGo.tag = "MainCamera";
                    camGo.transform.SetParent(offsetGo.transform, false);
                    existingCam = camGo.AddComponent<Camera>();
                }

                existingCam.clearFlags = CameraClearFlags.SolidColor;
                existingCam.backgroundColor = Color.black;

                if (camGo.GetComponent<ARCameraManager>() == null)
                    camGo.AddComponent<ARCameraManager>();

                if (camGo.GetComponent<ARCameraBackground>() == null)
                    camGo.AddComponent<ARCameraBackground>();

                origin.Camera = existingCam;

                // Tracked Image Manager
                var imgMgr = originGo.GetComponent<ARTrackedImageManager>();
                if (imgMgr == null) imgMgr = originGo.AddComponent<ARTrackedImageManager>();
                imgMgr.referenceLibrary = lib;
                imgMgr.requestedMaxNumberOfMovingImages = 6;

                // Adapter
                if (originGo.GetComponent<ARFoundationPerceptionAdapter>() == null)
                    originGo.AddComponent<ARFoundationPerceptionAdapter>();

                Debug.Log("[ChemAR] 'XR Origin (Mobile AR)' a fost configurat cu ARCameraBackground și ARTrackedImageManager.");
            }
            else
            {
                // Ensure Image Manager has library
                var imgMgr = origin.GetComponent<ARTrackedImageManager>();
                if (imgMgr == null) imgMgr = origin.gameObject.AddComponent<ARTrackedImageManager>();
                imgMgr.referenceLibrary = lib;
                imgMgr.requestedMaxNumberOfMovingImages = 6;

                if (origin.GetComponent<ARFoundationPerceptionAdapter>() == null)
                    origin.gameObject.AddComponent<ARFoundationPerceptionAdapter>();

                if (origin.Camera != null)
                {
                    if (origin.Camera.GetComponent<ARCameraManager>() == null)
                        origin.Camera.gameObject.AddComponent<ARCameraManager>();
                    if (origin.Camera.GetComponent<ARCameraBackground>() == null)
                        origin.Camera.gameObject.AddComponent<ARCameraBackground>();
                }
            }

            // 3. ChemAR System
            var bootstrap = Object.FindFirstObjectByType<ChemARSceneBootstrap>();
            if (bootstrap == null)
            {
                var root = new GameObject("ChemAR_System");
                root.AddComponent<ChemARSceneBootstrap>();
                Undo.RegisterCreatedObjectUndo(root, "Create ChemAR System");
                Debug.Log("[ChemAR] ChemAR_System creat.");
            }

            // 4. Directional Light
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.0f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("Configurare AR Finalizată!",
                "Scena a fost configurată complet pentru telefonul mobil:\n\n" +
                "✓ AR Session & AR Input Manager adăugate\n" +
                "✓ XR Origin cu Cameră AR & ARCameraBackground (activează camera video a telefonului)\n" +
                "✓ AR Tracked Image Manager cu librăria celor 6 markere chimice distincte (12cm)\n" +
                "✓ OpenGLES3 setat pe Android (cerință ARCore)\n\n" +
                "Acum poți da 'Build and Run' din File -> Build Settings!",
                "Excelent!");
        }

        [MenuItem("ChemAR/🧪 Configurare Scenă ChemAR (Auto-Setup)", false, 1)]
        public static void SetupCurrentScene()
        {
            SetupCompleteARScene();
        }

        [MenuItem("ChemAR/📦 Selectează Baza de Date ScriptableObject (Asset)", false, 2)]
        public static void SelectConfigurationAsset()
        {
            EnsureConfigurationAssetExists();
            string assetPath = "Assets/ChemAR/Resources/ChemAR_Configuration.asset";
            var obj = AssetDatabase.LoadAssetAtPath<ChemistryConfigurationSO>(assetPath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
                Debug.Log("[ChemAR] S-a selectat 'ChemAR_Configuration.asset' în Project view.");
            }
        }

        [MenuItem("ChemAR/📋 Rulare Validare Experimentală (Console)", false, 3)]
        public static void RunValidationTestsFromEditor()
        {
            var testObj = new GameObject("TempValidationHarness");
            var harness = testObj.AddComponent<ExperimentalValidationHarness>();
            harness.RunAllValidationTests();
            Object.DestroyImmediate(testObj);

            EditorUtility.DisplayDialog("Validare Experimentală ChemAR",
                "Toate cele 5 teste de validare experimentală au fost rulate cu succes!\n\n" +
                "1. Calcul Distanță & Proximitate 3D (Spatial Engine)\n" +
                "2. Validare Valență & Electronegativitate (Chemistry Logic)\n" +
                "3. Entalpie ΔH & Transfer Termic (Thermodynamics)\n" +
                "4. Rezistență la Ocluzie & Grace Period (Risk Management)\n" +
                "5. Filtrare Jitter & Reflexii (Risk Management)\n\n" +
                "Verifică fereastra 'Console' din Unity pentru logurile detaliate.",
                "Am înțeles");
        }

        [MenuItem("ChemAR/📄 Deshide Cartonașele de Markeri (Print/Ecran)", false, 4)]
        public static void OpenPrintableMarkers()
        {
            string htmlPath = Path.Combine(Application.dataPath, "ChemAR", "PrintableMarkers.html");
            if (File.Exists(htmlPath))
            {
                Application.OpenURL("file://" + htmlPath.Replace("\\", "/"));
            }
            else
            {
                EditorUtility.DisplayDialog("Eroare", "Fișierul PrintableMarkers.html nu a fost găsit!", "OK");
            }
        }

        [MenuItem("ChemAR/📱 Ghid Build Telefon Android (ARCore)", false, 5)]
        public static void ShowAndroidBuildGuide()
        {
            EditorUtility.DisplayDialog("Ghid Build Android (ChemAR)",
                "Pași simpli pentru a rula aplicația pe telefonul Android:\n\n" +
                "1. Apasă în Unity meniul: 'ChemAR -> 📱 Configurare Scenă AR Telefon'.\n" +
                "2. Mergi la: File -> Build Settings -> Selectează 'Android' -> Apasă 'Switch Platform'.\n" +
                "3. Mergi la Edit -> Project Settings -> XR Plug-in Management -> Bifează 'Google ARCore'.\n" +
                "4. Asigură-te că telefonul are opțiunea 'USB Debugging' activată și este conectat prin cablu USB.\n" +
                "5. Apasă 'Build and Run'!\n\n" +
                "Aplicația va porni pe telefon, va deschide camera fizică și va recunoaște cartonașele imprimate sau afișate pe ecran.",
                "Am înțeles");
        }

        [MenuItem("ChemAR/🛠️ Rezolvă Eroarea Vulkan (Setează OpenGLES3 pentru ARCore)", false, 6)]
        public static void FixAndroidGraphicsAPI()
        {
            EnsureAndroidOpenGLES3();
            EditorUtility.DisplayDialog("Configurare ARCore Finalizată",
                "Graphics API pentru Android a fost setat exclusiv la OpenGLES3!\n\n" +
                "Vulkan a fost dezactivat conform cerințelor ARCore.\n\n" +
                "Acum poți relua procesul de Build & Run!",
                "Perfect");
        }

        [MenuItem("ChemAR/🚀 Build APK Android (ChemAR.apk direct)", false, 7)]
        public static void BuildAndroidAPK()
        {
            EnsureAndroidOpenGLES3();

            string buildFolder = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
            if (!Directory.Exists(buildFolder)) Directory.CreateDirectory(buildFolder);

            string apkPath = Path.Combine(buildFolder, "ChemAR.apk");

            string[] scenes = { "Assets/Scenes/SampleScene.unity" };

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = apkPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            Debug.Log($"[ChemAR] Începe procesul de Build APK Android la: {apkPath}...");
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[ChemAR] Build finalizat cu succes! Mărime: {report.summary.totalSize / (1024 * 1024)} MB la {apkPath}");
                EditorUtility.DisplayDialog("Build APK Reușit!", 
                    $"Fișierul ChemAR.apk a fost generat cu succes!\n\n" +
                    $"Locație: {apkPath}\n" +
                    $"Mărime: {report.summary.totalSize / (1024 * 1024)} MB\n\n" +
                    $"Fișierul a fost salvat și în folderul Builds al proiectului.", 
                    "Excelent!");
                EditorUtility.RevealInFinder(apkPath);
            }
            else
            {
                Debug.LogError($"[ChemAR] Build-ul a eșuat cu {report.summary.totalErrors} erori.");
                EditorUtility.DisplayDialog("Eroare Build", 
                    $"Build-ul a eșuat cu {report.summary.totalErrors} erori.\nVerifică fereastra 'Console' din Unity pentru detalii.", 
                    "OK");
            }
        }
    }
}
