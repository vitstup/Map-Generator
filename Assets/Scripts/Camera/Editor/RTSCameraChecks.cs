using System;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

internal static class RTSCameraChecks
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Tools/Tactical Maps/Verify RTS Camera")]
    internal static void Verify()
    {
        var root = new GameObject("Camera checks"); root.SetActive(false);
        var controller = root.AddComponent<RTSCameraController>();
        root.AddComponent<CinemachineCamera>(); var orbit = root.AddComponent<CinemachineOrbitalFollow>();
        var target = new GameObject("Target").transform; target.SetParent(root.transform, false);
        var report = new StringBuilder();
        try
        {
            Set("target", target); Set("orbitalFollow", orbit); Set("zoomSmoothTime", .18f);
            orbit.RadialAxis.Range = new Vector2(.005f, 1);
            Reset(.8f); Step(-1, 1f / 60); float first = orbit.RadialAxis.Value, requested = Get<float>("_zoomTarget");
            Check(first < .8f && first > requested, "One wheel step eases towards its destination instead of jumping");
            float previous = first;
            for (int i = 0; i < 120; i++)
            {
                Step(0, 1f / 60); float value = orbit.RadialAxis.Value;
                Check(value <= previous + .000001f && value >= requested - .000001f, "Zoom continues smoothly without new wheel input and never overshoots");
                previous = value;
            }
            Check(Mathf.Abs(previous - requested) < .00001f, "Zoom settles at the requested value");
            report.AppendLine($"PASS: single scroll first-frame value {first:F6}, destination {requested:F6}, settled {previous:F6}.");
            float baseline = 0;
            foreach (int fps in new[] { 30, 60, 144 })
            {
                Reset(.8f); Step(-1, 1f / fps);
                for (int i = 1; i < fps; i++) Step(0, 1f / fps);
                float value = orbit.RadialAxis.Value;
                if (fps == 30) baseline = value; else Check(Mathf.Abs(value - baseline) < .00002f, "Consistent zoom at 30/60/144 FPS");
            }
            Reset(.8f); Step(-.25f, 1f / 60); float fractional = Get<float>("_zoomTarget");
            Check(fractional < .8f && fractional > requested, "Fractional wheel input retained");
            Reset(.8f); Step(-3, 1f / 60); Check(Get<float>("_zoomTarget") < requested, "Multi-notch wheel input retained");
            for (int i = 0; i < 5; i++) Step(0, 1f / 60);
            float beforeReverse = orbit.RadialAxis.Value; Step(1, 1f / 60);
            Check(orbit.RadialAxis.Value >= beforeReverse, "Reversing zoom cancels old-direction inertia");
            orbit.RadialAxis.Value = .4f; Step(0, 1f / 60);
            Check(Mathf.Abs(orbit.RadialAxis.Value - .4f) < .000001f, "External axis changes do not resume an old zoom request");
            orbit.RadialAxis.Range = new Vector2(2, 5); Reset(3);
            Step(-100, 1f / 60); for (int i = 0; i < 120; i++) Step(0, 1f / 60);
            Check(Mathf.Abs(orbit.RadialAxis.Value - 2) < .00001f, "Minimum of a custom radial range");
            Step(100, 1f / 60); for (int i = 0; i < 120; i++) Step(0, 1f / 60);
            Check(Mathf.Abs(orbit.RadialAxis.Value - 5) < .00001f, "Maximum of a custom radial range");
            Set("zoomSmoothTime", 0f); Reset(3); Step(-1, 1f / 60);
            Check(orbit.RadialAxis.Value == Get<float>("_zoomTarget"), "Optional instant zoom");
            report.AppendLine("PASS: frame-rate consistency, fractional/fast input, reversal, external edits and radial limits.");

            Set("limitMovement", true); Set("movementBoundsCenter", new Vector3(23, 999, -41)); Set("movementBoundsDistance", 10f);
            foreach (var point in new[] { new Vector3(100, 7, -41), new Vector3(-100, 7, -41), new Vector3(23, 7, 100), new Vector3(23, 7, -100), new Vector3(100, 7, -100), new Vector3(25, 7, -39) })
            {
                target.position = point; Clamp(); var value = target.position;
                Check(value.x >= 13 && value.x <= 33 && value.z >= -51 && value.z <= -31, "All sides and corners clamp in world XZ");
                Check(value.y == point.y, "Square never clamps height");
            }
            target.position = new Vector3(34, 7, -41); Set("_currentVelocity", new Vector3(8, 0, 3)); Set("_moveSmoothVelocity", new Vector3(5, 0, 4)); Clamp();
            Check(Get<Vector3>("_currentVelocity") == new Vector3(0, 0, 3) && Get<Vector3>("_moveSmoothVelocity") == new Vector3(0, 0, 4), "Wall removes outward inertia while preserving edge sliding");
            Set("limitMovement", false); target.position = new Vector3(100, 123, -100); Clamp();
            Check(target.position == new Vector3(100, 123, -100), "Disabled bounds leave the target free");
            Set("limitMovement", true); Set("movementBoundsDistance", 0f); Clamp();
            Check(target.position == new Vector3(23, 123, -41), "Zero distance clamps only XZ to centre");
            report.AppendLine("PASS: square centre/distance/toggle, all edges/corners, height preservation and boundary velocity handling.");
        }
        catch (Exception e) { report.AppendLine("FAIL: " + e); throw; }
        finally
        {
            Directory.CreateDirectory("Temp/CameraChecks"); File.WriteAllText("Temp/CameraChecks/checks.txt", report.ToString());
            UnityEngine.Object.DestroyImmediate(root);
        }
        void Set(string name, object value) => typeof(RTSCameraController).GetField(name, Private).SetValue(controller, value);
        T Get<T>(string name) => (T)typeof(RTSCameraController).GetField(name, Private).GetValue(controller);
        void Step(float scroll, float dt) => typeof(RTSCameraController).GetMethod("UpdateZoom", Private).Invoke(controller, new object[] { scroll, dt });
        void Clamp() => typeof(RTSCameraController).GetMethod("ClampTargetToBounds", Private).Invoke(controller, null);
        void Reset(float value) { orbit.RadialAxis.Value = value; Set("_zoomInitialized", false); Set("_zoomVelocity", 0f); }
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
