using UnityEngine;
using UnityEngine.UI;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.UI
{
    /// <summary>
    /// 手机首轮测试用的只读状态面板。只依赖 Core 契约，不引用 App 装配层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetStatusPanel : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour stateSourceBehaviour;
        [SerializeField] private Text trackingText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text behaviorText;
        [SerializeField] private Text hungerText;
        [SerializeField] private Text happinessText;
        [SerializeField] private Text energyText;

        private IAppUiState _stateSource;
        private TrackingUiState _lastTrackingState = (TrackingUiState)(-1);

        private void Awake()
        {
            ResolveSource();
            Refresh();
        }

        private void Update()
        {
            if (_stateSource == null && !ResolveSource()) return;
            Refresh();
        }

        private bool ResolveSource()
        {
            _stateSource = stateSourceBehaviour as IAppUiState;
            return _stateSource != null;
        }

        private void Refresh()
        {
            if (_stateSource == null)
            {
                SetText(trackingText, "App is not ready");
                if (trackingText != null) trackingText.color = new Color(1f, 0.65f, 0.45f, 1f);
                return;
            }

            var tracking = _stateSource.TrackingState;
            if (trackingText != null)
            {
                var message = _stateSource.IsReady
                    ? TrackingHintText.For(tracking)
                    : "Configuration error - check log";
                if (!string.IsNullOrWhiteSpace(_stateSource.TrackingPauseReason))
                {
                    message += "\n" + _stateSource.TrackingPauseReason;
                }
                trackingText.text = message;

                if (_lastTrackingState != tracking)
                {
                    trackingText.color = TrackingHintText.ColorFor(tracking);
                    _lastTrackingState = tracking;
                }
            }

            SetText(instructionText, InstructionFor(tracking));

            var state = _stateSource.PetSnapshot;
            SetText(behaviorText, "Action  " + BehaviorName(_stateSource.CurrentBehavior));
            SetText(hungerText, "Hunger  " + Percent(state.Hunger));
            SetText(happinessText, "Happy  " + Percent(state.Happiness));
            SetText(energyText, "Energy  " + Percent(state.Energy));
        }

        private static string Percent(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";

        private static void SetText(Text target, string value)
        {
            if (target != null) target.text = value;
        }

        private static string BehaviorName(PetBehaviorId behavior) => behavior switch
        {
            PetBehaviorId.Idle => "Idle",
            PetBehaviorId.Eating => "Eating",
            PetBehaviorId.Petted => "Petted",
            PetBehaviorId.Resting => "Resting",
            PetBehaviorId.Chasing => "Chasing",
            _ => "Starting",
        };

        private static string InstructionFor(TrackingUiState tracking) => tracking switch
        {
            TrackingUiState.Searching => "1. Show the full den card to the camera\n2. Hold still until the pet appears",
            TrackingUiState.Tracking => "Den found. Keep this card visible.\nPlace the food card nearby to feed",
            TrackingUiState.Recovered => "Tracking recovered. Keep the den card visible.",
            TrackingUiState.Lost => "Move closer and show the full den card\nwith even light and no glare",
            _ => "Show the full den card to the camera",
        };
    }
}
