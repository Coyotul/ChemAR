using System.Collections.Generic;
using ChemAR.Core;
using UnityEngine;

namespace ChemAR.Chemistry
{
    public class ChemistryDatabase
    {
        private static ChemistryDatabase instance;
        public static ChemistryDatabase Instance => instance ??= new ChemistryDatabase();

        private ChemistryConfigurationSO activeConfig;
        private readonly Dictionary<string, ElementData> elements = new();
        private readonly List<ReactionData> reactions = new();

        public ChemistryConfigurationSO Configuration => activeConfig;
        public IReadOnlyDictionary<string, ElementData> Elements => elements;
        public IReadOnlyList<ReactionData> Reactions => reactions;

        public ChemistryDatabase()
        {
            // Try loading ScriptableObject from Resources
            activeConfig = Resources.Load<ChemistryConfigurationSO>("ChemAR_Configuration");
            if (activeConfig != null && activeConfig.elements.Count > 0)
            {
                LoadFromConfiguration(activeConfig);
            }
            else
            {
                InitializeFallbackElements();
                InitializeFallbackReactions();
            }
        }

        public void SetConfiguration(ChemistryConfigurationSO config)
        {
            if (config == null) return;
            activeConfig = config;
            LoadFromConfiguration(config);
        }

        private void LoadFromConfiguration(ChemistryConfigurationSO config)
        {
            elements.Clear();
            reactions.Clear();

            foreach (var el in config.elements)
            {
                if (el != null && !string.IsNullOrEmpty(el.symbol))
                {
                    elements[el.symbol.Trim()] = el;
                }
            }

            foreach (var r in config.reactions)
            {
                if (r != null)
                {
                    reactions.Add(r);
                }
            }
        }

        private void InitializeFallbackElements()
        {
            elements["H"] = new ElementData("H", "Hidrogen", 1, 1.008f, 1, 2.20f, new Color(0.95f, 0.95f, 0.98f), 0.038f, ElementCategory.Nonmetal, "Cel mai ușor element chimic.");
            elements["O"] = new ElementData("O", "Oxigen", 8, 15.999f, 2, 3.44f, new Color(0.90f, 0.22f, 0.21f), 0.052f, ElementCategory.Nonmetal, "Element electronegativ esențial respirației.");
            elements["Na"] = new ElementData("Na", "Sodiu", 11, 22.990f, 1, 0.93f, new Color(0.58f, 0.20f, 0.75f), 0.070f, ElementCategory.AlkaliMetal, "Metal alcalin foarte reactiv.");
            elements["Cl"] = new ElementData("Cl", "Clor", 17, 35.450f, 1, 3.16f, new Color(0.18f, 0.70f, 0.28f), 0.062f, ElementCategory.Halogen, "Halogen gazos.");
            elements["C"] = new ElementData("C", "Carbon", 6, 12.011f, 4, 2.55f, new Color(0.18f, 0.18f, 0.20f), 0.055f, ElementCategory.Nonmetal, "Baza chimiei organice.");
            elements["N"] = new ElementData("N", "Azot", 7, 14.007f, 3, 3.04f, new Color(0.12f, 0.45f, 0.90f), 0.048f, ElementCategory.Nonmetal, "Gaz diatomic ce constituie 78% din aer.");
        }

        private void InitializeFallbackReactions()
        {
            reactions.Add(new ReactionData("R_H2O", "H2O", "Apă", "Dihidrogen monoxid", "2H + O → H₂O", new List<ReactionStoichiometry> { new("H", 1), new("O", 1) }, -285.8f, ReactionType.DirectSynthesis, MolecularGeometry.Bent, "Covalent Polar", "Reacție puternic exotermică.", new Color(1.0f, 0.45f, 0.1f)));
            reactions.Add(new ReactionData("R_NaCl", "NaCl", "Sare de bucătărie", "Clorură de sodiu", "Na + Cl → NaCl", new List<ReactionStoichiometry> { new("Na", 1), new("Cl", 1) }, -411.1f, ReactionType.DirectSynthesis, MolecularGeometry.IonicLattice, "Ionic", "Reacție redox viguroasă.", new Color(1.0f, 0.85f, 0.2f)));
            reactions.Add(new ReactionData("R_CO2", "CO2", "Dioxid de Carbon", "Dioxid de carbon", "C + O → CO₂", new List<ReactionStoichiometry> { new("C", 1), new("O", 1) }, -393.5f, ReactionType.Combustion, MolecularGeometry.Linear, "Covalent Dublu", "Oxidare a carbonului.", new Color(1.0f, 0.25f, 0.1f)));
            reactions.Add(new ReactionData("R_NH3", "NH3", "Amoniac", "Trihidrură de azot", "N + H → NH₃", new List<ReactionStoichiometry> { new("N", 1), new("H", 1) }, -46.1f, ReactionType.DirectSynthesis, MolecularGeometry.TrigonalPyramidal, "Covalent Polar", "Sinteza amoniacului.", new Color(0.3f, 0.6f, 1.0f)));
            reactions.Add(new ReactionData("R_Endo", "NO2", "Dioxid de Azot (Endo)", "Dioxid de azot", "N + O → NO₂ (Endo)", new List<ReactionStoichiometry> { new("N", 1), new("O", 1) }, +57.2f, ReactionType.ThermalDissociation, MolecularGeometry.TrigonalPlanar, "Covalent de Rezonanță", "Reacție endotermică.", new Color(0.1f, 0.8f, 1.0f)));
        }

        public ElementData GetElement(string symbol)
        {
            if (activeConfig != null)
            {
                var el = activeConfig.GetElement(symbol);
                if (el != null) return el;
            }

            if (string.IsNullOrEmpty(symbol)) return null;
            elements.TryGetValue(symbol.Trim(), out var fallbackEl);
            return fallbackEl;
        }

        public ReactionData FindReaction(List<string> candidateSymbols)
        {
            if (activeConfig != null)
            {
                var r = activeConfig.FindReaction(candidateSymbols);
                if (r != null) return r;
            }

            if (candidateSymbols == null || candidateSymbols.Count < 2) return null;

            var counts = new Dictionary<string, int>();
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
    }
}
