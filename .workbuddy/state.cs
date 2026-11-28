var f = Ring.Flow.GameFlow.Instance;
return "isPlaying=" + UnityEditor.EditorApplication.isPlaying
       + " state=" + (f != null ? f.State.ToString() : "null")
       + " player=" + (f != null && f.Player != null ? f.Player.DisplayName : "null")
       + " frame=" + UnityEngine.Time.frameCount;
