using System.Reflection;
using UnityEngine;

var flow = Ring.Flow.GameFlow.Instance;
var player = flow != null ? flow.Player : null;
if (player == null) return "NO PLAYER state=" + (flow != null ? flow.State.ToString() : "null");

var body = player.GetComponent<Ring.Flow.CombatBody>();
var caster = player.GetComponent<Ring.Flow.AbilityCaster>();
var input = Ring.Inputs.GameInput.Instance;
var t = typeof(Ring.Inputs.GameInput);
var fAim = t.GetField("aimWorldPoint", BindingFlags.NonPublic | BindingFlags.Instance);
var fValid = t.GetField("aimValid", BindingFlags.NonPublic | BindingFlags.Instance);

Vector3 pos = player.transform.position;
fAim.SetValue(input, new Vector2(pos.x - 3f, pos.y + 0.3f));
fValid.SetValue(input, true);
bool fired = caster.BasicAttack();

var vis = player.transform.Find("JumpOffset/Visual");
var sr = vis != null ? vis.GetComponent<SpriteRenderer>() : null;
return "LEFT fired=" + fired + " facing=" + body.FacingVector + " sign=" + body.FacingSign
       + " flipX=" + (sr != null ? sr.flipX.ToString() : "null")
       + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "null");
