using System;
using System.Collections.Generic;
using ChemAR.Core;
using ChemAR.Spatial;
using UnityEngine;

namespace ChemAR.Perception
{
    public enum PerceptionMode
    {
        EditorSimulation,
        DeviceARFoundation
    }

    public class ARPerceptionManager : MonoBehaviour
    {
        public static ARPerceptionManager Instance { get; private set; }

        [Header("Perception Configuration")]
        [SerializeField] private PerceptionMode mode = PerceptionMode.EditorSimulation;
        [SerializeField] private SpatialEngine spatialEngine;
        
        [Header("Editor Simulation Setup")]
        [SerializeField] private bool autoSpawnSimulationCards = false;
        [SerializeField] private List<SimulatedMarkerCard> activeSimulatedCards = new();

        public PerceptionMode CurrentMode => mode;
        public SpatialEngine SpatialEngine => spatialEngine;

        private ITrackedMarkerSource externalARSource;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            if (spatialEngine == null)
            {
                spatialEngine = FindFirstObjectByType<SpatialEngine>();
            }

            if (Application.isMobilePlatform || !Application.isEditor)
            {
                mode = PerceptionMode.DeviceARFoundation;
                autoSpawnSimulationCards = false;
            }
        }

        private void Start()
        {
            if (Application.isMobilePlatform || !Application.isEditor)
            {
                mode = PerceptionMode.DeviceARFoundation;
                autoSpawnSimulationCards = false;
            }

            if (externalARSource == null)
            {
                var adapter = FindFirstObjectByType<MonoBehaviour>() as ITrackedMarkerSource;
                if (adapter == null)
                {
                    foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    {
                        if (mb is ITrackedMarkerSource source)
                        {
                            SetExternalARSource(source);
                            break;
                        }
                    }
                }
            }

            if (mode == PerceptionMode.EditorSimulation && autoSpawnSimulationCards)
            {
                SpawnDefaultSimulationScene();
            }
        }

        public void SetMode(PerceptionMode newMode)
        {
            this.mode = newMode;
            Debug.Log($"[ARPerceptionManager] Switched mode to: {newMode}");
        }

        public void SetExternalARSource(ITrackedMarkerSource source)
        {
            if (externalARSource != null)
            {
                externalARSource.OnMarkerAdded -= HandleExternalMarkerAdded;
                externalARSource.OnMarkerUpdated -= HandleExternalMarkerUpdated;
                externalARSource.OnMarkerLost -= HandleExternalMarkerLost;
            }

            externalARSource = source;

            if (externalARSource != null)
            {
                externalARSource.OnMarkerAdded += HandleExternalMarkerAdded;
                externalARSource.OnMarkerUpdated += HandleExternalMarkerUpdated;
                externalARSource.OnMarkerLost += HandleExternalMarkerLost;
            }
        }

        private void Update()
        {
            if (mode == PerceptionMode.EditorSimulation)
            {
                UpdateEditorSimulatedCards();
            }
        }

        private void UpdateEditorSimulatedCards()
        {
            if (spatialEngine == null) return;

            foreach (var card in activeSimulatedCards)
            {
                if (card == null) continue;

                if (card.isTracked && !card.simulateOcclusion)
                {
                    spatialEngine.RegisterOrUpdateMarker(
                        card.markerId,
                        card.elementSymbol,
                        card.CardPosition,
                        card.CardRotation,
                        MarkerTrackingQuality.HighQuality,
                        0.98f
                    );
                }
                else
                {
                    spatialEngine.NotifyMarkerLost(card.markerId);
                }
            }
        }

        public void SpawnDefaultSimulationScene()
        {
            // Spawn Hydrogen and Oxygen cards for water synthesis test
            SpawnSimulatedCard("Element_H1", "H", new Vector3(-0.25f, 0f, 0.6f));
            SpawnSimulatedCard("Element_O1", "O", new Vector3(0.25f, 0f, 0.6f));
        }

        public SimulatedMarkerCard SpawnSimulatedCard(string markerId, string symbol, Vector3 position)
        {
            var cardGo = new GameObject($"SimulatedCard_{symbol}_{markerId}");
            cardGo.transform.SetParent(transform, false);

            var card = cardGo.AddComponent<SimulatedMarkerCard>();
            Texture2D tex = LoadMarkerTexture(symbol);
            card.Initialize(markerId, symbol, tex, position);

            activeSimulatedCards.Add(card);
            return card;
        }

        public void ClearSimulationCards()
        {
            foreach (var card in activeSimulatedCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
            activeSimulatedCards.Clear();
            if (spatialEngine != null) spatialEngine.ClearAllMarkers();
        }

        private Texture2D LoadMarkerTexture(string symbol)
        {
            string path = $"Assets/ChemAR/Textures/Markers/Marker_{symbol}.png";
            #if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            #else
            return null;
            #endif
        }

        // Bridge callbacks for device AR
        public void RegisterDeviceMarker(string markerId, string symbol, Vector3 pos, Quaternion rot, MarkerTrackingQuality quality, float confidence)
        {
            if (spatialEngine != null)
            {
                spatialEngine.RegisterOrUpdateMarker(markerId, symbol, pos, rot, quality, confidence);
            }
        }

        public void NotifyDeviceMarkerLost(string markerId)
        {
            if (spatialEngine != null)
            {
                spatialEngine.NotifyMarkerLost(markerId);
            }
        }

        private void HandleExternalMarkerAdded(TrackedMarkerPose pose)
        {
            RegisterDeviceMarker(pose.markerId, pose.elementSymbol, pose.rawPosition, pose.rawRotation, pose.quality, pose.trackingConfidence);
        }

        private void HandleExternalMarkerUpdated(TrackedMarkerPose pose)
        {
            RegisterDeviceMarker(pose.markerId, pose.elementSymbol, pose.rawPosition, pose.rawRotation, pose.quality, pose.trackingConfidence);
        }

        private void HandleExternalMarkerLost(string markerId)
        {
            NotifyDeviceMarkerLost(markerId);
        }
    }
}
