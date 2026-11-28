using System.Reflection;
using UnityEditor;
using UnityEngine;

AssetDatabase.Refresh();
var m1 = typeof(Ring.Inputs.GameInput).GetMethod("TryProjectPointerToPlane", BindingFlags.Public | BindingFlags.Instance);
var m2 = typeof(Ring.Inputs.GameInput).GetMethod("IsPointerInsideViewport", BindingFlags.NonPublic | BindingFlags.Static);
return "isPlaying=" + EditorApplication.isPlaying
       + " isCompiling=" + EditorApplication.isCompiling
       + " TryProjectPointerToPlane=" + (m1 != null)
       + " IsPointerInsideViewport=" + (m2 != null);
