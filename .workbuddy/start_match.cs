var f = Ring.Flow.GameFlow.Instance;
if (f == null) return "no flow";
bool started = f.StartMatch();
return "startMatch=" + started + " state=" + f.State;
