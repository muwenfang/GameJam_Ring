using UnityEngine;

var flow = Ring.Flow.GameFlow.Instance;
var player = flow != null ? flow.Player : null;
if (player == null) return "NO PLAYER";

var body = player.GetComponent<Ring.Flow.CombatBody>();
var caster = player.GetComponent<Ring.Flow.AbilityCaster>();
var input = Ring.Inputs.GameInput.Instance;

bool fired = caster.BasicAttack();
Vector2 world;
bool projected = input.TryProjectPointerToPlane(out world);

var vis = player.transform.Find("JumpOffset/Visual");
var sr = vis != null ? vis.GetComponent<SpriteRenderer>() : null;
return "NATURAL fired=" + fired
       + " hasValidAim=" + input.HasValidAim + " aim=" + input.AimWorldPoint
       + " pointerProjected=" + projected + " pointerWorld=" + world
       + " playerPos=" + player.transform.position
       + " facing=" + body.FacingVector + " sign=" + body.FacingSign
       + " flipX=" + (sr != null ? sr.flipX.ToString() : "null")
       + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "null");
