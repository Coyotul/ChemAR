using UnityEngine;

namespace ChemAR.Spatial
{
    public class MarkerPoseFilter
    {
        private readonly float minAlpha;
        private readonly float maxAlpha;
        private readonly float velocityThreshold;

        public MarkerPoseFilter(float minAlpha = 0.25f, float maxAlpha = 0.85f, float velocityThreshold = 0.5f)
        {
            this.minAlpha = minAlpha;
            this.maxAlpha = maxAlpha;
            this.velocityThreshold = velocityThreshold;
        }

        public Vector3 FilterPosition(Vector3 previous, Vector3 current, float deltaTime)
        {
            if (deltaTime <= 0.0001f) return current;

            float speed = Vector3.Distance(previous, current) / deltaTime;
            float t = Mathf.Clamp01(speed / velocityThreshold);
            float adaptiveAlpha = Mathf.Lerp(minAlpha, maxAlpha, t);

            return Vector3.Lerp(previous, current, adaptiveAlpha);
        }

        public Quaternion FilterRotation(Quaternion previous, Quaternion current, float deltaTime)
        {
            if (deltaTime <= 0.0001f) return current;

            float angleDiff = Quaternion.Angle(previous, current);
            float angularSpeed = angleDiff / deltaTime;
            float t = Mathf.Clamp01(angularSpeed / 90f); // 90 deg/s threshold
            float adaptiveAlpha = Mathf.Lerp(minAlpha, maxAlpha, t);

            return Quaternion.Slerp(previous, current, adaptiveAlpha);
        }
    }
}
