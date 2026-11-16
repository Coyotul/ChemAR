using ChemAR.Chemistry;
using ChemAR.Perception;
using ChemAR.Render;
using ChemAR.Spatial;
using ChemAR.UI;
using ChemAR.Validation;
using UnityEngine;

namespace ChemAR
{
    [DefaultExecutionOrder(-100)]
    public class ChemARSceneBootstrap : MonoBehaviour
    {
        [Header("Auto Bootstrap")]
        [SerializeField] private bool autoInitializeSystems = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrapExists()
        {
            if (FindFirstObjectByType<ChemARSceneBootstrap>() == null)
            {
                var root = new GameObject("ChemAR_System");
                root.AddComponent<ChemARSceneBootstrap>();
                Debug.Log("[ChemARSceneBootstrap] ChemAR_System a fost instanțiat automat la pornirea scenei.");
            }
        }

        private void Awake()
        {
            if (!autoInitializeSystems) return;

            // 1. Ensure Camera & Lighting (only if no Vuforia or AR camera exists)
            var cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera");
                cam = camGo.AddComponent<Camera>();
                cam.tag = "MainCamera";
                camGo.transform.position = new Vector3(0, 0.4f, 0f);
                camGo.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            }

            if (cam.GetComponent("VuforiaBehaviour") == null && cam.GetComponent("ARCameraManager") == null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            if (FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1.2f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // 2. Ensure Core Systems
            // In Vuforia mode, VuforiaReactionController and VuforiaTrackedCard handle perception and reactions.
            // Do not spin up AR Foundation / legacy perception & render managers to prevent duplicate reaction conflicts.
            bool isVuforiaMode = (cam != null && cam.GetComponent("VuforiaBehaviour") != null) ||
                                 GameObject.Find("ChemAR_Core")?.GetComponent("VuforiaReactionController") != null;

            if (!isVuforiaMode)
            {
                GetOrCreate<SpatialEngine>("SpatialEngine");
                GetOrCreate<ReactionResolver>("ReactionResolver");
                GetOrCreate<ChemARRenderManager>("ChemARRenderManager");
                GetOrCreate<ARPerceptionManager>("ARPerceptionManager");
            }

            GetOrCreate<ThermalVFXManager>("ThermalVFXManager");
            GetOrCreate<ChemARUIManager>("ChemARUIManager");
            GetOrCreate<ExperimentalValidationHarness>("ExperimentalValidationHarness");

            Debug.Log("[ChemARSceneBootstrap] Toate subsistemele ChemAR (Nivelurile 1-4, UI și Validare Experimentală) au fost inițializate.");
        }

        private T GetOrCreate<T>(string objectName) where T : Component
        {
            var existing = FindFirstObjectByType<T>();
            if (existing != null) return existing;

            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }
    }
}
