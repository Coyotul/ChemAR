using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChemAR.Core
{
    public enum ReactionType
    {
        DirectSynthesis,
        IonicPrecipitation,
        Combustion,
        AcidBaseNeutralization,
        ThermalDissociation
    }

    public enum ThermalType
    {
        Exothermic, // Delta H < 0 (releases heat)
        Endothermic  // Delta H > 0 (absorbs heat)
    }

    public enum MolecularGeometry
    {
        DiatomicLinear,
        Bent,                // e.g. H2O (104.5 deg)
        Linear,              // e.g. CO2 (180 deg)
        TrigonalPlanar,
        TrigonalPyramidal,   // e.g. NH3
        Tetrahedral,         // e.g. CH4
        IonicLattice         // e.g. NaCl cubic
    }

    [Serializable]
    public class ReactionStoichiometry
    {
        public string elementSymbol;
        public int count;

        public ReactionStoichiometry(string symbol, int count)
        {
            this.elementSymbol = symbol;
            this.count = count;
        }
    }

    [Serializable]
    public class ReactionData
    {
        public string reactionId;
        public string productFormula;
        public string productName;
        public string productIupac;
        public string reactionEquation;
        public List<ReactionStoichiometry> reactants;
        
        [Tooltip("Standard Enthalpy of reaction in kJ/mol. Negative = Exothermic, Positive = Endothermic")]
        public float deltaEnthalpy; // kJ/mol
        
        public ReactionType reactionType;
        public MolecularGeometry geometry;
        public string bondTypeDescription;
        public string pedagogicalExplanation;
        public Color primaryVfxColor;

        public ThermalType ThermalNature => deltaEnthalpy < 0 ? ThermalType.Exothermic : ThermalType.Endothermic;

        public ReactionData(string id, string formula, string name, string iupac, string equation,
            List<ReactionStoichiometry> reactants, float deltaH, ReactionType rType, MolecularGeometry geom,
            string bondType, string explanation, Color vfxColor)
        {
            this.reactionId = id;
            this.productFormula = formula;
            this.productName = name;
            this.productIupac = iupac;
            this.reactionEquation = equation;
            this.reactants = reactants;
            this.deltaEnthalpy = deltaH;
            this.reactionType = rType;
            this.geometry = geom;
            this.bondTypeDescription = bondType;
            this.pedagogicalExplanation = explanation;
            this.primaryVfxColor = vfxColor;
        }
    }
}
