using UnityEngine;
using StarEco.Player;
using StarEco.UI;

namespace StarEco.Combat
{
    /// <summary>
    /// 玩家战斗组件（T3）：持有 HP、实现 IHittable、采集攻击输入。
    /// 伤害入口唯一：TakeDamage → PlayerController.ApplyHit（M2 的受击/无敌/死亡流水线）。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerCombat : MonoBehaviour, IHittable
    {
        [SerializeField] private CombatConfig _config;

        private PlayerController _player;

        public CombatConfig Config => _config;

        /// <summary>当前 HP（整数定点，单位「半心点」：2 点 = 1 颗心；远程 1 点伤害 = 半颗心）。</summary>
        public int CurrentHP { get; private set; }

        public int MaxHP => _config != null ? _config.MaxHP : 10;

        /// <summary>本帧攻击输入。状态机各状态每帧消费；未被消费则随下帧刷新（不会带过下一帧）。</summary>
        public bool AttackPressedThisFrame { get; private set; }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
            if (_config == null)
            {
                _config = Resources.Load<CombatConfig>("CombatConfig") ?? CombatConfig.CreateDefault();
            }

            CurrentHP = MaxHP;
        }

        private void Update()
        {
            // 每帧刷新：死亡/锁定输入时不接收
            AttackPressedThisFrame = _player != null && !_player.IsDead && !_player.InputLocked
                && (Input.GetButtonDown("Fire1") || Input.GetKeyDown(KeyCode.J));
        }

        /// <summary>状态机消费攻击输入：仅当前帧有效，消费即清。</summary>
        public bool ConsumeAttackPressed()
        {
            if (!AttackPressedThisFrame)
            {
                return false;
            }

            AttackPressedThisFrame = false;
            return true;
        }

        // ===== IHittable =====

        public void TakeDamage(DamageInfo info)
        {
            if (_player == null || _player.IsDead)
            {
                return;
            }

            // 对话期间玩家免伤：攻击侧（弹体/判定盒/状态机）已有门控，
            // 这里在受击入口再兜一层，覆盖环境伤害等所有漏网来源。
            if (DialoguePanel.IsDialogueActive)
            {
                return;
            }

            if (_player.InvincibleTimer > 0f)
            {
                return;   // 无敌期忽略（R5）
            }

            int newHp = Mathf.Max(0, CurrentHP - info.Amount);
            CurrentHP = newHp;

            // 击退方向：无来源方向时按垂直上抛兜底
            Vector2 from = info.SourceDirection.sqrMagnitude > 0.0001f ? info.SourceDirection : Vector2.up;
            _player.ApplyHit(from, newHp, MaxHP);
        }
    }
}
