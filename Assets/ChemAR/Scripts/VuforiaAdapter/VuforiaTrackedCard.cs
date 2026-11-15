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

        private ObserverBehaviour observer;
        private bool isTracked;
        private bool isReacting;
        private float lastTrackedTime;

        public string Symbol => elementSymbol;
        public float Width => cardWidth;
        public bool IsTracked => isTracked;
        public bool IsReacting => isReacting;
        public float LastTrackedTime => lastTrackedTime;
        public AtomVisualizer AtomVisualizer => childAtomVisualizer;

        public event Action<VuforiaTrackedCard, bool> OnTrackingChanged;

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
            // Only consider tracked when Vuforia genuinely sees the image target in view.
            // Exclude EXTENDED_TRACKED and LIMITED to prevent false triggers from adjacent markers on printed sheets.
            bool nowTracked = (status.Status == Status.TRACKED && status.StatusInfo != StatusInfo.LIMITED);
            if (nowTracked != isTracked)
            {
                isTracked = nowTracked;
                if (isTracked)
                {
                    lastTrackedTime = Time.time;
                    Debug.Log($"<color=#00e676><b>[ChemAR] Marker RECONOSCUT: {elementSymbol} ({status.Status} / {status.StatusInfo})</b></color>");
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
        }

        public void ForceReset()
        {
            isReacting = false;
            if (observer != null)
            {
                HandleTargetStatusChanged(observer, observer.TargetStatus);
            }
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
                // Simplified mode: atoms stay visible on cards during reactions
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
