using System;
using ChemAR.Chemistry;
using ChemAR.Core;
using ChemAR.Render;
using ChemAR.UI;
using UnityEngine;
using Vuforia;

namespace ChemAR.VuforiaAdapter
{
    [RequireComponent(typeof(ObserverBehaviour))]
    public class VuforiaTrackedCard : MonoBehaviour
    {
        [Header("Element Definition")]
        [SerializeField] private string elementSymbol = "H";
        [SerializeField] private float cardWidth = 0.12f;

        [Header("Visualization")]
        [SerializeField] private AtomVisualizer childAtomVisualizer;
        [SerializeField] private bool autoCreateVisualizerIfMissing = true;

        [Header("Stability Guards")]
        [Tooltip("Time in seconds the marker must be continuously detected before confirming")]
        [SerializeField] private float trackingConfirmationTime = 0.20f;

        [Tooltip("Time in seconds the marker must be continuously lost before declaring lost")]
        [SerializeField] private float trackingLostDebounceTime = 0.35f;

        [Tooltip("Minimum distance in meters from another active card to prevent ghost overlap detections")]
        [SerializeField] private float minSpatialSeparation = 0.08f;

        private ObserverBehaviour observer;
        private bool isTracked;
        private bool isReacting;
        private float confirmedTrackedStartTime;
        private float lastTrackedTime;

        // Raw tracking state from Vuforia observer events
        private bool rawTracked;
        private float rawTrackedStartTime;
        private float rawLostStartTime;

        public string Symbol => elementSymbol;
        public float Width => cardWidth;
        public bool IsTracked => isTracked;
        public bool IsReacting => isReacting;
        public float LastTrackedTime => lastTrackedTime;
        public float ConfirmedTrackedDuration => isTracked ? (Time.time - confirmedTrackedStartTime) : 0f;
        public AtomVisualizer AtomVisualizer => childAtomVisualizer;

        public event Action<VuforiaTrackedCard, bool> OnTrackingChanged;

        private void OnEnable()
        {
            ChemARUIManager.OnRequestRevertAll += ForceReset;
        }

        private void OnDisable()
        {
            ChemARUIManager.OnRequestRevertAll -= ForceReset;
        }

        private void Awake()
        {
            observer = GetComponent<ObserverBehaviour>();
            if (string.IsNullOrEmpty(elementSymbol))
            {
                elementSymbol = ParseSymbolFromName(gameObject.name);
            }

            if (childAtomVisualizer == null)
            {
                childAtomVisualizer = GetComponentInChildren<AtomVisualizer>(true);
            }
            if (childAtomVisualizer != null)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            if (observer != null)
            {
                observer.OnTargetStatusChanged += HandleTargetStatusChanged;
                HandleTargetStatusChanged(observer, observer.TargetStatus);
            }

            SetupVisualizer();
        }

        private void OnDestroy()
        {
            if (observer != null)
            {
                observer.OnTargetStatusChanged -= HandleTargetStatusChanged;
            }
        }

        public void ConfigureElement(string symbol, float width = 0.12f)
        {
            this.elementSymbol = symbol;
            this.cardWidth = width;
            SetupVisualizer();
        }

        private void SetupVisualizer()
        {
            if (childAtomVisualizer == null)
            {
                childAtomVisualizer = GetComponentInChildren<AtomVisualizer>(true);
            }

            var el = ChemistryDatabase.Instance.GetElement(elementSymbol);
            if (el != null)
            {
                if (childAtomVisualizer == null && autoCreateVisualizerIfMissing)
                {
                    var visGo = new GameObject($"Atom_{elementSymbol}");
                    visGo.transform.SetParent(transform, false);
                    visGo.transform.localPosition = Vector3.zero;
                    visGo.transform.localRotation = Quaternion.identity;
                    childAtomVisualizer = visGo.AddComponent<AtomVisualizer>();
                }

                if (childAtomVisualizer != null && childAtomVisualizer.Element == null)
                {
                    childAtomVisualizer.Initialize(el);
                }
            }

            // Initially hide visualizer until tracked
            if (childAtomVisualizer != null && !isTracked)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
        }

        private void HandleTargetStatusChanged(ObserverBehaviour obs, TargetStatus status)
        {
            // Only accept genuine TRACKED status, rejecting LIMITED and EXTENDED_TRACKED
            bool valid = (status.Status == Status.TRACKED && status.StatusInfo != StatusInfo.LIMITED);
            if (valid != rawTracked)
            {
                rawTracked = valid;
                if (rawTracked)
                {
                    rawTrackedStartTime = Time.time;
                }
                else
                {
                    rawLostStartTime = Time.time;
                }
            }
        }

        private void Update()
        {
            // GUARD 1 & 2: Temporal confirmation debounce + Spatial mutex
            if (!isTracked && rawTracked)
            {
                if (Time.time - rawTrackedStartTime >= trackingConfirmationTime)
                {
                    // Spatial Mutex Guard: Check if another card is already confirmed at this exact position
                    if (!IsPositionOccupiedByAnotherCard())
                    {
                        SetTrackedConfirmed(true);
                    }
                }
            }
            else if (isTracked && !rawTracked)
            {
                if (Time.time - rawLostStartTime >= trackingLostDebounceTime)
                {
                    SetTrackedConfirmed(false);
                }
            }
            else if (isTracked && rawTracked)
            {
                lastTrackedTime = Time.time;
            }
        }

        private bool IsPositionOccupiedByAnotherCard()
        {
            var allCards = FindObjectsByType<VuforiaTrackedCard>(FindObjectsSortMode.None);
            for (int i = 0; i < allCards.Length; i++)
            {
                var other = allCards[i];
                if (other != null && other != this && other.IsTracked)
                {
                    float dist = Vector3.Distance(transform.position, other.transform.position);
                    if (dist < minSpatialSeparation)
                    {
                        // Another card is already stably confirmed at this position!
                        // This prevents ghost spawning (e.g. Cl ghosting when looking at N or Na)
                        return true;
                    }
                }
            }
            return false;
        }

        private void SetTrackedConfirmed(bool tracked)
        {
            if (isTracked == tracked) return;

            isTracked = tracked;
            if (isTracked)
            {
                confirmedTrackedStartTime = Time.time;
                lastTrackedTime = Time.time;
                Debug.Log($"<color=#00e676><b>[ChemAR] Marker STABIL CONFIRMAT: {elementSymbol}</b></color>");
            }
            else
            {
                Debug.Log($"<color=#ff9100><b>[ChemAR] Marker PIERDUT: {elementSymbol}</b></color>");
            }

            UpdateVisualState();
            OnTrackingChanged?.Invoke(this, isTracked);

            var ui = ChemARUIManager.Instance;
            if (ui != null)
            {
                ui.NotifyMarkerTrackingChanged(isTracked);
            }
        }

        public void ForceReset()
        {
            isReacting = false;
            rawTracked = false;
            rawTrackedStartTime = 0f;
            rawLostStartTime = 0f;
            SetTrackedConfirmed(false);
            UpdateVisualState();
        }

        public void SetReacting(bool reacting)
        {
            isReacting = reacting;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            if (childAtomVisualizer == null)
            {
                SetupVisualizer();
            }
            if (childAtomVisualizer == null) return;

            if (!isTracked)
            {
                childAtomVisualizer.gameObject.SetActive(false);
            }
            else
            {
                childAtomVisualizer.gameObject.SetActive(true);
                childAtomVisualizer.transform.localScale = Vector3.one * ChemARUIManager.CurrentScale;
                childAtomVisualizer.SetReactingState(isReacting);
            }
        }

        public static string ParseSymbolFromName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "H";
            string upper = name.ToUpperInvariant();
            if (upper.Contains("NACL")) return "NaCl";
            if (upper.Contains("NA")) return "Na";
            if (upper.Contains("CL")) return "Cl";
            if (upper.Contains("CO2")) return "CO2";
            if (upper.Contains("H2O")) return "H2O";
            if (upper.Contains("NH3")) return "NH3";
            if (upper.Contains("NO2")) return "NO2";
            if (upper.EndsWith("_H") || upper.Contains("HIDROGEN") || upper.Contains("HYDROGEN")) return "H";
            if (upper.EndsWith("_O") || upper.Contains("OXIGEN") || upper.Contains("OXYGEN")) return "O";
            if (upper.EndsWith("_C") || upper.Contains("CARBON")) return "C";
            if (upper.EndsWith("_N") || upper.Contains("AZOT") || upper.Contains("NITROGEN")) return "N";

            int underscore = name.LastIndexOf('_');
            if (underscore >= 0 && underscore < name.Length - 1)
            {
                return name.Substring(underscore + 1).Trim();
            }
            return name.Trim();
        }
    }
}
