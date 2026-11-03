using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.UI
{
    /// <summary>
    /// 手机首轮测试用的只读状态面板。只依赖 Core 契约，不引用 App 装配层。
    ///
    /// 多宠：跟踪 / 行为 / 开心值三栏显示的是<b>选中宠物</b>（<see cref="IAppUiState.SelectedPetId"/>）；
    /// 可选的 <c>petListText</c> 逐行列出全部宠物的变体、跟踪状态与开心值百分比，选中项前加 "▶"；
    /// 可选的 <c>happinessBar</c> 是选中宠物的开心值进度条（Image.type = Filled / Horizontal）。
    ///
    /// <b>百分比一律向下取整</b>：99.9% 显示 99%，进度条也只填到 99%，
    /// 只有内部值真的 ≥ 1 才显示 100% 和满条。满值必须在玩家真正点满时才算发生，
    /// 界面提前显示"已满"会让人以为专属演出漏播了。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetStatusPanel : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour stateSourceBehaviour;
        [SerializeField] private Text trackingText;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text behaviorText;
        [SerializeField] private Text happinessText;

        [Tooltip("可选：多宠列表文本。每行“变体名 + 跟踪状态 + 开心值”，选中宠物前加 ▶。")]
        [SerializeField] private Text petListText;

        [Tooltip("可选：选中宠物的开心值进度条。Image.type 应为 Filled、FillMethod 为 Horizontal。")]
        [SerializeField] private Image happinessBar;

        [Tooltip("可选：帧率文本。读 fpsSourceBehaviour 的 CurrentFps（实现 IFrameRateSource）。")]
        [SerializeField] private Text fpsText;

        [SerializeField] private MonoBehaviour fpsSourceBehaviour;

        private readonly StringBuilder _listBuilder = new();

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
                SetText(petListText, string.Empty);
                RefreshHappinessBar(0f);
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
            var behaviorNow = _stateSource.CurrentBehavior;
            var percentNow = PercentValue(state.Happiness);
            // 文本只在显示内容变化时重建：面板每帧刷新，拼接字符串会持续产生垃圾。
            if (behaviorNow != _shownBehavior || state.HappyCycle != _shownCycle)
            {
                _shownBehavior = behaviorNow;
                _shownCycle = state.HappyCycle;
                SetText(behaviorText, "Action  " + FormatBehavior(behaviorNow, state.HappyCycle));
            }

            if (percentNow != _shownPercent)
            {
                _shownPercent = percentNow;
                SetText(happinessText, "Happy  " + percentNow + "%");
            }

            RefreshHappinessBar(state.Happiness);

            RefreshPetList();
            RefreshFps();
        }

        private int _shownFps = -1;
        private PetBehaviorId _shownBehavior = (PetBehaviorId)(-1);
        private HappyCycleState _shownCycle = (HappyCycleState)(-1);
        private int _shownPercent = -1;
        private int _shownListSignature = int.MinValue;

        /// <summary>帧率只在数值变化时重写文本，避免每帧产生字符串。</summary>
        private void RefreshFps()
        {
            if (fpsText == null || !(fpsSourceBehaviour is IFrameRateSource source)) return;
            var fps = Mathf.RoundToInt(source.CurrentFps);
            if (fps == _shownFps) return;
            _shownFps = fps;
            fpsText.text = fps > 0 ? fps + " FPS" : string.Empty;
        }

        /// <summary>逐行列出全部宠物；选中宠物前加 "▶"。没有分配文本组件时整段跳过。</summary>
        private void RefreshPetList()
        {
            if (petListText == null) return;

            if (_stateSource == null)
            {
                SetText(petListText, string.Empty);
                return;
            }

            var pets = _stateSource.Pets;
            if (pets == null || pets.Count == 0)
            {
                SetText(petListText, string.Empty);
                return;
            }

            var selected = _stateSource.SelectedPetId;

            // 先算一个"列表签名"（选中项、各行变体/跟踪/百分比），没变就不重建字符串。
            var signature = pets.Count;
            for (var i = 0; i < pets.Count; i++)
            {
                var pet = pets[i];
                var isSelected = string.Equals(pet.PetId, selected, StringComparison.Ordinal);
                signature = signature * 31 + (isSelected ? 1 : 0);
                signature = signature * 31 + (int)pet.Variant;
                signature = signature * 31 + (int)pet.Tracking;
                signature = signature * 31 + PercentValue(pet.State.Happiness);
            }

            if (signature == _shownListSignature) return;
            _shownListSignature = signature;

            _listBuilder.Clear();
            for (var i = 0; i < pets.Count; i++)
            {
                var pet = pets[i];
                if (i > 0) _listBuilder.Append('\n');
                if (string.Equals(pet.PetId, selected, StringComparison.Ordinal)) _listBuilder.Append("▶ ");
                _listBuilder.Append(VariantName(pet.Variant));
                _listBuilder.Append("  ");
                _listBuilder.Append(TrackingShortName(pet.Tracking));
                _listBuilder.Append("  ");
                _listBuilder.Append(PercentValue(pet.State.Happiness)).Append('%');
            }

            petListText.text = _listBuilder.ToString();
        }

        /// <summary>
        /// 选中宠物的开心条。用 <see cref="PercentValue"/> 换算，和文字百分比同一个口径：
        /// 0.999 只填到 0.99，视觉上不会提前变满。
        /// </summary>
        private void RefreshHappinessBar(float happiness)
        {
            if (happinessBar == null) return;
            happinessBar.fillAmount = PercentValue(happiness) / 100f;
        }

        /// <summary>
        /// 开心值百分比：<b>向下取整</b>到整数。NaN / 负数 / 无穷都按 0 或 100 安全处理。
        /// 内部加 1e-4 只是为了抵消 float 乘法误差（如 0.06f × 100 略小于 6），不改变取整方向。
        /// </summary>
        public static int PercentValue(float value)
        {
            if (float.IsNaN(value) || value <= 0f) return 0;
            if (value >= 1f) return 100;

            var scaled = Mathf.FloorToInt(value * 100f + 0.0001f);
            return scaled < 0 ? 0 : (scaled > 99 ? 99 : scaled);
        }

        /// <summary>开心值百分比文本，例如 <c>0.06 → "6%"</c>。</summary>
        public static string Percent(float value) => PercentValue(value) + "%";

        /// <summary>
        /// 行为栏文字。满值待确认（读档时已满）优先提示"点一下确认"，
        /// 其次区分专属演出（Happy!）、共同大笑与忙碌（Busy = 触屏反应执行中）。
        /// </summary>
        public static string FormatBehavior(PetBehaviorId behavior, HappyCycleState cycle)
        {
            if (cycle == HappyCycleState.PendingConfirm) return "Full - tap to celebrate";

            return behavior switch
            {
                PetBehaviorId.Exclusive => "Happy!",
                PetBehaviorId.Social => "Laughing together!",
                PetBehaviorId.Petted => "Busy",
                PetBehaviorId.Idle => "Idle",
                PetBehaviorId.Resting => "Resting",
                PetBehaviorId.Chasing => "Chasing",
                _ => "Starting",
            };
        }

        private static string VariantName(PetVariantId variant) => variant switch
        {
            PetVariantId.Classic => "暖黄",
            PetVariantId.Calm => "湖蓝",
            PetVariantId.Rage => "朱红",
            PetVariantId.Divine => "紫",
            _ => "未知",
        };

        private static string TrackingShortName(TrackingUiState tracking) => tracking switch
        {
            TrackingUiState.Searching => "寻找中",
            TrackingUiState.Tracking => "在场",
            TrackingUiState.Lost => "丢失",
            TrackingUiState.Recovered => "恢复",
            _ => "未知",
        };

        private static void SetText(Text target, string value)
        {
            if (target != null) target.text = value;
        }

        private static string InstructionFor(TrackingUiState tracking) => tracking switch
        {
            TrackingUiState.Searching => "1. Point the camera at a character card\n2. Hold still until the pet appears",
            TrackingUiState.Tracking => "Card found. Keep this card visible.\nTap the pet to play",
            TrackingUiState.Recovered => "Tracking recovered. Keep the card visible.",
            TrackingUiState.Lost => "Move closer and show the full card\nwith even light and no glare",
            _ => "Point the camera at a character card",
        };
    }
}
