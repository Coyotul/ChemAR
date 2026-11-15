using System;
using UnityEngine;

namespace ChemAR.Core
{
    public enum ElementCategory
    {
        Nonmetal,
        NobleGas,
        AlkaliMetal,
        AlkalineEarth,
        TransitionMetal,
        PostTransitionMetal,
        Metalloid,
        Halogen
    }

    [Serializable]
    public class ElementData
    {
        public string symbol;
        public string name;
        public int atomicNumber;
        public float atomicMass;
        public int standardValence;
        public float electronegativity;
        public Color cpkColor;
        public float visualRadius;
        public ElementCategory category;
        public string pedagogicalNotes;

        public ElementData(string symbol, string name, int atomicNumber, float atomicMass, int valence, 
            float electronegativity, Color cpkColor, float visualRadius, ElementCategory category, string pedagogicalNotes)
        {
            this.symbol = symbol;
            this.name = name;
            this.atomicNumber = atomicNumber;
            this.atomicMass = atomicMass;
            this.standardValence = valence;
            this.electronegativity = electronegativity;
            this.cpkColor = cpkColor;
            this.visualRadius = visualRadius;
            this.category = category;
            this.pedagogicalNotes = pedagogicalNotes;
        }
    }
}
