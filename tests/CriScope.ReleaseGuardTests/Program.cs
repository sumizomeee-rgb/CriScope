using System.Reflection;
using CriScope.Unity;

// Structural Release compilation test, not a substitute for a Unity Player build.
var type = typeof(CriScopeDiagnostics);
if (type.Assembly.GetType("CriScope.Unity.CriScopeBridge") != null || type.Assembly.GetType("CriScope.Unity.CriScopeMonitor") != null)
    throw new Exception("Release includes diagnostics transport/private native adapter");
var declared = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
if (type.GetFields(declared).Length != 0 || type.GetMethods(declared).Any(m => m.Name is "Update" or "Sample" or "Beat" or "Sequence" or "Close"))
    throw new Exception("Release includes collection fields or callbacks");
if (CriScopeDiagnostics.SetCaptureEnabled(true) || CriScopeDiagnostics.CaptureEnabled || !CriScopeDiagnostics.SetCaptureEnabled(false))
    throw new Exception("Release stub can enable capture");
Console.WriteLine("PASS: no diagnostic symbols => no transport, adapter, state, Update or callbacks; inert toggle verified");
namespace UnityEngine { public class MonoBehaviour { } }
