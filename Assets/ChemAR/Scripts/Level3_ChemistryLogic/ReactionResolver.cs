using System;
using System.Collections.Generic;
using ChemAR.Core;
using ChemAR.Spatial;
using UnityEngine;

namespace ChemAR.Chemistry
{
    public class ReactionInstance
    {
        public ReactionData reaction;
        public Vector3 reactionCenter;
        public List<string> participatingMarkerIds;
        public float triggerTimestamp;
        public bool isSynthesized;
    }

    public class ReactionResolver : MonoBehaviour
    {
        [SerializeField] private SpatialEngine spatialEngine;
        [SerializeField] private float reactionHoldTime = 0.4f; // time proximate markers must stay close before fusing

        public event Action<ReactionData, Vector3, List<TrackedMarkerPose>> OnReactionSynthesized;
        public event Action<ReactionInstance> OnReactionDissociated;

        private readonly Dictionary<string, float> proximityDuration = new();
        private readonly List<ReactionInstance> activeReactions = new();

        public IReadOnlyList<ReactionInstance> ActiveReactions => activeReactions;

        private void Start()
        {
            if (spatialEngine == null)
            {
                spatialEngine = FindFirstObjectByType<SpatialEngine>();
            }

            if (spatialEngine != null)
            {
                spatialEngine.OnProximityEntered += HandleProximityEntered;
                spatialEngine.OnProximityExited += HandleProximityExited;
            }
        }

        private void OnDestroy()
        {
            if (spatialEngine != null)
            {
                spatialEngine.OnProximityEntered -= HandleProximityEntered;
                spatialEngine.OnProximityExited -= HandleProximityExited;
            }
        }

        private void Update()
        {
            if (spatialEngine == null) return;

            // 1. Check currently active pairs within reaction threshold to trigger synthesis
            foreach (var pair in spatialEngine.CurrentPairs)
            {
                if (!pair.isWithinReactionThreshold) continue;

                // Check if already in an active reaction
                if (IsMarkerInActiveReaction(pair.markerA.markerId) || IsMarkerInActiveReaction(pair.markerB.markerId))
                {
                    continue;
                }

                string pairKey = GetPairKey(pair.markerA.markerId, pair.markerB.markerId);
                proximityDuration[pairKey] = proximityDuration.GetValueOrDefault(pairKey, 0f) + Time.deltaTime;

                if (proximityDuration[pairKey] >= reactionHoldTime)
                {
                    TryExecuteReaction(pair.markerA, pair.markerB);
                    proximityDuration[pairKey] = 0f;
                }
            }

            // 2. AUTOMATIC DISSOCIATION: Check if cards separated or lost
            float separationThreshold = spatialEngine.ReactionDistanceThreshold + 0.05f; // Hysteresis (e.g. 25 cm)
            for (int i = activeReactions.Count - 1; i >= 0; i--)
            {
                var instance = activeReactions[i];
                if (instance.participatingMarkerIds == null || instance.participatingMarkerIds.Count < 2)
                {
                    DissociateReaction(instance);
                    continue;
                }

                var markerA = spatialEngine.GetMarker(instance.participatingMarkerIds[0]);
                var markerB = spatialEngine.GetMarker(instance.participatingMarkerIds[1]);

                // If either marker is no longer active (lost tracking / removed)
                if (markerA == null || markerB == null || !markerA.IsActive || !markerB.IsActive)
                {
                    DissociateReaction(instance);
                    continue;
                }

                // If cards were pulled apart beyond threshold
                float distance = Vector3.Distance(markerA.Position, markerB.Position);
                if (distance > separationThreshold)
                {
                    Debug.Log($"[ReactionResolver] Cartonașele au fost depărtate ({distance:F2}m > {separationThreshold:F2}m). Reacția {instance.reaction.reactionEquation} a fost disociată automat.");
                    DissociateReaction(instance);
                }
                else
                {
                    // Dynamically update midpoint so molecule follows cards in hand
                    instance.reactionCenter = (markerA.Position + markerB.Position) * 0.5f;
                }
            }
        }

        private void HandleProximityEntered(MarkerSpatialPair pair)
        {
            string pairKey = GetPairKey(pair.markerA.markerId, pair.markerB.markerId);
            proximityDuration[pairKey] = 0f;
        }

        private void HandleProximityExited(MarkerSpatialPair pair)
        {
            string pairKey = GetPairKey(pair.markerA.markerId, pair.markerB.markerId);
            proximityDuration.Remove(pairKey);
        }

        public bool TryExecuteReaction(TrackedMarkerPose a, TrackedMarkerPose b)
        {
            var db = ChemistryDatabase.Instance;
            var elA = db.GetElement(a.elementSymbol);
            var elB = db.GetElement(b.elementSymbol);

            if (elA == null || elB == null) return false;

            // 1. Valence validation
            var valResult = ValenceValidator.ValidatePair(elA, elB);
            if (!valResult.isValid)
            {
                Debug.Log($"[ReactionResolver] Incompatible pair: {valResult.validationMessage}");
                return false;
            }

            // 2. Reaction lookup in database
            var candidateSymbols = new List<string> { a.elementSymbol, b.elementSymbol };
            var reaction = db.FindReaction(candidateSymbols);

            if (reaction == null)
            {
                Debug.Log($"[ReactionResolver] No specific reaction registered for {a.elementSymbol} + {b.elementSymbol}");
                return false;
            }

            // 3. Barycenter position for reaction
            Vector3 center = (a.Position + b.Position) * 0.5f;

            var reactants = new List<TrackedMarkerPose> { a, b };
            var instance = new ReactionInstance
            {
                reaction = reaction,
                reactionCenter = center,
                participatingMarkerIds = new List<string> { a.markerId, b.markerId },
                triggerTimestamp = Time.time,
                isSynthesized = true
            };

            activeReactions.Add(instance);
            Debug.Log($"[ReactionResolver] REACTION SUCCESS: {reaction.reactionEquation} (Delta H = {reaction.deltaEnthalpy} kJ/mol) at {center}");
            
            OnReactionSynthesized?.Invoke(reaction, center, reactants);
            return true;
        }

        public void DissociateReaction(ReactionInstance instance)
        {
            if (activeReactions.Remove(instance))
            {
                instance.isSynthesized = false;
                OnReactionDissociated?.Invoke(instance);
            }
        }

        public void DissociateAllReactions()
        {
            for (int i = activeReactions.Count - 1; i >= 0; i--)
            {
                DissociateReaction(activeReactions[i]);
            }
        }

        public bool IsMarkerInActiveReaction(string markerId)
        {
            foreach (var r in activeReactions)
            {
                if (r.participatingMarkerIds.Contains(markerId)) return true;
            }
            return false;
        }

        private string GetPairKey(string a, string b)
        {
            return string.CompareOrdinal(a, b) < 0 ? $"{a}_{b}" : $"{b}_{a}";
        }
    }
}
