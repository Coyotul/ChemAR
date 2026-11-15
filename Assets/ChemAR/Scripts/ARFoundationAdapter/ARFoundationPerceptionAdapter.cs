#if CHEMAR_ARFOUNDATION_PRESENT
using System;
using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Perception;
using ChemAR.Render;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ChemAR.AR
{
    [RequireComponent(typeof(ARTrackedImageManager))]
    public class ARFoundationPerceptionAdapter : MonoBehaviour, ITrackedMarkerSource
    {
        public event Action<TrackedMarkerPose> OnMarkerAdded;
        public event Action<TrackedMarkerPose> OnMarkerUpdated;
        public event Action<string> OnMarkerLost;

        public bool IsSourceActive => trackedImageManager != null && trackedImageManager.enabled;

        private ARTrackedImageManager trackedImageManager;
        private ReactionResolver reactionResolver;
        private readonly Dictionary<string, TrackedMarkerPose> activeMarkers = new();
        private readonly Dictionary<string, AtomVisualizer> cardVisualizers = new();

        public IReadOnlyDictionary<string, TrackedMarkerPose> ActiveMarkers => activeMarkers;
        public IReadOnlyDictionary<string, AtomVisualizer> CardVisualizers => cardVisualizers;

        private void Awake()
        {
            trackedImageManager = GetComponent<ARTrackedImageManager>();
        }

        private void OnEnable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
            }

            AttachToPerceptionManager();
            HookReactionResolver();
        }

        private void Start()
        {
            AttachToPerceptionManager();
            HookReactionResolver();
        }

        private void HookReactionResolver()
        {
            if (reactionResolver == null)
            {
                reactionResolver = FindFirstObjectByType<ReactionResolver>();
                if (reactionResolver != null)
                {
                    reactionResolver.OnReactionSynthesized += HandleReactionSynthesized;
                    reactionResolver.OnReactionDissociated += HandleReactionDissociated;
                }
            }
        }

        private void AttachToPerceptionManager()
        {
            if (ARPerceptionManager.Instance != null)
            {
                ARPerceptionManager.Instance.SetExternalARSource(this);
                ARPerceptionManager.Instance.SetMode(PerceptionMode.DeviceARFoundation);
            }
        }

        private void OnDisable()
        {
            if (trackedImageManager != null)
            {
                trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
            }

            if (reactionResolver != null)
            {
                reactionResolver.OnReactionSynthesized -= HandleReactionSynthesized;
                reactionResolver.OnReactionDissociated -= HandleReactionDissociated;
            }
        }

        private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
        {
            // 1. Process added images
            foreach (var img in eventArgs.added)
            {
                ProcessImageUpdate(img, isNew: true);
            }

            // 2. Process updated images
            foreach (var img in eventArgs.updated)
            {
                ProcessImageUpdate(img, isNew: false);
            }

            // 3. Process removed images
            foreach (var img in eventArgs.removed)
            {
                string id = img.trackableId.ToString();
                if (activeMarkers.Remove(id))
                {
                    OnMarkerLost?.Invoke(id);
                }

                if (cardVisualizers.Remove(id, out var vis) && vis != null)
                {
                    Destroy(vis.gameObject);
                }
            }
        }

        private void ProcessImageUpdate(ARTrackedImage img, bool isNew)
        {
            string id = img.trackableId.ToString();
            string refName = img.referenceImage.name;
            string symbol = ExtractSymbolFromReferenceName(refName);

            if (!activeMarkers.TryGetValue(id, out var pose))
            {
                pose = new TrackedMarkerPose(id, symbol);
                activeMarkers[id] = pose;
            }

            MarkerTrackingQuality quality = ConvertTrackingState(img.trackingState);
            float confidence = (quality == MarkerTrackingQuality.HighQuality) ? 0.95f : 0.5f;

            pose.UpdatePose(img.transform.position, img.transform.rotation, quality, confidence);

            // Ensure 3D holographic Visualizer & UI Badge exists anchored to this physical card
            if (!cardVisualizers.TryGetValue(id, out var vis) || vis == null)
            {
                var el = ChemistryDatabase.Instance.GetElement(symbol);
                if (el != null)
                {
                    var visGo = new GameObject($"AtomVisualizer_{symbol}");
                    visGo.transform.SetParent(img.transform, false);
                    visGo.transform.localPosition = Vector3.zero;
                    visGo.transform.localRotation = Quaternion.identity;
                    vis = visGo.AddComponent<AtomVisualizer>();
                    vis.Initialize(el);
                    cardVisualizers[id] = vis;
                    Debug.Log($"[ARFoundationPerceptionAdapter] Spawnează vizualizator și UI augmentat pentru {symbol} ({id})");
                }
            }

            if (vis != null)
            {
                bool isVisible = (img.trackingState == TrackingState.Tracking || img.trackingState == TrackingState.Limited);
                vis.gameObject.SetActive(isVisible);
            }

            if (isNew)
            {
                OnMarkerAdded?.Invoke(pose);
            }
            else
            {
                if (img.trackingState == TrackingState.None)
                {
                    OnMarkerLost?.Invoke(id);
                }
                else
                {
                    OnMarkerUpdated?.Invoke(pose);
                }
            }
        }

        private void HandleReactionSynthesized(ReactionData reaction, Vector3 center, List<TrackedMarkerPose> reactants)
        {
            foreach (var r in reactants)
            {
                if (cardVisualizers.TryGetValue(r.markerId, out var vis) && vis != null)
                {
                    vis.SetReactingState(true);
                }
            }
        }

        private void HandleReactionDissociated(ReactionInstance instance)
        {
            if (instance.participatingMarkerIds != null)
            {
                foreach (var id in instance.participatingMarkerIds)
                {
                    if (cardVisualizers.TryGetValue(id, out var vis) && vis != null)
                    {
                        vis.SetReactingState(false);
                    }
                }
            }
        }

        private string ExtractSymbolFromReferenceName(string refName)
        {
            if (string.IsNullOrEmpty(refName)) return "H";

            // Support both "Marker_H", "Element_H", or raw "H"
            string s = refName.Replace("Marker_", "").Replace("Element_", "").Trim();
            return s;
        }

        private MarkerTrackingQuality ConvertTrackingState(TrackingState state)
        {
            return state switch
            {
                TrackingState.Tracking => MarkerTrackingQuality.HighQuality,
                TrackingState.Limited => MarkerTrackingQuality.Good, // Valid AR pose in ARCore!
                _ => MarkerTrackingQuality.NotTracked
            };
        }
    }
}
#endif
