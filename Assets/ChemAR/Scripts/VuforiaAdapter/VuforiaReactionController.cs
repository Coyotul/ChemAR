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
        public float lostTimestamp;
    }

    public class VuforiaReactionController : MonoBehaviour
    {
        public static VuforiaReactionController Instance { get; private set; }

        [Header("Reaction Distance Thresholds")]
        [Tooltip("Minimum distance in meters to prevent overlapping cards / same physical card false reactions (default 10.5cm)")]
        [SerializeField] private float minReactionDistance = 0.105f;

        [Tooltip("Distance in meters to trigger molecule synthesis (default 18cm)")]
        [SerializeField] private float reactionDistance = 0.18f;

        [Tooltip("Distance in meters to trigger automatic dissociation (default 25cm)")]
        [SerializeField] private float dissociationDistance = 0.25f;

        [Tooltip("Time in seconds cards must stay close before reacting (default 1.0s)")]
        [SerializeField] private float reactionTriggerDelay = 1.0f;

        [Tooltip("Grace period in seconds when a card is briefly lost before dissociating")]
        [SerializeField] private float trackingLostGracePeriod = 1.0f;

        [Header("Dependencies (Auto-located if null)")]
        [SerializeField] private ThermalVFXManager thermalVfx;
        [SerializeField] private ChemARUIManager uiManager;

        private readonly List<VuforiaTrackedCard> trackedCards = new();
        private readonly List<ActiveVuforiaReaction> activeReactions = new();
        private readonly Dictionary<(string, string), float> proximityTimer = new();
        private float revertCooldownTimer = 0f;

        public IReadOnlyList<ActiveVuforiaReaction> ActiveReactions => activeReactions;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            ReactionResolver.IsExternalReactionControllerActive = true;

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
            ReactionResolver.IsExternalReactionControllerActive = false;
            ChemARUIManager.OnRequestRevertAll -= DissociateAllReactions;
        }

        public void RegisterCard(VuforiaTrackedCard card)
        {
            if (!trackedCards.Contains(card)) trackedCards.Add(card);
        }

        private void Update()
        {
            // 1. Check for potential reactions between pairs of tracked cards
            CheckForNewReactions();

            // 2. Update existing active reactions (follow midpoint & auto-dissociate when separated)
            UpdateActiveReactions();
        }

        private void CheckForNewReactions()
        {
            if (revertCooldownTimer > 0f)
            {
                revertCooldownTimer -= Time.deltaTime;
                proximityTimer.Clear();
                return;
            }

            // Strict limit: maximum 1 active reaction at a time to prevent visual clutter
            if (activeReactions.Count >= 1) return;

            for (int i = 0; i < trackedCards.Count; i++)
            {
                var cardA = trackedCards[i];
                if (cardA == null || !cardA.IsTracked || cardA.IsReacting || !cardA.IsVisibleInCamera()) continue;
                // Guard: cardA must be stably confirmed
                if (cardA.ConfirmedTrackedDuration < 0.40f) continue;

                for (int j = i + 1; j < trackedCards.Count; j++)
                {
                    var cardB = trackedCards[j];
                    if (cardB == null || !cardB.IsTracked || cardB.IsReacting || !cardB.IsVisibleInCamera()) continue;
                    // Guard: cardB must be stably confirmed
                    if (cardB.ConfirmedTrackedDuration < 0.40f) continue;

                    // Chemical Identity Guard: cards must represent distinct elements
                    if (cardA.Symbol == cardB.Symbol) continue;

                    // Chemical Validity Guard: verify these elements can react before counting proximity
                    var symbols = new List<string> { cardA.Symbol, cardB.Symbol };
                    var reaction = ChemistryDatabase.Instance.FindReaction(symbols);
                    if (reaction == null) continue;

                    float dist = Vector3.Distance(cardA.transform.position, cardB.transform.position);

                    // Must be within intentional reaction zone: not overlapping (< 10.5cm) and not too far (> 18cm)
                    if (dist >= minReactionDistance && dist <= reactionDistance)
                    {
                        var key = GetCardKey(cardA, cardB);
                        proximityTimer[key] = proximityTimer.GetValueOrDefault(key, 0f) + Time.deltaTime;

                        if (proximityTimer[key] >= reactionTriggerDelay)
                        {
                            TryTriggerReaction(cardA, cardB, reaction);
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

        private void TryTriggerReaction(VuforiaTrackedCard cardA, VuforiaTrackedCard cardB, ReactionData reaction)
        {
            if (reaction == null) return;

            Debug.Log($"<color=#00e676>[VuforiaReactionController] REACȚIE SINTETIZATĂ: {reaction.reactionEquation} între {cardA.Symbol} și {cardB.Symbol}</color>");

            // Set cards reacting
            cardA.SetReacting(true);
            cardB.SetReacting(true);

            // Compute midpoint in world space
            Vector3 midpoint = (cardA.transform.position + cardB.transform.position) * 0.5f;

            // Spawn 3D molecule visualizer (clean, simple geometry + label)
            var molGo = new GameObject($"Molecule_{reaction.productFormula}");
            var molVis = molGo.AddComponent<MoleculeVisualizer>();
            molVis.Initialize(reaction, midpoint);

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

                bool aLost = !act.cardA.IsTracked || !act.cardA.IsVisibleInCamera();
                bool bLost = !act.cardB.IsTracked || !act.cardB.IsVisibleInCamera();

                if (aLost || bLost)
                {
                    if (act.lostTimestamp <= 0f)
                    {
                        act.lostTimestamp = Time.time;
                    }

                    // Grace period check: do not instantly tear down reaction on single-frame drops
                    float grace = (aLost && bLost) ? 0.6f : trackingLostGracePeriod;
                    if (Time.time - act.lostTimestamp >= grace)
                    {
                        Debug.Log($"[VuforiaReactionController] Cartonaș pierdut sau ieșit din cadru (> {grace:F1}s). Disociere automată pentru {act.reaction.reactionEquation}.");
                        DissociateReaction(act);
                        continue;
                    }
                }
                else
                {
                    // Both cards stably tracked: reset lost timer
                    act.lostTimestamp = 0f;
                }

                // Check distance between cards
                float dist = Vector3.Distance(act.cardA.transform.position, act.cardB.transform.position);

                if (dist > dissociationDistance)
                {
                    Debug.Log($"[VuforiaReactionController] Cartonașe depărtate ({dist:F2}m > {dissociationDistance:F2}m). Disociere automată pentru {act.reaction.reactionEquation}.");
                    DissociateReaction(act);
                    continue;
                }

                // Update molecule position to stay centered between cards with smooth interpolation
                if (act.moleculeVisualizer != null)
                {
                    Vector3 midpoint = (act.cardA.transform.position + act.cardB.transform.position) * 0.5f;
                    act.moleculeVisualizer.transform.position = Vector3.Lerp(act.moleculeVisualizer.transform.position, midpoint, Time.deltaTime * 18f);
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
            revertCooldownTimer = 1.5f;

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

            // Destroy any leftover spawned 3D molecule visualizers in scene
            var allMols = FindObjectsByType<MoleculeVisualizer>(FindObjectsSortMode.None);
            for (int i = 0; i < allMols.Length; i++)
            {
                if (allMols[i] != null) Destroy(allMols[i].gameObject);
            }

            for (int i = 0; i < trackedCards.Count; i++)
            {
                if (trackedCards[i] != null)
                {
                    trackedCards[i].SetReacting(false);
                    trackedCards[i].ForceReset();
                }
            }

            if (uiManager != null)
            {
                uiManager.HideReactionInfo();
            }
        }

        public void ClearAllMemory()
        {
            DissociateAllReactions();
        }

        private (string, string) GetCardKey(VuforiaTrackedCard a, VuforiaTrackedCard b)
        {
            return string.CompareOrdinal(a.name, b.name) < 0 ? (a.name, b.name) : (b.name, a.name);
        }
    }
}
