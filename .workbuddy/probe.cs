var sb = new System.Text.StringBuilder();
sb.Append("isPlaying=").Append(UnityEditor.EditorApplication.isPlaying);
sb.Append(" | focused=").Append(UnityEngine.Application.isFocused);
sb.Append(" | frame=").Append(UnityEngine.Time.frameCount);
var flow = Ring.Flow.GameFlow.Instance;
sb.Append(" | state=").Append(flow != null ? flow.State.ToString() : "null");
var player = flow != null ? flow.Player : null;
sb.Append(" | player=").Append(player != null ? player.DisplayName : "null");
if (player != null)
{
    var body = player.GetComponent<Ring.Flow.CombatBody>();
    sb.Append(" | facing=").Append(body.FacingVector.ToString());
    sb.Append(" sign=").Append(body.FacingSign);
    sb.Append(" groundLock=").Append(body.IsGroundLocked);
    var vis = player.transform.Find("JumpOffset/Visual");
    if (vis != null)
    {
        foreach (var c in vis.GetComponents<Component>()) sb.Append(" [").Append(c.GetType().Name).Append("]");
        var sr = vis.GetComponent<UnityEngine.SpriteRenderer>();
        if (sr != null) sb.Append(" flipX=").Append(sr.flipX).Append(" sprite=").Append(sr.sprite != null ? sr.sprite.name : "null");
    }
    var caster = player.GetComponent<Ring.Flow.AbilityCaster>();
    sb.Append(" | caster=").Append(caster != null);
}
var gi = Ring.Inputs.GameInput.Instance;
if (gi != null)
{
    sb.Append(" | aimValid=").Append(gi.HasValidAim);
    sb.Append(" aim=").Append(gi.AimWorldPoint.ToString());
}
sb.Append(" | mouse=").Append(UnityEngine.Input.mousePosition.ToString());
sb.Append(" | screen=").Append(UnityEngine.Screen.width).Append("x").Append(UnityEngine.Screen.height);
var es = UnityEngine.EventSystems.EventSystem.current;
sb.Append(" | overUI=").Append(es != null ? es.IsPointerOverGameObject().ToString() : "noES");
var cam = UnityEngine.Camera.main;
sb.Append(" | camPixelRect=").Append(cam != null ? cam.pixelRect.ToString() : "nocam");
if (cam != null && player != null)
{
    sb.Append(" | playerScreen=").Append(cam.WorldToScreenPoint(player.transform.position).ToString());
}
return sb.ToString();
