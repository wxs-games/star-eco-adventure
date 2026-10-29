namespace StarEco.Combat
{
    /// <summary>
    /// 可被命中的统一接口：M3 玩家实现，M4 敌人实现同接口即接入同一条命中流水线。
    /// 命中逻辑（开窗、去重）在攻击方，受击逻辑（扣血/无敌/硬直/死亡）在实现方。
    /// </summary>
    public interface IHittable
    {
        void TakeDamage(DamageInfo info);
    }
}
