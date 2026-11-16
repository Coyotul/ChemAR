using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Render;
using ChemAR.UI;
using UnityEngine;

namespace ChemAR.VuforiaAdapter
{
    public class ActiveVuforiaReaction
    {
        public ReactionData reaction;
        public VuforiaTrackedCard cardA;
        public VuforiaTrackedCard cardB;
        public MoleculeVisualizer moleculeVisualizer;
        public float startTimestamp;
    }

    public class VuforiaReactionController : MonoBehaviour
    {
        public static VuforiaReactionController Instance { get; private set; }

        [Header("Reaction Distance Thresholds")]
        [Tooltip("Minimum distance in meters between cards to reject co-located ghost detections (default 6cm)")]
        [SerializeField] private float minReactionDistance = 0.06f;

        [Tooltip("Distance in meters to trigger molecule synthesis (default 18cm)")]
        [SerializeField] private float reactionDistance = 0.18f;

        [Tooltip("Distance in meters to trigger automatic dissociation (default 25cm)")]
        [SerializeField] private float dissociationDistance = 0.25f;

        [Tooltip("Time in seconds cards must stay close before reacting")]
        [SerializeField] private float reactionTriggerDelay = 0.25f;

        [Tooltip("Grace period in seconds when a card is briefly lost before dissociating")]
        [SerializeField] private float trackingLostGracePeriod = 1.2f;

        [Header("Dependencies (Auto-located if null)")]
        [SerializeField] private ThermalVFXManager thermalVfx;
        [SerializeField] private ChemARUIManager uiManager;

        private readonly List<VuforiaTrackedCard> trackedCards = new();
        private readonly List<ActiveVuforiaReaction> activeReactions = new();
        private readonly Dictionary<(string, string), float> proximityTimer = new();

        public IReadOnlyList<ActiveVuforiaReaction> ActiveReactions => activeReactions;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            if (thermalVfx == null) thermalVfx = GetComponent<ThermalVFXManager>() ?? FindFirstObjectByType<ThermalVFXManager>();
            if (uiManager == null) uiManager = GetComponent<ChemARUIManager>() ?? FindFirstObjectByType<ChemARUIManager>();
        }

        private void Start()
        {
            if (thermalVfx == null) thermalVfx = FindFirstObjectByType<ThermalVFXManager>();
            if (uiManager == null) uiManager = FindFirstObjectByType<ChemARUIManager>();

            // Listen for manual user Revert / Reset requests
            ChemARUIManager.OnRequestRevertAll += DissociateAllReactions;

            // Find all cards in scene
            trackedCards.AddRange(FindObjectsByType<VuforiaTrackedCard>(FindObjectsSortMode.None));
        }

        private void OnDestroy()
        {
            ChemARUIManager.OnRequestRevertAll -= DissociateAllReactions;
        }

        public void RegisterCard(VuforiaTrackedCard card)
        {
            if (!trackedCards.Contains(card)) trackedCards.Add(card);
        }

        private void Update()
        {
            // 0. Suppress duplicate/ghost cards falsely detected at the exact same location (< minReactionDistance)
            SuppressCoLocatedGhostCards();

            // 1. Check for potential reactions between pairs of tracked cards
            CheckForNewReactions();

            // 2. Update existing active reactions (follow midpoint & auto-dissociate when separated)
            UpdateActiveReactions();
        }

        private void SuppressCoLocatedGhostCards()
        {
            var activeCards = new List<VuforiaTrackedCard>();
            for (int i = 0; i < trackedCards.Count; i++)
            {
                var card = trackedCards[i];
                if (card != null && card.IsRawTracked)
                {
                    activeCards.Add(card);
                }
            }

            if (activeCards.Count <= 1)
            {
                if (activeCards.Count == 1)
                {
                    activeCards[0].SetGhostSuppressed(false);
                }
                return;
            }

            for (int i = 0; i < activeCards.Count; i++)
            {
                activeCards[i].SetGhostSuppressed(false);
            }

            for (int i = 0; i < activeCards.Count; i++)
            {
                var cardA = activeCards[i];
                if (cardA.IsGhostSuppressed) continue;

                for (int j = i + 1; j < activeCards.Count; j++)
                {
                    var cardB = activeCards[j];
                    if (cardB.IsGhostSuppressed) continue;

                    float dist = Vector3.Distance(cardA.transform.position, cardB.transform.position);
                    if (dist < minReactionDistance)
                    {
                        // Co-located ghost conflict: two cards detected on the same physical sheet!
                        // Prioritize optically verified cards over unverified ones
                        if (cardA.IsOpticallyVerified && !cardB.IsOpticallyVerified)
                        {
                            cardB.SetGhostSuppressed(true);
                        }
                        else if (!cardA.IsOpticallyVerified && cardB.IsOpticallyVerified)
                        {
                            cardA.SetGhostSuppressed(true);
                            break;
                        }
                        else if (cardA.LastTrackedTime <= cardB.LastTrackedTime)
                        {
                            cardB.SetGhostSuppressed(true);
                        }
                        else
                        {
                            cardA.SetGhostSuppressed(true);
                            break;
                        }
                    }
                }
            }
        }

        private void CheckForNewReactions()
        {
            // Strict limit: maximum 1 active reaction at a time to prevent visual clutter
            if (activeReactions.Count >= 1) return;

            for (int i = 0; i < trackedCards.Count; i++)
            {
                var cardA = trackedCards[i];
                if (!cardA.IsTracked || cardA.IsReacting) continue;

                for (int j = i + 1; j < trackedCards.Count; j++)
                {
                    var cardB = trackedCards[j];
                    if (!cardB.IsTracked || cardB.IsReacting) continue;

                    float dist = Vector3.Distance(cardA.transform.position, cardB.transform.position);

                    // Must be two separate physical cards (dist >= minReactionDistance) and within reaction threshold
                    if (dist >= minReactionDistance && dist <= reactionDistance)
                    {
                        var key = GetCardKey(cardA, cardB);
                        proximityTimer[key] = proximityTimer.GetValueOrDefault(key, 0f) + Time.deltaTime;

                        if (proximityTimer[key] >= reactionTriggerDelay)
                        {
                            TryTriggerReaction(cardA, cardB);
                            proximityTimer[key] = 0f;
                        }
                    }
                    else
                    {
                        var key = GetCardKey(cardA, cardB);
                        proximityTimer[key] = 0f;
                    }
                }
            }
        }

        private void TryTriggerReaction(VuforiaTrackedCard cardA, VuforiaTrackedCard cardB)
        {
            var symbols = new List<string> { cardA.Symbol, cardB.Symbol };
            var reaction = ChemistryDatabase.Instance.FindReaction(symbols);

            if (reaction == null) return;

            Debug.Log($"[VuforiaReactionController] REACȚIE SINTETIZATĂ: {reaction.reactionEquation} între {cardA.Symbol} și {cardB.Symbol}");

            // Set cards reacting (hides individual atoms)
            cardA.SetReacting(true);
            cardB.SetReacting(true);

            // Compute midpoint in world space
            Vector3 midpoint = (cardA.transform.position + cardB.transform.position) * 0.5f;

            // Spawn 3D molecule visualizer
            var molGo = new GameObject($"Molecule_{reaction.productFormula}");
            var molVis = molGo.AddComponent<MoleculeVisualizer>();
            molVis.Initialize(reaction, midpoint);

            // Trigger thermal particle burst (exothermic heat shockwave / endothermic frost)
            if (thermalVfx != null)
            {
                thermalVfx.TriggerThermalEffect(reaction, midpoint);
            }

            // Show bottom reaction info UI
            if (uiManager != null)
            {
                uiManager.ShowReactionInfo(reaction);
            }

            var activeReaction = new ActiveVuforiaReaction
            {
                reaction = reaction,
                cardA = cardA,
                cardB = cardB,
                moleculeVisualizer = molVis,
                startTimestamp = Time.time
            };

            activeReactions.Add(activeReaction);
        }

        private void UpdateActiveReactions()
        {
            for (int i = activeReactions.Count - 1; i >= 0; i--)
            {
                var act = activeReactions[i];

                // If BOTH cards lost tracking -> dissociate immediately without waiting
                if (!act.cardA.IsTracked && !act.cardB.IsTracked)
                {
                    Debug.Log($"[VuforiaReactionController] Ambele cartonașe au fost ascunse. Disociere imediată pentru {act.reaction.reactionEquation}.");
                    DissociateReaction(act);
                    continue;
                }

                // If either card lost tracking for more than grace period -> dissociate
                bool aLost = !act.cardA.IsTracked && (Time.time - act.cardA.LastTrackedTime > trackingLostGracePeriod);
                bool bLost = !act.cardB.IsTracked && (Time.time - act.cardB.LastTrackedTime > trackingLostGracePeriod);

                if (aLost || bLost)
                {
                    Debug.Log($"[VuforiaReactionController] Cartonaș pierdut din vedere. Disociere automată pentru {act.reaction.reactionEquation}.");
                    DissociateReaction(act);
                    continue;
                }

                // Check distance between cards
                float dist = Vector3.Distance(act.cardA.transform.position, act.cardB.transform.position);

                if (dist < minReactionDistance || dist > dissociationDistance)
                {
                    Debug.Log($"[VuforiaReactionController] Cartonașe depărtate sau suprapuse ({dist:F2}m). Disociere automată pentru {act.reaction.reactionEquation}.");
                    DissociateReaction(act);
                    continue;
                }

                // Update molecule position to stay centered between cards
                if (act.moleculeVisualizer != null)
                {
                    Vector3 midpoint = (act.cardA.transform.position + act.cardB.transform.position) * 0.5f;
                    act.moleculeVisualizer.transform.position = midpoint;
                }
            }
        }

        public void DissociateReaction(ActiveVuforiaReaction act)
        {
            activeReactions.Remove(act);

            // Destroy molecule visualizer
            if (act.moleculeVisualizer != null)
            {
                Destroy(act.moleculeVisualizer.gameObject);
            }

            // Restore individual atoms on cards
            if (act.cardA != null) act.cardA.SetReacting(false);
            if (act.cardB != null) act.cardB.SetReacting(false);

            // Hide UI if no active reactions remaining
            if (activeReactions.Count == 0 && uiManager != null)
            {
                uiManager.HideReactionInfo();
            }
        }

        public void DissociateAllReactions()
        {
            for (int i = activeReactions.Count - 1; i >= 0; i--)
            {
                var act = activeReactions[i];
                if (act.moleculeVisualizer != null)
                {
                    Destroy(act.moleculeVisualizer.gameObject);
                }
                if (act.cardA != null) act.cardA.SetReacting(false);
                if (act.cardB != null) act.cardB.SetReacting(false);
            }
            activeReactions.Clear();
            proximityTimer.Clear();

            var allCards = FindObjectsByType<VuforiaTrackedCard>(FindObjectsSortMode.None);
            for (int i = 0; i < allCards.Length; i++)
            {
                if (allCards[i] != null)
                {
                    allCards[i].SetReacting(false);
                    allCards[i].ForceReset();
                }
            }

            if (uiManager != null)
            {
                uiManager.HideReactionInfo();
            }
        }

        private (string, string) GetCardKey(VuforiaTrackedCard a, VuforiaTrackedCard b)
        {
            return string.CompareOrdinal(a.name, b.name) < 0 ? (a.name, b.name) : (b.name, a.name);
        }
    }
}
