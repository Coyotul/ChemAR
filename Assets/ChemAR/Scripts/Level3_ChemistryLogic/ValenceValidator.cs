using System;
using ChemAR.Core;
using UnityEngine;

namespace ChemAR.Chemistry
{
    public enum BondClassification
    {
        NonpolarCovalent,
        PolarCovalent,
        Ionic,
        Incompatible
    }

    public class ValenceValidationResult
    {
        public bool isValid;
        public BondClassification bondClassification;
        public float deltaElectronegativity;
        public string validationMessage;
    }

    public static class ValenceValidator
    {
        public static ValenceValidationResult ValidatePair(ElementData elA, ElementData elB)
        {
            var result = new ValenceValidationResult();

            if (elA == null || elB == null)
            {
                result.isValid = false;
                result.validationMessage = "Elemente invalide sau lipsă.";
                result.bondClassification = BondClassification.Incompatible;
                return result;
            }

            // Electronegativity difference
            float deltaEN = Mathf.Abs(elA.electronegativity - elB.electronegativity);
            result.deltaElectronegativity = deltaEN;

            if (deltaEN > 1.7f)
            {
                result.bondClassification = BondClassification.Ionic;
            }
            else if (deltaEN >= 0.4f)
            {
                result.bondClassification = BondClassification.PolarCovalent;
            }
            else
            {
                result.bondClassification = BondClassification.NonpolarCovalent;
            }

            // Metals bonding with metals without alloy context or noble gases
            if (elA.category == ElementCategory.NobleGas || elB.category == ElementCategory.NobleGas)
            {
                result.isValid = false;
                result.validationMessage = "Gazele nobile au configurația stabilă de octet complet și nu formează legături uzuale.";
                result.bondClassification = BondClassification.Incompatible;
                return result;
            }

            // Valence rule check
            if (elA.standardValence > 0 && elB.standardValence > 0)
            {
                result.isValid = true;
                result.validationMessage = $"Valență compatibilă: {elA.name} (Val {elA.standardValence}) + {elB.name} (Val {elB.standardValence}). Legătură: {result.bondClassification} (ΔEN = {deltaEN:F2}).";
            }
            else
            {
                result.isValid = false;
                result.validationMessage = "Valențele elementelor nu permit formarea unei legături stabile.";
            }

            return result;
        }
    }
}
