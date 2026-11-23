using Ring.Characters;

namespace Ring.Abilities
{
    /// <summary>
    /// 把「角色基础数值资产」适配成能力系统认识的 <see cref="IAbilityStats"/>。
    ///
    /// 这一层存在的意义：AbilityRuntime 是纯 C#（能离线测），不能直接引用 ScriptableObject；
    /// 而 CharacterBaseStats 是 UnityEngine 资产。中间隔一个适配器，两边都干净。
    ///
    /// 资产为空时退回到文档快照（<see cref="CharacterStatBlock.FromDocument"/>），
    /// 这样即使场景里没拖资产，数值也是有意义的，不会全员 0 血。
    /// </summary>
    public sealed class CharacterStatAdapter : IAbilityStats
    {
        private readonly CharacterBaseStats source;
        private readonly CharacterStatBlock fallback;

        public CharacterStatAdapter(CharacterBaseStats source)
        {
            this.source = source;
            fallback = CharacterStatBlock.FromDocument(source != null ? source.Persona : PersonaId.Swordsman);
        }

        public CharacterStatAdapter(PersonaId persona)
        {
            source = null;
            fallback = CharacterStatBlock.FromDocument(persona);
        }

        /// <summary>被适配的资产；为 null 表示走文档快照。</summary>
        public CharacterBaseStats Source
        {
            get { return source; }
        }

        public PersonaId Persona
        {
            get { return source != null ? source.Persona : fallback.Persona; }
        }

        public float MaxHealth
        {
            get { return source != null ? source.MaxHealth : fallback.MaxHealth; }
        }

        public float Defense
        {
            get { return source != null ? source.Defense : fallback.Defense; }
        }

        public float DamageReduction
        {
            get { return source != null ? source.DamageReduction : fallback.DamageReduction; }
        }

        public float Speed
        {
            get { return source != null ? source.Speed : fallback.Speed; }
        }

        public float MoveSpeedCellsPerSecond
        {
            get { return source != null ? source.MoveSpeedCellsPerSecond : fallback.MoveSpeedCellsPerSecond; }
        }

        public float JumpHeightCells
        {
            get { return source != null ? source.JumpHeightCells : fallback.JumpHeightCells; }
        }

        public float AttackIntervalSeconds
        {
            get { return source != null ? source.AttackIntervalSeconds : fallback.AttackIntervalSeconds; }
        }

        /// <summary>当前数值的快照（UI、存档、离线比对用）。</summary>
        public CharacterStatBlock ToBlock()
        {
            return new CharacterStatBlock(
                Persona,
                MaxHealth,
                Defense,
                DamageReduction,
                Speed,
                MoveSpeedCellsPerSecond,
                JumpHeightCells,
                AttackIntervalSeconds);
        }
    }
}
