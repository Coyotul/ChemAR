using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChemAR.Core
{
    [CreateAssetMenu(fileName = "ChemAR_Configuration", menuName = "ChemAR/Chemistry Configuration", order = 1)]
    public class ChemistryConfigurationSO : ScriptableObject
    {
        [Header("Parametri Spațiali & Tracking (Spatial Engine)")]
        [Tooltip("Distanța maximă (în metri) pentru declanșarea reacției chimice între două cartonașe.")]
        [Range(0.05f, 0.50f)]
        public float reactionDistanceThreshold = 0.20f; // 20 cm

        [Tooltip("Distanța la care apare linia de atracție și avertizarea de proximitate.")]
        [Range(0.10f, 0.80f)]
        public float proximityNoticeThreshold = 0.40f; // 40 cm

        [Tooltip("Timpul (în secunde) în care ancora unui cartonaș este menținută activă dacă este acoperit de mână.")]
        [Range(0.5f, 5.0f)]
        public float occlusionGracePeriod = 2.0f; // 2 secunde

        [Tooltip("Activare filtru adaptiv trece-jos (EMA) pentru eliminarea tremurului cauzat de reflexii.")]
        public bool enableJitterFilter = true;

        [Tooltip("Timpul cât cartonașele trebuie să stea în pragul de reacție înainte de fuziune.")]
        [Range(0.1f, 2.0f)]
        public float reactionHoldTime = 0.4f;

        [Header("Baza de Date: Elemente Chimice")]
        public List<ElementData> elements = new();

        [Header("Baza de Date: Reacții Chimice & Entalpie")]
        public List<ReactionData> reactions = new();

        public ElementData GetElement(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return null;
            string target = symbol.Trim();
            return elements.Find(e => string.Equals(e.symbol, target, StringComparison.OrdinalIgnoreCase));
        }

        public ReactionData FindReaction(List<string> candidateSymbols)
        {
            if (candidateSymbols == null || candidateSymbols.Count < 2) return null;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in candidateSymbols)
            {
                counts[s] = counts.GetValueOrDefault(s, 0) + 1;
            }

            foreach (var r in reactions)
            {
                bool matches = true;
                foreach (var req in r.reactants)
                {
                    if (!counts.TryGetValue(req.elementSymbol, out int have) || have < 1)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches) return r;
            }

            return null;
        }

        [ContextMenu("Populează cu Valorile Implicite ChemAR")]
        public void PopulateDefaults()
        {
            elements.Clear();
            reactions.Clear();

            // 1. Elemente
            elements.Add(new ElementData(
                "H", "Hidrogen", 1, 1.008f, 1, 2.20f,
                new Color(0.95f, 0.95f, 0.98f), 0.038f,
                ElementCategory.Nonmetal,
                "Cel mai ușor element chimic. 1 proton și 1 electron de valență."
            ));

            elements.Add(new ElementData(
                "O", "Oxigen", 8, 15.999f, 2, 3.44f,
                new Color(0.90f, 0.22f, 0.21f), 0.052f,
                ElementCategory.Nonmetal,
                "Element electronegativ esențial arderii și respirației. Valența standard: 2."
            ));

            elements.Add(new ElementData(
                "Na", "Sodiu", 11, 22.990f, 1, 0.93f,
                new Color(0.58f, 0.20f, 0.75f), 0.070f,
                ElementCategory.AlkaliMetal,
                "Metal alcalin foarte reactiv. Cedează ușor 1 electron (Na+)."
            ));

            elements.Add(new ElementData(
                "Cl", "Clor", 17, 35.450f, 1, 3.16f,
                new Color(0.18f, 0.70f, 0.28f), 0.062f,
                ElementCategory.Halogen,
                "Halogen reactiv. Acceptă 1 electron devenind anion clorură (Cl-)."
            ));

            elements.Add(new ElementData(
                "C", "Carbon", 6, 12.011f, 4, 2.55f,
                new Color(0.18f, 0.18f, 0.20f), 0.055f,
                ElementCategory.Nonmetal,
                "Baza chimiei organice. Tetravalent (valența 4)."
            ));

            elements.Add(new ElementData(
                "N", "Azot", 7, 14.007f, 3, 3.04f,
                new Color(0.12f, 0.45f, 0.90f), 0.048f,
                ElementCategory.Nonmetal,
                "Gaz diatomic, 78% din atmosferă. Trivalent (valența 3)."
            ));

            // 2. Reacții
            reactions.Add(new ReactionData(
                "R_H2O", "H2O", "Apă", "Dihidrogen monoxid", "2H + O → H₂O",
                new List<ReactionStoichiometry> { new("H", 1), new("O", 1) },
                -285.8f, ReactionType.DirectSynthesis, MolecularGeometry.Bent,
                "Covalent Polar (104.5°)",
                "Reacție puternic exotermică. Se formează legături covalente polare orientate la 104.5° cu eliberare de energie (ΔH = -285.8 kJ/mol).",
                new Color(1.0f, 0.45f, 0.1f)
            ));

            reactions.Add(new ReactionData(
                "R_NaCl", "NaCl", "Sare de bucătărie", "Clorură de sodiu", "Na + Cl → NaCl",
                new List<ReactionStoichiometry> { new("Na", 1), new("Cl", 1) },
                -411.1f, ReactionType.DirectSynthesis, MolecularGeometry.IonicLattice,
                "Ionic (transfer Na⁺ + Cl⁻)",
                "Reacție redox viguroasă. Transfer complet de electroni cu formare de rețea cristalină ionică (ΔH = -411.1 kJ/mol).",
                new Color(1.0f, 0.85f, 0.2f)
            ));

            reactions.Add(new ReactionData(
                "R_CO2", "CO2", "Dioxid de Carbon", "Dioxid de carbon", "C + O → CO₂",
                new List<ReactionStoichiometry> { new("C", 1), new("O", 1) },
                -393.5f, ReactionType.Combustion, MolecularGeometry.Linear,
                "Covalent Dublu O=C=O (180°)",
                "Oxidare completă a carbonului cu legături duble simetrice liniare (ΔH = -393.5 kJ/mol).",
                new Color(1.0f, 0.25f, 0.1f)
            ));

            reactions.Add(new ReactionData(
                "R_NH3", "NH3", "Amoniac", "Trihidrură de azot", "N + H → NH₃",
                new List<ReactionStoichiometry> { new("N", 1), new("H", 1) },
                -46.1f, ReactionType.DirectSynthesis, MolecularGeometry.TrigonalPyramidal,
                "Covalent Polar Piramidal",
                "Sinteza amoniacului generează o moleculă piramidal-trigonală (ΔH = -46.1 kJ/mol).",
                new Color(0.3f, 0.6f, 1.0f)
            ));

            reactions.Add(new ReactionData(
                "R_Endo", "NO2", "Dioxid de Azot (Endo)", "Dioxid de azot", "N + O → NO₂ (Endo)",
                new List<ReactionStoichiometry> { new("N", 1), new("O", 1) },
                +57.2f, ReactionType.ThermalDissociation, MolecularGeometry.TrigonalPlanar,
                "Covalent de Rezonanță",
                "Reacție endotermică (ΔH = +57.2 kJ/mol). Absoarbe căldură din mediu, răcind zona de contact.",
                new Color(0.1f, 0.8f, 1.0f)
            ));
        }
    }
}
