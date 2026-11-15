using System;
using System.Collections.Generic;
using ChemAR.Core;
using UnityEngine;

namespace ChemAR.Spatial
{
    public class OcclusionHandler
    {
        public float GracePeriodSeconds { get; set; } = 2.0f;
        public float ProximityOcclusionThreshold { get; set; } = 0.25f; // meters

        public event Action<string, bool> OnMarkerOcclusionStateChanged;

        private readonly Dictionary<string, float> occlusionTimers = new();

        public bool ShouldEnterGhostState(TrackedMarkerPose lostMarker, IEnumerable<TrackedMarkerPose> otherActiveMarkers)
        {
            if (lostMarker == null) return false;

            // Check if this marker was close to any other active marker when tracking was lost
            foreach (var other in otherActiveMarkers)
            {
                if (other.markerId == lostMarker.markerId) continue;
                if (!other.isTracked && !other.isOccludedGhost) continue;

                float dist = Vector3.Distance(lostMarker.filteredPosition, other.filteredPosition);
                if (dist <= ProximityOcclusionThreshold)
                {
                    return true;
                }
            }

            return false;
        }

        public void StartGhostState(TrackedMarkerPose marker)
        {
            marker.isOccludedGhost = true;
            marker.occlusionTimeRemaining = GracePeriodSeconds;
            occlusionTimers[marker.markerId] = GracePeriodSeconds;
            OnMarkerOcclusionStateChanged?.Invoke(marker.markerId, true);
        }

        public void ClearGhostState(TrackedMarkerPose marker)
        {
            if (marker.isOccludedGhost)
            {
                marker.isOccludedGhost = false;
                marker.occlusionTimeRemaining = 0f;
                occlusionTimers.Remove(marker.markerId);
                OnMarkerOcclusionStateChanged?.Invoke(marker.markerId, false);
            }
        }

        public void Update(float deltaTime, List<TrackedMarkerPose> markers, Action<string> onGhostExpired)
        {
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                var marker = markers[i];
                if (marker.isOccludedGhost)
                {
                    marker.occlusionTimeRemaining -= deltaTime;
                    if (marker.occlusionTimeRemaining <= 0f)
                    {
                        marker.isOccludedGhost = false;
                        occlusionTimers.Remove(marker.markerId);
                        OnMarkerOcclusionStateChanged?.Invoke(marker.markerId, false);
                        onGhostExpired?.Invoke(marker.markerId);
                    }
                }
            }
        }
    }
}
