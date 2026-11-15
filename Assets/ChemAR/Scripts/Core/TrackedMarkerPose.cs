using System;
using UnityEngine;

namespace ChemAR.Core
{
    public enum MarkerTrackingQuality
    {
        NotTracked,
        Limited,
        Good,
        HighQuality
    }

    [Serializable]
    public class TrackedMarkerPose
    {
        public string markerId;
        public string elementSymbol;
        public Vector3 rawPosition;
        public Quaternion rawRotation;
        public Vector3 filteredPosition;
        public Quaternion filteredRotation;
        
        public bool isTracked;
        public MarkerTrackingQuality quality;
        public float trackingConfidence; // 0..1
        public float lastSeenTimestamp;
        
        // Occlusion handling states
        public bool isOccludedGhost;
        public float occlusionTimeRemaining;

        public bool IsActive => isTracked || isOccludedGhost;
        public bool isActive => IsActive;

        public Vector3 Position => isTracked ? filteredPosition : (isOccludedGhost ? filteredPosition : Vector3.zero);
        public Quaternion Rotation => isTracked ? filteredRotation : (isOccludedGhost ? filteredRotation : Quaternion.identity);

        public TrackedMarkerPose(string markerId, string elementSymbol)
        {
            this.markerId = markerId;
            this.elementSymbol = elementSymbol;
            this.rawPosition = Vector3.zero;
            this.rawRotation = Quaternion.identity;
            this.filteredPosition = Vector3.zero;
            this.filteredRotation = Quaternion.identity;
            this.isTracked = false;
            this.quality = MarkerTrackingQuality.NotTracked;
            this.trackingConfidence = 0f;
            this.lastSeenTimestamp = 0f;
            this.isOccludedGhost = false;
            this.occlusionTimeRemaining = 0f;
        }

        public void UpdatePose(Vector3 pos, Quaternion rot, MarkerTrackingQuality q, float conf)
        {
            this.rawPosition = pos;
            this.rawRotation = rot;
            this.quality = q;
            this.trackingConfidence = conf;
            this.isTracked = (q != MarkerTrackingQuality.NotTracked);
            this.lastSeenTimestamp = Time.time;
            this.isOccludedGhost = false;
        }

        public void SetTrackingState(bool tracked, MarkerTrackingQuality q, float conf)
        {
            this.isTracked = tracked;
            this.quality = tracked ? q : MarkerTrackingQuality.NotTracked;
            this.trackingConfidence = conf;
        }
    }
}
