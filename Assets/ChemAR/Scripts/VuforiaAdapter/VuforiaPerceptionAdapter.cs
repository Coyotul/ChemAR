using System;
using System.Collections.Generic;
using ChemAR.Core;
using ChemAR.Perception;
using UnityEngine;
using Vuforia;

namespace ChemAR.VuforiaAdapter
{
    public class VuforiaPerceptionAdapter : MonoBehaviour, ITrackedMarkerSource
    {
        public event Action<TrackedMarkerPose> OnMarkerAdded;
        public event Action<TrackedMarkerPose> OnMarkerUpdated;
        public event Action<string> OnMarkerLost;

        public bool IsSourceActive => isActiveAndEnabled;

        private readonly Dictionary<string, TrackedMarkerPose> activeMarkers = new();
        public IReadOnlyDictionary<string, TrackedMarkerPose> ActiveMarkers => activeMarkers;

        private readonly List<ObserverBehaviour> observers = new();

        private void OnEnable()
        {
            ChemAR.UI.ChemARUIManager.OnRequestRevertAll += ClearAllMemory;
        }

        private void OnDisable()
        {
            ChemAR.UI.ChemARUIManager.OnRequestRevertAll -= ClearAllMemory;
        }

        private void Start()
        {
            RegisterExistingObservers();
            AttachToPerceptionManager();
        }

        public void RegisterExistingObservers()
        {
            observers.Clear();
            observers.AddRange(FindObjectsByType<ObserverBehaviour>(FindObjectsSortMode.None));

            foreach (var obs in observers)
            {
                obs.OnTargetStatusChanged += HandleTargetStatusChanged;
            }
        }

        private void OnDestroy()
        {
            foreach (var obs in observers)
            {
                if (obs != null) obs.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }
        }

        private void AttachToPerceptionManager()
        {
            if (ARPerceptionManager.Instance != null)
            {
                ARPerceptionManager.Instance.SetExternalARSource(this);
            }
        }

        public void ClearAllMemory()
        {
            activeMarkers.Clear();
        }

        private void HandleTargetStatusChanged(ObserverBehaviour obs, TargetStatus targetStatus)
        {
            string id = obs.TargetName;
            if (string.IsNullOrEmpty(id)) id = obs.gameObject.name;

            // Strict: only genuine TRACKED status is accepted. EXTENDED_TRACKED is rejected!
            bool isObserved = (targetStatus.Status == Status.TRACKED);

            if (isObserved)
            {
                string symbol = VuforiaTrackedCard.ParseSymbolFromName(id);
                bool isNew = !activeMarkers.TryGetValue(id, out var pose);

                if (isNew)
                {
                    pose = new TrackedMarkerPose(id, symbol);
                    activeMarkers[id] = pose;
                }

                pose.UpdatePose(obs.transform.position, obs.transform.rotation, MarkerTrackingQuality.HighQuality, 1.0f);

                if (isNew) OnMarkerAdded?.Invoke(pose);
                else OnMarkerUpdated?.Invoke(pose);
            }
            else
            {
                if (activeMarkers.Remove(id, out var pose))
                {
                    pose.SetTrackingState(false, MarkerTrackingQuality.NotTracked, 0f);
                    OnMarkerLost?.Invoke(id);
                }
            }
        }

        private void LateUpdate()
        {
            foreach (var obs in observers)
            {
                if (obs == null) continue;
                string id = obs.TargetName;
                if (string.IsNullOrEmpty(id)) id = obs.gameObject.name;

                if (activeMarkers.TryGetValue(id, out var pose))
                {
                    if (Vector3.Distance(pose.Position, obs.transform.position) > 0.001f ||
                        Quaternion.Angle(pose.Rotation, obs.transform.rotation) > 0.5f)
                    {
                        pose.UpdatePose(obs.transform.position, obs.transform.rotation, MarkerTrackingQuality.HighQuality, 1.0f);
                        OnMarkerUpdated?.Invoke(pose);
                    }
                }
            }
        }
    }
}
