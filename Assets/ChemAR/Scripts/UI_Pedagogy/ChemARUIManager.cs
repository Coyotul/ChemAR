using System.Collections;
using System.Collections.Generic;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Perception;
using ChemAR.Spatial;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ChemAR.UI
{
    public class ChemARUIManager : MonoBehaviour
    {
        public static ChemARUIManager Instance { get; private set; }

        [Header("System References")]
        [HideInInspector] [SerializeField] private SpatialEngine spatialEngine;
        [HideInInspector] [SerializeField] private ReactionResolver reactionResolver;
        [HideInInspector] [SerializeField] private ARPerceptionManager perceptionManager;

        private Canvas canvas;
        private GameObject reactionBottomCard;
        private Text reactionEquationText;
        private Text reactionProductNameText;
        private Text enthalpyBadgeText;
        private Text explanationText;
        private GameObject cameraHintText;
        private GameObject editorToolbar;
        private Text scaleValueText;
        private Text resetBtnText;
        private int activeTrackedMarkersCount = 0;

        public static float CurrentScale { get; private set; } = 1.0f;
        public static event System.Action<float> OnScaleChanged;
        public static event System.Action OnRequestRevertAll;

        public void NotifyMarkerTrackingChanged(bool tracked)
        {
            if (tracked) activeTrackedMarkersCount++;
            else activeTrackedMarkersCount = Mathf.Max(0, activeTrackedMarkersCount - 1);

            if (cameraHintText != null && spatialEngine == null)
            {
                cameraHintText.SetActive(activeTrackedMarkersCount == 0);
            }

            // When no cards are visible to the camera, immediately dismiss reaction card
            if (activeTrackedMarkersCount == 0)
            {
                HideReactionInfo();
            }
        }

        public void TriggerRevertAll()
        {
            HideReactionInfo();
            activeTrackedMarkersCount = 0;

            OnRequestRevertAll?.Invoke();

            // 1. Clear SpatialEngine memory (active markers, filters, pairs, ghosts)
            if (spatialEngine == null) spatialEngine = FindFirstObjectByType<SpatialEngine>();
            if (spatialEngine != null) spatialEngine.ClearAllMarkers();

            // 2. Clear ReactionResolver memory (proximity durations, active reaction records, cooldown)
            if (reactionResolver == null) reactionResolver = FindFirstObjectByType<ReactionResolver>();
            if (reactionResolver != null)
            {
                reactionResolver.DissociateAllReactions();
                reactionResolver.ClearAllMemory();
            }

            // 3. Clear ARPerceptionManager memory
            if (perceptionManager == null) perceptionManager = FindFirstObjectByType<ARPerceptionManager>();
            if (perceptionManager != null) perceptionManager.ClearAll();

            // 4. Clear VuforiaPerceptionAdapter memory
            var vpa = FindFirstObjectByType<ChemAR.VuforiaAdapter.VuforiaPerceptionAdapter>();
            if (vpa != null) vpa.ClearAllMemory();

            // 5. Clear VuforiaReactionController (reactions, proximity timers, cooldown)
            var vuforiaReactionCtrl = ChemAR.VuforiaAdapter.VuforiaReactionController.Instance ?? FindFirstObjectByType<ChemAR.VuforiaAdapter.VuforiaReactionController>();
            if (vuforiaReactionCtrl != null)
            {
                vuforiaReactionCtrl.DissociateAllReactions();
                vuforiaReactionCtrl.ClearAllMemory();
            }

            // 6. Force reset all VuforiaTrackedCard instances (tracking flags, optical sampling, atom visuals)
            var allCards = FindObjectsByType<ChemAR.VuforiaAdapter.VuforiaTrackedCard>(FindObjectsSortMode.None);
            for (int i = 0; i < allCards.Length; i++)
            {
                if (allCards[i] != null) allCards[i].ForceReset();
            }

            // 7. Destroy any leftover spawned 3D molecule visualizers in scene
            var allMols = FindObjectsByType<ChemAR.Render.MoleculeVisualizer>(FindObjectsSortMode.None);
            foreach (var m in allMols)
            {
                if (m != null) Destroy(m.gameObject);
            }

            // 8. Reset scale back to 100%
            SetScale(1.0f);

            // Visual feedback on button
            if (resetBtnText != null)
            {
                StopCoroutine("FlashResetButtonFeedback");
                StartCoroutine("FlashResetButtonFeedback");
            }
        }

        private IEnumerator FlashResetButtonFeedback()
        {
            if (resetBtnText != null)
            {
                resetBtnText.text = "✓ Resetat!";
                yield return new WaitForSeconds(1.2f);
                resetBtnText.text = "↺ Revert";
            }
        }

        public void IncreaseScale()
        {
            SetScale(CurrentScale + 0.15f);
        }

        public void DecreaseScale()
        {
            SetScale(CurrentScale - 0.15f);
        }

        public void SetScale(float newScale)
        {
            CurrentScale = Mathf.Clamp(newScale, 0.30f, 2.20f);
            if (scaleValueText != null)
            {
                scaleValueText.text = $"{Mathf.RoundToInt(CurrentScale * 100f)}%";
            }
            ApplyGlobalScale(CurrentScale);
            OnScaleChanged?.Invoke(CurrentScale);
        }

        public static void ApplyGlobalScale(float scale)
        {
            var atoms = Object.FindObjectsByType<ChemAR.Render.AtomVisualizer>(FindObjectsSortMode.None);
            for (int i = 0; i < atoms.Length; i++)
            {
                if (atoms[i] != null)
                {
                    atoms[i].transform.localScale = Vector3.one * scale;
                }
            }

            var molecules = Object.FindObjectsByType<ChemAR.Render.MoleculeVisualizer>(FindObjectsSortMode.None);
            for (int i = 0; i < molecules.Length; i++)
            {
                if (molecules[i] != null)
                {
                    molecules[i].transform.localScale = Vector3.one * scale;
                }
            }
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void Start()
        {
            if (spatialEngine == null) spatialEngine = FindFirstObjectByType<SpatialEngine>();
            if (reactionResolver == null) reactionResolver = FindFirstObjectByType<ReactionResolver>();
            if (perceptionManager == null) perceptionManager = FindFirstObjectByType<ARPerceptionManager>();

            BuildCleanUIHierarchy();

            if (reactionResolver != null)
            {
                reactionResolver.OnReactionSynthesized += HandleReactionSynthesized;
                reactionResolver.OnReactionDissociated += HandleReactionDissociated;
            }
        }

        private void OnDestroy()
        {
            if (reactionResolver != null)
            {
                reactionResolver.OnReactionSynthesized -= HandleReactionSynthesized;
                reactionResolver.OnReactionDissociated -= HandleReactionDissociated;
            }
        }

        private void BuildCleanUIHierarchy()
        {
            // 0. Ensure EventSystem exists with InputModule so buttons can receive touch/click events
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();

                var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (inputModuleType != null)
                {
                    eventSystemGo.AddComponent(inputModuleType);
                }
                else
                {
                    eventSystemGo.AddComponent<StandaloneInputModule>();
                }
            }

            // 1. Fullscreen Overlay Canvas
            var canvasGo = new GameObject("ChemAR_Canvas");
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); // Mobile Portrait Standard
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                try { defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch {}
            }

            // 2. Subtle Camera Scan Hint (Visible ONLY when 0 markers are in view)
            var hintGo = CreatePanel("CameraHint", canvas.transform, new Vector2(0.15f, 0.06f), new Vector2(0.85f, 0.11f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.45f));
            var hintTxt = CreateText("HintText", hintGo.transform, "📸 Îndreaptă camera spre cartonașe", defaultFont, 26, TextAnchor.MiddleCenter, Color.white);
            hintTxt.rectTransform.anchorMin = Vector2.zero;
            hintTxt.rectTransform.anchorMax = Vector2.one;
            hintTxt.rectTransform.sizeDelta = Vector2.zero;
            cameraHintText = hintGo;

            // 3. Sleek Floating Bottom Reaction Card (Visible ONLY during active reaction)
            reactionBottomCard = CreatePanel("ReactionBottomCard", canvas.transform, new Vector2(0.04f, 0.03f), new Vector2(0.96f, 0.18f), new Vector2(0.5f, 0f), Vector2.zero, Vector2.zero, new Color(0.06f, 0.09f, 0.16f, 0.93f));

            // Accent Top Stripe
            var stripe = CreatePanel("AccentStripe", reactionBottomCard.transform, new Vector2(0f, 0.97f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero, new Color(0.2f, 0.8f, 1f, 0.9f));

            // Equation (Large Cyan)
            reactionEquationText = CreateText("ReactionEquation", reactionBottomCard.transform, "2H₂ + O₂ → 2H₂O", defaultFont, 34, TextAnchor.UpperLeft, new Color(0.25f, 0.85f, 1f));
            reactionEquationText.rectTransform.anchorMin = new Vector2(0.04f, 0.62f);
            reactionEquationText.rectTransform.anchorMax = new Vector2(0.50f, 0.94f);
            reactionEquationText.rectTransform.sizeDelta = Vector2.zero;

            // Enthalpy / Thermo Badge
            enthalpyBadgeText = CreateText("EnthalpyBadge", reactionBottomCard.transform, "🔥 EXOTERMICĂ (ΔH = -483.6 kJ/mol)", defaultFont, 22, TextAnchor.UpperRight, new Color(1f, 0.5f, 0.2f));
            enthalpyBadgeText.rectTransform.anchorMin = new Vector2(0.48f, 0.64f);
            enthalpyBadgeText.rectTransform.anchorMax = new Vector2(0.82f, 0.94f);
            enthalpyBadgeText.rectTransform.sizeDelta = Vector2.zero;

            // Close / Revert button on bottom card
            var closeBtnGo = CreateButton("CloseBtn", reactionBottomCard.transform, new Vector2(0.84f, 0.62f), new Vector2(0.97f, 0.94f), "✕ Închide", defaultFont, new Color(0.85f, 0.2f, 0.2f, 0.85f));
            closeBtnGo.GetComponent<Button>().onClick.AddListener(TriggerRevertAll);

            // Product Name
            reactionProductNameText = CreateText("ProductName", reactionBottomCard.transform, "<b>Apă</b> (Monoxid de dihidrogen)", defaultFont, 26, TextAnchor.MiddleLeft, Color.white);
            reactionProductNameText.rectTransform.anchorMin = new Vector2(0.04f, 0.34f);
            reactionProductNameText.rectTransform.anchorMax = new Vector2(0.96f, 0.62f);
            reactionProductNameText.rectTransform.sizeDelta = Vector2.zero;

            // Pedagogical explanation & Bond description
            explanationText = CreateText("Explanation", reactionBottomCard.transform, "Legătură covalentă polară • Geometrie unghiulară 104.5°", defaultFont, 21, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.95f));
            explanationText.rectTransform.anchorMin = new Vector2(0.04f, 0.06f);
            explanationText.rectTransform.anchorMax = new Vector2(0.96f, 0.34f);
            explanationText.rectTransform.sizeDelta = Vector2.zero;

            reactionBottomCard.SetActive(false);

            // 4. Interactive Scale Controls ([-] 100% [+]) placed comfortably below mobile status bar / notch
            var scalePanelGo = CreatePanel("ScaleControlPanel", canvas.transform, new Vector2(0.66f, 0.875f), new Vector2(0.97f, 0.93f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.08f, 0.12f, 0.20f, 0.92f));

            // [-] Button
            var scaleDownBtn = CreateButton("BtnScaleDown", scalePanelGo.transform, new Vector2(0f, 0f), new Vector2(0.32f, 1f), "-", defaultFont, new Color(0.18f, 0.24f, 0.36f, 0.95f), 32);
            scaleDownBtn.GetComponent<Button>().onClick.AddListener(DecreaseScale);

            // Scale % Label
            scaleValueText = CreateText("ScaleValue", scalePanelGo.transform, $"{Mathf.RoundToInt(CurrentScale * 100f)}%", defaultFont, 20, TextAnchor.MiddleCenter, Color.white);
            scaleValueText.rectTransform.anchorMin = new Vector2(0.32f, 0f);
            scaleValueText.rectTransform.anchorMax = new Vector2(0.68f, 1f);
            scaleValueText.rectTransform.sizeDelta = Vector2.zero;

            // [+] Button
            var scaleUpBtn = CreateButton("BtnScaleUp", scalePanelGo.transform, new Vector2(0.68f, 0f), new Vector2(1f, 1f), "+", defaultFont, new Color(0.18f, 0.24f, 0.36f, 0.95f), 32);
            scaleUpBtn.GetComponent<Button>().onClick.AddListener(IncreaseScale);

            // 5. Dedicated Floating Reset / Revert Button directly below the Scale panel (safe touch zone)
            var resetBtnGo = CreateButton("ResetScreenBtn", canvas.transform, new Vector2(0.66f, 0.81f), new Vector2(0.97f, 0.865f), "↺ Revert", defaultFont, new Color(0.16f, 0.22f, 0.34f, 0.95f), 22);
            resetBtnGo.GetComponent<Button>().onClick.AddListener(TriggerRevertAll);
            resetBtnText = resetBtnGo.GetComponentInChildren<Text>();

            // 4. Editor Quick Toolbar removed - keep screen clean showing only camera feed and AR augmentations
        }

        private void SwitchElementPair(string elA, string elB)
        {
            if (perceptionManager == null) return;
            perceptionManager.ClearSimulationCards();
            perceptionManager.SpawnSimulatedCard($"Element_{elA}1", elA, new Vector3(-0.25f, 0f, 0.6f));
            perceptionManager.SpawnSimulatedCard($"Element_{elB}1", elB, new Vector3(0.25f, 0f, 0.6f));
            if (reactionResolver != null) reactionResolver.DissociateAllReactions();
        }

        private void Update()
        {
            // Toggle camera scan hint depending on whether any marker is currently detected
            if (cameraHintText != null && spatialEngine != null)
            {
                bool hasMarkers = spatialEngine.ActiveMarkers.Count > 0;
                bool hasReaction = reactionResolver != null && reactionResolver.ActiveReactions.Count > 0;
                cameraHintText.SetActive(!hasMarkers && !hasReaction);
            }
        }

        public void ShowReactionInfo(ReactionData reaction)
        {
            if (reactionBottomCard == null || reaction == null) return;

            reactionBottomCard.SetActive(true);
            if (reactionEquationText != null) reactionEquationText.text = reaction.reactionEquation;
            if (reactionProductNameText != null) reactionProductNameText.text = $"<b>{reaction.productName}</b> ({reaction.productFormula})";

            bool isExo = reaction.ThermalNature == ThermalType.Exothermic;
            string thermoIcon = isExo ? "🔥" : "❄️";
            string natureStr = isExo ? "EXOTERMICĂ" : "ENDOTERMICĂ";
            if (enthalpyBadgeText != null)
            {
                enthalpyBadgeText.text = $"{thermoIcon} {natureStr} (ΔH = {reaction.deltaEnthalpy} kJ/mol)";
                enthalpyBadgeText.color = isExo ? new Color(1f, 0.45f, 0.15f) : new Color(0.25f, 0.85f, 1f);
            }

            if (explanationText != null) explanationText.text = $"{reaction.pedagogicalExplanation} • <b>{reaction.bondTypeDescription}</b>";
        }

        public void HideReactionInfo()
        {
            if (reactionBottomCard != null)
            {
                reactionBottomCard.SetActive(false);
            }
        }

        private void HandleReactionSynthesized(ReactionData reaction, Vector3 center, List<TrackedMarkerPose> reactants)
        {
            ShowReactionInfo(reaction);
        }

        private void HandleReactionDissociated(ReactionInstance instance)
        {
            HideReactionInfo();
        }

        // Procedural UI element generators
        private GameObject CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 sizeDelta, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycastTarget;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            return go;
        }

        private Text CreateText(string name, Transform parent, string content, Font font, int fontSize, TextAnchor alignment, Color color, bool raycastTarget = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.text = content;
            txt.font = font;
            txt.fontSize = fontSize;
            txt.alignment = alignment;
            txt.color = color;
            txt.supportRichText = true;
            txt.raycastTarget = raycastTarget;
            return txt;
        }

        private GameObject CreateButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label, Font font, Color btnColor, int fontSize = 20)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = btnColor;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var txt = CreateText("Label", go.transform, label, font, fontSize, TextAnchor.MiddleCenter, Color.white, false);
            txt.rectTransform.anchorMin = Vector2.zero;
            txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.sizeDelta = Vector2.zero;

            return go;
        }

        private void CreateToolbarButton(Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label, Font font, UnityEngine.Events.UnityAction onClick)
        {
            var btnGo = CreateButton($"Btn_{label}", parent, anchorMin, anchorMax, label, font, new Color(0.18f, 0.35f, 0.65f, 0.85f));
            btnGo.GetComponent<Button>().onClick.AddListener(onClick);
        }
    }
}
