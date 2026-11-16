using System;
using System.Collections.Generic;
using ChemAR.Core;
using UnityEngine;

namespace ChemAR.Spatial
{
    [Serializable]
    public class MarkerSpatialPair
    {
        public TrackedMarkerPose markerA;
        public TrackedMarkerPose markerB;
        public float euclideanDistance;
        public Vector3 relativeDirection; // A to B
        public float normalAlignment;    // dot product of normals
        public float approachSpeed;      // m/s
        public bool isWithinReactionThreshold;
    }

    public class SpatialEngine : MonoBehaviour
    {
        [Header("Distance & Spatial Thresholds")]
        [Tooltip("Max Euclidean distance (meters) to trigger a chemical reaction between markers")]
        [SerializeField] private float reactionDistanceThreshold = 0.16f; // 16 cm (optimal for two 12cm cards side-by-side)

        [Tooltip("Min Euclidean distance to reject co-located duplicate/ghost detections")]
        [SerializeField] private float minReactionDistanceThreshold = 0.035f; // 3.5 cm
        
        [Tooltip("Distance at which visual proximity warnings/attraction lines begin")]
        [SerializeField] private float proximityNoticeThreshold = 0.30f; // 30 cm
        
        [Tooltip("Minimum normal alignment (-1 to 1) required if strict orientation is checked")]
        [SerializeField] private float minNormalAlignment = -0.5f;

        [Header("Smoothing & Risk Management")]
        [SerializeField] private bool enableJitterFilter = true;
        [SerializeField] private bool enableOcclusionRecovery = true;
        [SerializeField] private float occlusionGracePeriod = 2.0f;

        public float ReactionDistanceThreshold
        {
            get => reactionDistanceThreshold;
            set => reactionDistanceThreshold = Mathf.Max(0.05f, value);
        }

        public float ProximityNoticeThreshold => proximityNoticeThreshold;

        // Events
        public event Action<MarkerSpatialPair> OnProximityEntered;
        public event Action<MarkerSpatialPair> OnProximityExited;
        public event Action<string, bool> OnMarkerOcclusionChanged;

        private readonly Dictionary<string, TrackedMarkerPose> activeMarkers = new();
        private readonly Dictionary<string, MarkerPoseFilter> filters = new();
        private readonly Dictionary<(string, string), float> previousDistances = new();
        private readonly List<MarkerSpatialPair> currentPairs = new();
        private readonly HashSet<(string, string)> activeProximityPairs = new();

        private OcclusionHandler occlusionHandler;

        public IReadOnlyList<MarkerSpatialPair> CurrentPairs => currentPairs;
        public IReadOnlyCollection<TrackedMarkerPose> ActiveMarkers => activeMarkers.Values;

        private void Awake()
        {
            occlusionHandler = new OcclusionHandler
            {
                GracePeriodSeconds = occlusionGracePeriod,
                ProximityOcclusionThreshold = proximityNoticeThreshold
            };
            occlusionHandler.OnMarkerOcclusionStateChanged += (id, occluded) => 
                OnMarkerOcclusionChanged?.Invoke(id, occluded);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            if (enableOcclusionRecovery)
            {
                var markerList = new List<TrackedMarkerPose>(activeMarkers.Values);
                occlusionHandler.Update(dt, markerList, OnGhostExpired);
            }

            ComputeSpatialRelations(dt);
        }

        public void RegisterOrUpdateMarker(string markerId, string elementSymbol, Vector3 pos, Quaternion rot, 
            MarkerTrackingQuality quality, float confidence)
        {
            bool isNew = !activeMarkers.TryGetValue(markerId, out var marker);
            if (isNew)
            {
                marker = new TrackedMarkerPose(markerId, elementSymbol);
                marker.filteredPosition = pos;
                marker.filteredRotation = rot;
                activeMarkers[markerId] = marker;
                filters[markerId] = new MarkerPoseFilter();
            }

            // Apply filter only after first frame
            if (!isNew && enableJitterFilter && (marker.isTracked || quality != MarkerTrackingQuality.NotTracked))
            {
                marker.filteredPosition = filters[markerId].FilterPosition(marker.filteredPosition, pos, Time.deltaTime);
                marker.filteredRotation = filters[markerId].FilterRotation(marker.filteredRotation, rot, Time.deltaTime);
            }
            else if (isNew)
            {
                marker.filteredPosition = pos;
                marker.filteredRotation = rot;
            }

            marker.UpdatePose(pos, rot, quality, confidence);
            occlusionHandler.ClearGhostState(marker);
        }

        public void NotifyMarkerLost(string markerId)
        {
            if (!activeMarkers.TryGetValue(markerId, out var marker)) return;

            if (enableOcclusionRecovery && occlusionHandler.ShouldEnterGhostState(marker, activeMarkers.Values))
            {
                occlusionHandler.StartGhostState(marker);
                Debug.Log($"[SpatialEngine] Marker '{markerId}' entered Occlusion Ghost Grace Period ({occlusionGracePeriod}s).");
            }
            else
            {
                RemoveMarkerImmediate(markerId);
            }
        }

        private void OnGhostExpired(string markerId)
        {
            Debug.Log($"[SpatialEngine] Ghost grace period expired for '{markerId}'. Removing.");
            RemoveMarkerImmediate(markerId);
        }

        public TrackedMarkerPose GetMarker(string markerId)
        {
            if (string.IsNullOrEmpty(markerId)) return null;
            return activeMarkers.TryGetValue(markerId, out var m) ? m : null;
        }

        public void RemoveMarkerImmediate(string markerId)
        {
            if (activeMarkers.Remove(markerId))
            {
                filters.Remove(markerId);
            }
        }

        public void ClearAllMarkers()
        {
            activeMarkers.Clear();
            filters.Clear();
            previousDistances.Clear();
            currentPairs.Clear();
            activeProximityPairs.Clear();
            if (occlusionHandler != null)
            {
                occlusionHandler.ClearAll();
            }
        }

        private void ComputeSpatialRelations(float dt)
        {
            currentPairs.Clear();
            var markers = new List<TrackedMarkerPose>();

            foreach (var m in activeMarkers.Values)
            {
                if (m.isTracked || m.isOccludedGhost)
                {
                    markers.Add(m);
                }
            }

            for (int i = 0; i < markers.Count; i++)
            {
                for (int j = i + 1; j < markers.Count; j++)
                {
                    var a = markers[i];
                    var b = markers[j];

                    var pairKey = GetPairKey(a.markerId, b.markerId);

                    float dist = Vector3.Distance(a.Position, b.Position);
                    Vector3 dir = (b.Position - a.Position).normalized;

                    Vector3 normalA = a.Rotation * Vector3.up;
                    Vector3 normalB = b.Rotation * Vector3.up;
                    float normalAlign = Vector3.Dot(normalA, normalB);

                    float prevDist = previousDistances.TryGetValue(pairKey, out var pd) ? pd : dist;
                    float approachSpeed = (prevDist - dist) / Mathf.Max(0.0001f, dt);
                    previousDistances[pairKey] = dist;

                    bool inReaction = (dist >= minReactionDistanceThreshold && dist <= reactionDistanceThreshold) && (normalAlign >= minNormalAlignment);

                    var spatialPair = new MarkerSpatialPair
                    {
                        markerA = a,
                        markerB = b,
                        euclideanDistance = dist,
                        relativeDirection = dir,
                        normalAlignment = normalAlign,
                        approachSpeed = approachSpeed,
                        isWithinReactionThreshold = inReaction
                    };

                    currentPairs.Add(spatialPair);

                    // Proximity trigger lifecycle
                    if (inReaction)
                    {
                        if (activeProximityPairs.Add(pairKey))
                        {
                            OnProximityEntered?.Invoke(spatialPair);
                        }
                    }
                    else
                    {
                        if (activeProximityPairs.Remove(pairKey))
                        {
                            OnProximityExited?.Invoke(spatialPair);
                        }
                    }
                }
            }
        }

        private (string, string) GetPairKey(string idA, string idB)
        {
            return string.CompareOrdinal(idA, idB) < 0 ? (idA, idB) : (idB, idA);
        }
    }
}
