using UnityEngine;

namespace ZQXNXS.ARPet.App
{
    /// <summary>
    /// 一只宠物的触屏命中区域标记。挂在宠物可视根（或它的子物体）上，
    /// 由 <see cref="TouchInputBridge"/> 在按下/抬起时收集并按射线判定归属哪只宠物。
    ///
    /// 为什么用独立组件而不是让桥接脚本直接引用 Collider：
    /// 多宠同屏时"一个碰撞体字段"表达不了"这一下点到的是哪一只"，
    /// 而多个 <see cref="PetHitTarget"/> 各自带 petId，才能把命中结果路由到对应宠物。
    ///
    /// 命中判定优先用碰撞体；没有碰撞体时退化为一个竖直胶囊：从本物体原点（角色脚底）沿局部 +Y
    /// 向上 <see cref="HitHeight"/>，半径 <see cref="HitRadius"/>，正好包住整只奶蛙，不是精确物理形状。
    /// 2026-10-30 之前是"以脚底为球心"的球，只覆盖下半身，点头和肚子没反应（真机 BUG-02）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetHitTarget : MonoBehaviour
    {
        [Tooltip("命中区域归属的宠物稳定 ID（如 pet_classic），必须与身份目录一致。为空表示不参与命中判定。")]
        [SerializeField] private string petId = "pet_classic";

        [Tooltip("可选碰撞体。留空时按“脚底到头顶的竖直胶囊”近似判定。")]
        [SerializeField] private Collider hitCollider;

        [Tooltip("没有碰撞体时近似胶囊的半径（米）。奶蛙腹宽约 0.132m，取 0.08 留出手指误差。")]
        [SerializeField] private float hitRadiusMeters = 0.08f;

        [Tooltip("没有碰撞体时近似胶囊的高度（米），从本物体原点（脚底）沿局部 +Y 量起。奶蛙身高 0.2m。")]
        [SerializeField] private float hitHeightMeters = 0.2f;

        /// <summary>归属宠物的稳定 ID。</summary>
        public string PetId => petId;

        /// <summary>显式配置的碰撞体；为空表示走球形近似。</summary>
        public Collider HitCollider => hitCollider;

        /// <summary>没有碰撞体时近似胶囊的半径（米）。</summary>
        public float HitRadius => hitRadiusMeters;

        /// <summary>没有碰撞体时近似胶囊的高度（米），从脚底量起。</summary>
        public float HitHeight => hitHeightMeters;

        /// <summary>
        /// 近似胶囊的中轴线段（世界坐标）与半径。胶囊整体从脚底到头顶，
        /// 所以线段两端各向内收一个半径；高度不足两倍半径时退化成一个球。
        /// 跟随本物体的旋转与缩放（ImageTarget 倾斜时胶囊也跟着倾斜）。
        /// </summary>
        public void GetCapsule(out Vector3 bottom, out Vector3 top, out float radius)
        {
            var scale = transform.lossyScale;
            var factor = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            radius = Mathf.Max(0f, hitRadiusMeters) * factor;

            var height = Mathf.Max(0f, hitHeightMeters);
            var r = Mathf.Max(0f, hitRadiusMeters);
            var inset = Mathf.Min(r, height * 0.5f);
            bottom = transform.TransformPoint(new Vector3(0f, inset, 0f));
            top = transform.TransformPoint(new Vector3(0f, height - inset, 0f));
        }

        private void Awake()
        {
            // 没显式指定时用同物体上的碰撞体（常见情况是模型根自带的胶囊/球体），
            // 仍然找不到才退化到球形近似，不需要调用方额外配置。
            if (hitCollider == null) hitCollider = GetComponent<Collider>();
        }

        private void Reset()
        {
            hitCollider = GetComponent<Collider>();
        }
    }
}
