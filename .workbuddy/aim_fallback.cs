using System.Reflection;
using UnityEngine;

var flow = Ring.Flow.GameFlow.Instance;
var player = flow != null ? flow.Player : null;
if (player == null) return "NO PLAYER";

var body = player.GetComponent<Ring.Flow.CombatBody>();
var caster = player.GetComponent<Ring.Flow.AbilityCaster>();
var input = Ring.Inputs.GameInput.Instance;
typeof(Ring.Inputs.GameInput)
    .GetField("aimValid", BindingFlags.NonPublic | BindingFlags.Instance)
    .SetValue(input, false);

Vector2 world;
bool projected = input.TryProjectPointerToPlane(out world);
Vector3 playerPos = player.transform.position;

// 只有当指针在视口内、投影成功时，兜底才会改变朝向；否则应退回移动朝向。
bool fired = caster.BasicAttack();

var vis = player.transform.Find("JumpOffset/Visual");
var sr = vis != null ? vis.GetComponent<SpriteRenderer>() : null;
return "FALLBACK projected=" + projected + " pointerWorld=" + world
       + " mouse=" + Input.mousePosition + " screen=" + Screen.width + "x" + Screen.height
       + " playerPos=" + playerPos
       + " expectedSide=" + (projected ? (world.x < playerPos.x ? "left" : "right") : "n/a")
       + " | fired=" + fired
       + " facing=" + body.FacingVector + " sign=" + body.FacingSign
       + " flipX=" + (sr != null ? sr.flipX.ToString() : "null")
       + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "null");
