// Deliberately minimal API-shaped stand-ins; does not establish compatibility with a real SDK release.
using System;
using UnityEngine;

namespace Vuforia
{
    public enum Status { NO_POSE, LIMITED, TRACKED, EXTENDED_TRACKED }
    public struct TargetStatus { public Status Status; }
    public class ObserverBehaviour : Behaviour { public TargetStatus TargetStatus; }
    public sealed class VuforiaApplication
    {
        public static VuforiaApplication Instance { get; } = new();
        public bool IsRunning;
        public int InitializeCount;
        public event Action OnVuforiaStarted;
        public void Initialize() { InitializeCount++; }
        public void CompleteStart() { IsRunning = true; OnVuforiaStarted?.Invoke(); }
    }
    public class VuforiaBehaviour : Behaviour { public static VuforiaBehaviour Instance; }
}
