using System;
using System.Collections.Generic;

namespace Ring.Combat
{
    /// <summary>一发弹道的配置。直线、扇形弹丸、折返弹都复用。</summary>
    public sealed class ProjectileSpec
    {
        public int OwnerId;
        public int OpponentFilter = -1;
        public Vec2 Origin;
        public Vec2 Direction = Vec2.Right;
        public float Speed = 8f;
        public float MaxDistance = 5f;
        public float Width = 0.5f;
        public float Damage = 1f;
        public float SecondaryDamage;
        public float Lifetime = 4f;
        public bool Returns;
        public bool StopOnFirst = true;
        public int Tags;
        public float KnockbackCells;
        public float SlowPercent;
        public float SlowSeconds;
        public float StunSeconds;
        public float RootSeconds;
        public float VulnerabilityPercent;
        public float VulnerabilitySeconds;
        public string StatusId = string.Empty;
        public float VisualSize = 0.25f;
        public int Slot;

        /// <summary>
        /// 是否用游侠的箭矢序列帧渲染。由「技能属于哪个人设」决定，和施法者当前长什么样无关：
        /// 形象已经跟着特质走（见 Docs/18），游侠属性的玩家可能顶着别人设的造型，
        /// 但放的是弓，飞出去的仍然应该是箭。
        /// </summary>
        public bool ArrowVisual;

        public CombatStatusSpec[] BuildStatuses()
        {
            var statuses = new List<CombatStatusSpec>(3);
            if (SlowPercent > 0f && SlowSeconds > 0f)
            {
                statuses.Add(CombatStatusSpec.Slow(SlowPercent, SlowSeconds, OwnerId));
            }

            if (StunSeconds > 0f)
            {
                statuses.Add(CombatStatusSpec.Stun(StunSeconds, OwnerId));
            }

            if (RootSeconds > 0f)
            {
                statuses.Add(CombatStatusSpec.Root(RootSeconds, OwnerId));
            }

            if (VulnerabilityPercent > 0f && VulnerabilitySeconds > 0f)
            {
                statuses.Add(CombatStatusSpec.Vulnerability(VulnerabilityPercent, VulnerabilitySeconds, OwnerId));
            }

            return statuses.ToArray();
        }
    }

    public sealed class ProjectileState
    {
        public ProjectileSpec Spec;
        public Vec2 Position;
        public float Traveled;
        public float Age;
        public bool Returning;
        public bool PendingReturn;
        public bool Finished;
        public int Pass;
        public bool PassSpent;
        public readonly HashSet<int> HitIds = new HashSet<int>();

        public bool IsActive => !Finished;

        public ProjectileState(ProjectileSpec spec)
        {
            Spec = spec;
            Position = spec.Origin;
        }

        public void Tick(float deltaTime)
        {
            if (Finished || deltaTime <= 0f)
            {
                return;
            }

            Age += deltaTime;
            if (Spec.Lifetime > 0f && Age >= Spec.Lifetime)
            {
                Finished = true;
                return;
            }

            float step = Math.Max(0f, Spec.Speed) * deltaTime;
            if (PendingReturn)
            {
                // 先让去程的最后一段参与命中结算，下一帧再转头。
                PendingReturn = false;
                Return();
            }

            if (!Returning)
            {
                Position += Spec.Direction * step;
                Traveled += step;
                if (Traveled < Spec.MaxDistance)
                {
                    return;
                }

                if (!Spec.Returns)
                {
                    Finished = true;
                    return;
                }

                PendingReturn = true;
                return;
            }

            Vec2 toOrigin = Spec.Origin - Position;
            float distance = toOrigin.Magnitude;
            if (distance <= step || distance <= CombatMath.Epsilon)
            {
                Position = Spec.Origin;
                Finished = true;
                return;
            }

            Position += toOrigin * (step / distance);
        }

        public void Return()
        {
            Returning = true;
            Pass = 1;
            Traveled = 0f;
            HitIds.Clear();
            PassSpent = false;
        }

        public float DamageForCurrentPass()
        {
            return Returning && Spec.SecondaryDamage > 0f ? Spec.SecondaryDamage : Spec.Damage;
        }

        public override string ToString()
        {
            return "Projectile(" + Position + ", " + (Returning ? "返程" : "去程") + (Finished ? ", 结束" : string.Empty) + ")";
        }
    }

    /// <summary>
    /// 弹道世界：直线命中首个目标即停；折返弹去回各结算一次，每个目标每程只命中一次。
    /// 命中回调负责施加伤害/状态，核心只负责判定与去重。
    /// </summary>
    public sealed class ProjectileWorld
    {
        private readonly List<ProjectileState> projectiles = new List<ProjectileState>(16);

        public IReadOnlyList<ProjectileState> All => projectiles;
        public int Count => projectiles.Count;

        public ProjectileState Spawn(ProjectileSpec spec)
        {
            if (spec == null)
            {
                return null;
            }

            var state = new ProjectileState(spec);
            projectiles.Add(state);
            return state;
        }

        public void SpawnFan(ProjectileSpec template, int count, float totalSpreadDegrees)
        {
            if (template == null)
            {
                return;
            }

            count = Math.Max(1, count);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float angle = (t - 0.5f) * Math.Max(0f, totalSpreadDegrees);
                ProjectileSpec spec = Copy(template);
                spec.Direction = CombatMath.Rotate(template.Direction, angle);
                Spawn(spec);
            }
        }

        /// <summary>推进所有弹道；每个命中回调参数为（弹道, 目标, 命中点, 本程伤害）。</summary>
        public void Tick(float deltaTime, IReadOnlyList<CombatTargetInfo> targets,
            Action<ProjectileState, CombatTargetInfo, Vec2, float> onHit)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < projectiles.Count; i++)
            {
                ProjectileState projectile = projectiles[i];
                if (projectile.Finished)
                {
                    continue;
                }

                Vec2 start = projectile.Position;
                projectile.Tick(deltaTime);
                Vec2 end = projectile.Position;
                if (Vec2.Distance(start, end) <= CombatMath.Epsilon)
                {
                    continue;
                }

                CollectHits(projectile, start, end, targets, onHit);
            }

            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                if (projectiles[i].Finished)
                {
                    projectiles.RemoveAt(i);
                }
            }
        }

        private static void CollectHits(ProjectileState projectile, Vec2 start, Vec2 end,
            IReadOnlyList<CombatTargetInfo> targets, Action<ProjectileState, CombatTargetInfo, Vec2, float> onHit)
        {
            if (targets == null)
            {
                return;
            }

            var candidates = new List<HitCandidate>(4);
            if (projectile.PassSpent)
            {
                return;
            }

            float radius = Math.Max(0.01f, projectile.Spec.Width) * 0.5f;
            for (int i = 0; i < targets.Count; i++)
            {
                CombatTargetInfo target = targets[i];
                if (!target.IsAlive || !target.IsInPlay || target.Id == projectile.Spec.OwnerId)
                {
                    continue;
                }

                if (projectile.Spec.OpponentFilter >= 0 && target.Id != projectile.Spec.OpponentFilter)
                {
                    continue;
                }

                if (projectile.HitIds.Contains(target.Id))
                {
                    continue;
                }

                if (CombatMath.DistancePointToSegment(target.Position, start, end) > radius + target.Radius)
                {
                    continue;
                }

                float along = Vec2.Dot(target.Position - start, (end - start).Normalized);
                candidates.Add(new HitCandidate(target, along));
            }

            candidates.Sort((a, b) => a.Along.CompareTo(b.Along));
            for (int i = 0; i < candidates.Count; i++)
            {
                CombatTargetInfo target = candidates[i].Target;
                projectile.HitIds.Add(target.Id);
                float damage = projectile.DamageForCurrentPass();
                Vec2 point = ClosestPoint(target.Position, start, end);
                onHit?.Invoke(projectile, target, point, damage);
                if (projectile.Spec.StopOnFirst)
                {
                    projectile.PassSpent = true;
                    projectile.Finished = !projectile.Spec.Returns;
                    break;
                }
            }
        }

        public void Clear() => projectiles.Clear();

        private static Vec2 ClosestPoint(Vec2 point, Vec2 start, Vec2 end)
        {
            Vec2 segment = end - start;
            float lengthSquared = segment.SqrMagnitude;
            if (lengthSquared <= CombatMath.Epsilon)
            {
                return start;
            }

            float t = CombatMath.Clamp01(Vec2.Dot(point - start, segment) / lengthSquared);
            return start + segment * t;
        }

        private static ProjectileSpec Copy(ProjectileSpec source)
        {
            return new ProjectileSpec
            {
                OwnerId = source.OwnerId,
                OpponentFilter = source.OpponentFilter,
                Origin = source.Origin,
                Direction = source.Direction,
                Speed = source.Speed,
                MaxDistance = source.MaxDistance,
                Width = source.Width,
                Damage = source.Damage,
                SecondaryDamage = source.SecondaryDamage,
                Lifetime = source.Lifetime,
                Returns = source.Returns,
                StopOnFirst = source.StopOnFirst,
                Tags = source.Tags,
                KnockbackCells = source.KnockbackCells,
                SlowPercent = source.SlowPercent,
                SlowSeconds = source.SlowSeconds,
                StunSeconds = source.StunSeconds,
                RootSeconds = source.RootSeconds,
                VulnerabilityPercent = source.VulnerabilityPercent,
                VulnerabilitySeconds = source.VulnerabilitySeconds,
                StatusId = source.StatusId,
                VisualSize = source.VisualSize,
                Slot = source.Slot,
                ArrowVisual = source.ArrowVisual
            };
        }

        private readonly struct HitCandidate
        {
            public readonly CombatTargetInfo Target;
            public readonly float Along;

            public HitCandidate(CombatTargetInfo target, float along)
            {
                Target = target;
                Along = along;
            }
        }
    }
}
