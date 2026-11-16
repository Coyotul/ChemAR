#if CHEMAR_VUFORIA_PRESENT
using System.IO;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Render;
using ChemAR.UI;
using ChemAR.VuforiaAdapter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Vuforia;

namespace ChemAR.Vuforia.Editor
{
    [InitializeOnLoad]
    public static class VuforiaSceneSetup
    {
        private static readonly string[] ElementSymbols = { "H", "O", "Na", "Cl", "C", "N" };
        private const float CardPhysicalWidth = 0.12f; // 12 cm standard printed card width

        static VuforiaSceneSetup()
        {
            EditorApplication.delayCall += () =>
            {
                SetupVuforiaScene(showDialog: false);
            };
        }

        [MenuItem("ChemAR/🎯 Configurare Scenă Vuforia (AR Camera + 6 Image Targets)", false, 0)]
        public static void SetupVuforiaSceneMenuItem()
        {
            SetupVuforiaScene(showDialog: true);
        }

        public static void SetupVuforiaScene(bool showDialog = false)
        {
            var activeScene = EditorSceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Setup ChemAR Vuforia Scene");

            Debug.Log("<color=#00e676><b>[ChemAR] Începere configurare scenă Vuforia Engine...</b></color>");

            // 1. Remove / disable obsolete AR Foundation GameObjects to prevent camera conflict
            CleanupARFoundationObjects();

            // 2. Setup Vuforia ARCamera
            SetupVuforiaARCamera();

            // 3. Setup Directional Light
            SetupDirectionalLight();

            // 4. Configure Vuforia Engine Settings (Max Simultaneous Targets = 4)
            ConfigureVuforiaSettings();

            // 5. Setup Chemistry Core & UI Controllers
            SetupChemARCoreControllers();

            // 6. Setup 6 Image Targets with Marker Textures and Atom Visualizers
            SetupAllImageTargets();

            // 7. Save Scene
            EditorSceneManager.MarkSceneDirty(activeScene);
            EditorSceneManager.SaveScene(activeScene);

            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("<color=#00e676><b>[ChemAR] Scenă Vuforia configurată cu SUCCES! (6 Image Targets + AR Camera + Atom Visualizers + Reaction Controller)</b></color>");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("ChemAR - Vuforia Configurat",
                    "Scena a fost configurată complet pentru Vuforia Engine!\n\n" +
                    "• ARCamera cu VuforiaBehaviour este gata.\n" +
                    "• 6 Image Targets (H, O, Na, Cl, C, N) au fost create cu texturile corespunzătoare.\n" +
                    "• Atom Visualizer 3D integrat direct pe fiecare cartonaș.\n" +
                    "• Reaction Controller & Auto-Disociere configurate.\n\n" +
                    "Nu uitați să introduceți License Key-ul Vuforia (Window -> Vuforia Configuration) dacă este necesar.",
                    "Am înțeles");
            }
        }

        private static void CleanupARFoundationObjects()
        {
            // Remove old AR Foundation GameObjects & residual debug meshes
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allObjects)
            {
                if (go == null) continue;
                string n = go.name.ToLowerInvariant();
                if (n.Contains("ar session") || n.Contains("xr origin") || n.Contains("ar session origin") ||
                    n.Contains("ar camera manager") || n.StartsWith("simulatedcard") ||
                    n == "backingplate" || n == "accentbar" || n == "cardreticle" ||
                    n.StartsWith("atomvisualizer_") || n.StartsWith("atom_") || n.StartsWith("molecule_") ||
                    n.StartsWith("orbitalring") || n == "visualroot" || n == "billboardbadge" || n == "nucleus" ||
                    n.StartsWith("electron_"))
                {
                    Debug.Log($"[ChemAR] Curățare obiect vechi/rezidual: {go.name}");
                    Undo.DestroyObjectImmediate(go);
                }
            }
        }

        private static void SetupVuforiaARCamera()
        {
            // Check if Vuforia ARCamera already exists
            var existingCam = Object.FindFirstObjectByType<VuforiaBehaviour>();
            GameObject cameraGo;

            if (existingCam != null)
            {
                cameraGo = existingCam.gameObject;
            }
            else
            {
                // Delete standard non-Vuforia cameras tagged MainCamera
                var mainCam = Camera.main;
                if (mainCam != null && mainCam.GetComponent<VuforiaBehaviour>() == null)
                {
                    Undo.DestroyObjectImmediate(mainCam.gameObject);
                }

                cameraGo = new GameObject("ARCamera");
                Undo.RegisterCreatedObjectUndo(cameraGo, "Create ARCamera");
                cameraGo.tag = "MainCamera";

                var cam = cameraGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 20f;

                cameraGo.AddComponent<VuforiaBehaviour>();
                cameraGo.AddComponent<DefaultInitializationErrorHandler>();
                cameraGo.AddComponent<AudioListener>();
            }

            cameraGo.transform.position = Vector3.zero;
            cameraGo.transform.rotation = Quaternion.identity;
        }

        private static void SetupDirectionalLight()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            bool hasDirLight = false;
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    hasDirLight = true;
                    break;
                }
            }

            if (!hasDirLight)
            {
                var lightGo = new GameObject("Directional Light");
                Undo.RegisterCreatedObjectUndo(lightGo, "Create Directional Light");
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Directional;
                l.intensity = 1.2f;
                l.color = new Color(1f, 0.98f, 0.95f);
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private static void ConfigureVuforiaSettings()
        {
            try
            {
                var config = VuforiaConfiguration.Instance;
                if (config != null)
                {
                    config.Vuforia.MaxSimultaneousImageTargets = 4;
                    EditorUtility.SetDirty(config);
                    Debug.Log("[ChemAR] Vuforia Max Simultaneous Image Targets setat la 4.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ChemAR] Nu s-a putut seta automat MaxSimultaneousImageTargets: {ex.Message}");
            }
        }

        private static void SetupChemARCoreControllers()
        {
            var coreGo = GameObject.Find("ChemAR_Core");
            if (coreGo == null)
            {
                coreGo = new GameObject("ChemAR_Core");
                Undo.RegisterCreatedObjectUndo(coreGo, "Create ChemAR_Core");
            }

            // Ensure core components
            if (coreGo.GetComponent<ThermalVFXManager>() == null)
                coreGo.AddComponent<ThermalVFXManager>();

            if (coreGo.GetComponent<ChemARUIManager>() == null)
                coreGo.AddComponent<ChemARUIManager>();

            if (coreGo.GetComponent<VuforiaReactionController>() == null)
                coreGo.AddComponent<VuforiaReactionController>();

            if (coreGo.GetComponent<VuforiaPerceptionAdapter>() == null)
                coreGo.AddComponent<VuforiaPerceptionAdapter>();

            // Ensure EventSystem exists for UI button interaction
            var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                var esGo = new GameObject("EventSystem");
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputModuleType != null)
                {
                    esGo.AddComponent(inputModuleType);
                }
                else
                {
                    esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                }
            }
        }

        private static void SetupAllImageTargets()
        {
            for (int i = 0; i < ElementSymbols.Length; i++)
            {
                string symbol = ElementSymbols[i];
                try
                {
                    SetupSingleImageTarget(symbol, i);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[ChemAR] Eroare la crearea ImageTarget pentru {symbol}: {ex}");
                }
            }
        }

        private static void SetupSingleImageTarget(string symbol, int index)
        {
            string texturePath = $"Assets/ChemAR/Textures/Markers/Marker_{symbol}.png";
            EnsureTextureSettings(texturePath);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null)
            {
                Debug.LogError($"[ChemAR] Nu s-a găsit textura markerului la: {texturePath}");
                return;
            }

            string targetName = $"ImageTarget_{symbol}";
            var targetGo = GameObject.Find(targetName);
            if (targetGo == null)
            {
                targetGo = new GameObject(targetName);
                Undo.RegisterCreatedObjectUndo(targetGo, $"Create {targetName}");
            }

            // Remove any leaked procedural children under the ImageTarget
            for (int c = targetGo.transform.childCount - 1; c >= 0; c--)
            {
                var child = targetGo.transform.GetChild(c);
                string cn = child.name.ToLowerInvariant();
                if (cn.StartsWith("atom_") || cn == "cardreticle" || cn == "visualroot" ||
                    cn == "billboardbadge" || cn.StartsWith("orbitalring") || cn == "nucleus" || cn.StartsWith("electron_"))
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            // Space them out slightly in the editor scene view for preview
            targetGo.transform.position = new Vector3((index - 2.5f) * 0.35f, 0, 1.2f);
            targetGo.transform.rotation = Quaternion.Euler(90f, 0, 0);

            // Configure ImageTargetBehaviour
            var itb = targetGo.GetComponent<ImageTargetBehaviour>();
            if (itb == null) itb = targetGo.AddComponent<ImageTargetBehaviour>();
            itb.ImageTargetType = ImageTargetType.INSTANT;

            // Configure SerializedObject on ImageTargetBehaviour
            var so = new SerializedObject(itb);
            so.Update();

            var propType = so.FindProperty("mImageTargetType");
            if (propType != null) propType.intValue = (int)ImageTargetType.INSTANT; // 3 = From Image (INSTANT)

            var propTexture = so.FindProperty("mRuntimeTexture");
            if (propTexture == null) propTexture = so.FindProperty("mInstantTexture");
            if (propTexture != null)
            {
                propTexture.objectReferenceValue = texture;
                Debug.Log($"<color=#00e676>[ChemAR] Atribuit {propTexture.name} = {texture.name} pe {targetName}</color>");
            }
            else
            {
                Debug.LogWarning($"[ChemAR] Nicio proprietate de textură găsită pe {targetName}!");
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.name.ToLowerInvariant().Contains("textur"))
                        Debug.LogWarning($"   Posibilă proprietate: {it.name} ({it.propertyType})");
                }
            }

            var propInit = so.FindProperty("mInitializedInEditor");
            if (propInit != null) propInit.boolValue = true;

            var propWidth = so.FindProperty("mWidth");
            if (propWidth != null) propWidth.floatValue = CardPhysicalWidth;

            var propHeight = so.FindProperty("mHeight");
            if (propHeight != null) propHeight.floatValue = CardPhysicalWidth;

            var propName = so.FindProperty("mTargetName");
            if (propName != null) propName.stringValue = $"Marker_{symbol}";

            var propTrackableName = so.FindProperty("mTrackableName");
            if (propTrackableName != null) propTrackableName.stringValue = $"Marker_{symbol}";

            so.ApplyModifiedProperties();

            // Default Observer Event Handler (Tracked and Extended Tracked with Pose Smoothing)
            var eventHandler = targetGo.GetComponent<DefaultObserverEventHandler>();
            if (eventHandler == null) eventHandler = targetGo.AddComponent<DefaultObserverEventHandler>();
            eventHandler.StatusFilter = DefaultObserverEventHandler.TrackingStatusFilter.Tracked_ExtendedTracked;
            eventHandler.UsePoseSmoothing = true;

            // VuforiaTrackedCard
            var card = targetGo.GetComponent<VuforiaTrackedCard>();
            if (card == null) card = targetGo.AddComponent<VuforiaTrackedCard>();
            try
            {
                card.ConfigureElement(symbol, CardPhysicalWidth);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ChemAR] ConfigureElement pentru {symbol}: {ex.Message}");
            }

            // Register card with reaction controller
            var controller = Object.FindFirstObjectByType<VuforiaReactionController>();
            if (controller != null) controller.RegisterCard(card);

            Debug.Log($"[ChemAR] Configurat cu succes ImageTarget: {targetName} ({symbol}) cu textura {texture.name}");
        }

        private static void EnsureTextureSettings(string texturePath)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) return;

            bool modified = false;

            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                modified = true;
            }

            if (!importer.isReadable)
            {
                importer.isReadable = true;
                modified = true;
            }

            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                modified = true;
            }

            if (importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                modified = true;
            }

            if (modified)
            {
                importer.SaveAndReimport();
                Debug.Log($"[ChemAR] Import Settings actualizate pentru textura Vuforia: {texturePath}");
            }
        }
    }
}
#endif
