using System;
using System.Collections.Generic;
using UnityEngine;

namespace ChemAR.Core
{
    public interface ITrackedMarkerSource
    {
        event Action<TrackedMarkerPose> OnMarkerAdded;
        event Action<TrackedMarkerPose> OnMarkerUpdated;
        event Action<string> OnMarkerLost;
        
        IReadOnlyDictionary<string, TrackedMarkerPose> ActiveMarkers { get; }
        bool IsSourceActive { get; }
    }
}
