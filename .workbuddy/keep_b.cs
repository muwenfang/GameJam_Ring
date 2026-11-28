using System.Reflection;
using UnityEngine;

var flow = Ring.Flow.GameFlow.Instance;
var player = flow != null ? flow.Player : null;
if (player == null) return "NO PLAYER state=" + (flow != null ? flow.State.ToString() : "null");

var body = player.GetComponent<Ring.Flow.CombatBody>();
var caster = player.GetComponent<Ring.Flow.AbilityCaster>();
var input = Ring.Inputs.GameInput.Instance;
var t = typeof(Ring.Inputs.GameInput);
Vector3 p = player.transform.position;

// 场景二：角色朝左，鼠标在角色右边 → 点击后仍应朝左
body.SetFacing(new Ring.Combat.Vec2(-1f, 0f));
t.GetField("aimWorldPoint", BindingFlags.NonPublic | BindingFlags.Instance)
 .SetValue(input, new Vector2(p.x + 3f, p.y + 0.3f));
t.GetField("aimValid", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(input, true);
bool fired = caster.BasicAttack();

var vis = player.transform.Find("JumpOffset/Visual");
var sr = vis != null ? vis.GetComponent<SpriteRenderer>() : null;
return "CASE_B(mouse右,朝左) fired=" + fired
       + " facing=" + body.FacingVector + " sign=" + body.FacingSign
       + " flipX=" + (sr != null ? sr.flipX.ToString() : "null")
       + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "null");
