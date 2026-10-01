using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace ARVisualizer.Tests
{
    public sealed class ScreenSurfaceTests
    {
        ScreenSurface screen;
        [SetUp] public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ARVisualizer/Prefabs/Screen.prefab");
            Assert.IsNotNull(prefab);
            screen = Object.Instantiate(prefab).GetComponent<ScreenSurface>();
            screen.Initialize(7, true);
        }
        [TearDown] public void TearDown() { if (screen != null) Object.DestroyImmediate(screen.gameObject); }

        [Test] public void PrefabDimensionsAndResizeStepsPreserveRatioAndFlatGeometry()
        {
            Assert.That(screen.Width, Is.EqualTo(0.16f).Within(0.000001f));
            Assert.That(screen.Height, Is.EqualTo(0.09f).Within(0.000001f));
            Assert.AreEqual("ARVisualizer/Screen Media", screen.GetComponentInChildren<Renderer>().sharedMaterial.shader.name);
            Assert.AreEqual(1, screen.GetComponentsInChildren<Renderer>(true).Length);
            Assert.IsEmpty(screen.GetComponentsInChildren<Collider>(true));
            var mesh = screen.GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, mesh.vertexCount);
            Assert.AreEqual(6, mesh.triangles.Length);
            Assert.That(mesh.bounds.size.z, Is.LessThan(0.000001f));
            Assert.IsTrue(screen.Resize(1));
            Assert.That(screen.Width, Is.EqualTo(0.176f).Within(0.000001f));
            Assert.That(screen.Height, Is.EqualTo(0.099f).Within(0.000001f));
            for (int i = 0; i < 120; ++i) screen.Resize(-1);
            Assert.AreEqual(1, screen.SizeSteps);
            Assert.IsFalse(screen.Resize(-1));
            for (int i = 0; i < 120; ++i) screen.Resize(1);
            Assert.AreEqual(100, screen.SizeSteps);
            Assert.IsFalse(screen.Resize(1));
            Assert.That(screen.Width / screen.Height, Is.EqualTo(16f / 9).Within(0.00001f));
            Assert.AreEqual(0, ScreenSurface.Thickness);
            Assert.AreEqual(-ScreenSurface.SurfaceOffset, screen.transform.Find("Body").localPosition.z);
        }

        [TestCase(0, 1, 0, true)]
        [TestCase(0, 0, -1, false)]
        [TestCase(0.4f, 0.5f, -0.8f, false)]
        public void PlacementLiesOnSurfaceAndKeepsWallWidthLevel(float x, float y, float z, bool expectedHorizontal)
        {
            Vector3 normal = new Vector3(x, y, z).normalized;
            var pose = ScreenSurface.PlacementPose(Vector3.zero, normal, new Vector3(0, 1, -1), out bool horizontal);
            Assert.AreEqual(expectedHorizontal, horizontal);
            Assert.That(Vector3.Dot(pose.rotation * Vector3.back, normal), Is.GreaterThan(0.999f));
            if (!horizontal) Assert.That(Mathf.Abs(Vector3.Dot(pose.rotation * Vector3.right, Vector3.up)), Is.LessThan(0.00001f));
            screen.Initialize(1, horizontal);
            Assert.AreEqual(horizontal, screen.Rotate(15));
        }

        [TestCase(0.75f)]
        [TestCase(1f)]
        [TestCase(2.4f)]
        public void MediaShapePreservesWidthPoseAndInputAfterResize(float aspect)
        {
            screen.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(45, 20, 15));
            var position = screen.transform.position; var rotation = screen.transform.rotation;
            float originalWidth = screen.Width;
            Assert.IsTrue(screen.SetMediaAspectRatio(aspect));
            Assert.AreEqual(originalWidth, screen.Width);
            Assert.IsTrue(screen.Resize(2));
            Assert.That(screen.Width / screen.Height, Is.EqualTo(aspect).Within(0.00001f));
            Assert.AreEqual(position, screen.transform.position); Assert.AreEqual(rotation, screen.transform.rotation);
            var body = screen.transform.Find("Body");
            Assert.That(body.localScale.y, Is.EqualTo(screen.Height).Within(0.00001f));
            var expected = new Vector2(0.05f, 0.95f);
            var point = screen.WorldPoint(expected);
            Assert.IsTrue(screen.TryRaycast(new Ray(point + screen.FrontNormal, -screen.FrontNormal), 2, out _, out var uv));
            Assert.Less(Vector2.Distance(uv, expected), 0.0001f);
            Assert.IsTrue(screen.TryTouch(point, 0.01f, out _, out uv));
            Assert.Less(Vector2.Distance(uv, expected), 0.0001f);
            var state = ScreenState.From(screen);
            Assert.AreEqual(screen.Height, state.heightMetres);
            Assert.IsFalse(screen.SetMediaAspectRatio(0));
            Assert.IsFalse(screen.SetMediaAspectRatio(float.NaN));
            Assert.IsFalse(screen.SetMediaAspectRatio(float.PositiveInfinity));
            Assert.AreEqual(aspect, screen.AspectRatio);
        }

        [Test] public void RayAndTouchReportTheSameFrontFaceUVAfterResizeAndRotation()
        {
            screen.Resize(3);
            screen.transform.SetPositionAndRotation(new Vector3(1, 0.4f, 2), Quaternion.Euler(25, 35, 17));
            Vector2 expected = new Vector2(0.2f, 0.75f);
            Vector3 point = screen.WorldPoint(expected);
            var ray = new Ray(point + screen.FrontNormal, -screen.FrontNormal);
            Assert.IsTrue(screen.TryRaycast(ray, 2, out var distance, out var uv));
            Assert.That(distance, Is.EqualTo(1).Within(0.0001f));
            Assert.That(Vector2.Distance(expected, uv), Is.LessThan(0.0001f));
            Assert.IsTrue(screen.TryTouch(point + screen.FrontNormal * 0.005f, 0.012f, out _, out uv));
            Assert.That(Vector2.Distance(expected, uv), Is.LessThan(0.0001f));
            Assert.IsFalse(screen.TryTouch(point + screen.FrontNormal * 0.08f, 0.012f, out _, out _));
            Assert.IsFalse(screen.TryTouch(screen.WorldPoint(new Vector2(1.1f, 0.5f)), 0.012f, out _, out _));
            Assert.IsFalse(screen.TryRaycast(new Ray(point - screen.FrontNormal, screen.FrontNormal), 2, out _, out _));
        }

        [Test] public void InputStateTransitionsClearOldCoordinates()
        {
            int events = 0;
            screen.InputChanged += (_, kind, uv) => ++events;
            screen.SetInput(ScreenInputKind.Ray, new Vector2(0.7f, 0.8f));
            screen.SetInput(ScreenInputKind.Touch, new Vector2(0.1f, 0.2f));
            Assert.AreEqual(ScreenInputKind.Touch, screen.InputKind);
            screen.SetInput(ScreenInputKind.None, Vector2.one);
            Assert.AreEqual(Vector2.zero, screen.InputUV);
            screen.SetInput(ScreenInputKind.None, Vector2.one);
            Assert.AreEqual(3, events);
        }

        T Event<T>(string field) where T : class => typeof(ScreenSurface)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(screen) as T;

        [TestCase(ScreenInputKind.Ray)]
        [TestCase(ScreenInputKind.Touch)]
        public void ExitCallbackCanDisableScreenWithoutStartingAnotherInteraction(ScreenInputKind previous)
        {
            screen.SetInput(previous, Vector2.one * 0.5f);
            var notifications = new List<ScreenInputKind>();
            screen.InputChanged += (_, kind, uv) => notifications.Add(kind);
            int starts = 0, ends = 0;
            bool fromRay = previous == ScreenInputKind.Ray;
            Event<UnityEvent>(fromRay ? "rayExited" : "touchEnded").AddListener(() => screen.gameObject.SetActive(false));
            Event<UnityEvent<Vector2>>(fromRay ? "touchStarted" : "rayPointed").AddListener(_ => ++starts);
            Event<UnityEvent>(fromRay ? "touchEnded" : "rayExited").AddListener(() => ++ends);

            screen.SetInput(fromRay ? ScreenInputKind.Touch : ScreenInputKind.Ray, Vector2.one * 0.25f);

            Assert.AreEqual(0, starts, "The callback cancelled the new interaction");
            Assert.AreEqual(0, ends, "An interaction that never began must not end");
            Assert.AreEqual(ScreenInputKind.None, screen.InputKind);
            Assert.AreEqual(Vector2.zero, screen.InputUV);
            CollectionAssert.AreEqual(new[] { ScreenInputKind.None }, notifications);
        }

        [Test] public void TouchStartCallbackCanDisableScreenWithoutReportingStaleTouch()
        {
            var notifications = new List<ScreenInputKind>();
            screen.InputChanged += (_, kind, uv) => notifications.Add(kind);
            int ends = 0;
            Event<UnityEvent<Vector2>>("touchStarted").AddListener(_ => screen.enabled = false);
            Event<UnityEvent>("touchEnded").AddListener(() => ++ends);

            screen.SetInput(ScreenInputKind.Touch, Vector2.one * 0.3f);
            screen.SetInput(ScreenInputKind.Ray, Vector2.one * 0.7f);

            Assert.AreEqual(1, ends);
            Assert.AreEqual(ScreenInputKind.None, screen.InputKind);
            Assert.AreEqual(Vector2.zero, screen.InputUV);
            CollectionAssert.AreEqual(new[] { ScreenInputKind.None }, notifications);
        }
    }
}
