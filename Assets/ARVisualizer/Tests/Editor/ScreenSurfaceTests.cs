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

        [Test] public void PrefabDimensionsAndResizeStepsPreserveRatioAndThickness()
        {
            Assert.That(screen.Width, Is.EqualTo(0.16f).Within(0.000001f));
            Assert.That(screen.Height, Is.EqualTo(0.09f).Within(0.000001f));
            Assert.AreEqual(Color.black, screen.GetComponentInChildren<Renderer>().sharedMaterial.GetColor("_BaseColor"));
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
            Assert.AreEqual(ScreenSurface.Thickness, screen.GetComponent<BoxCollider>().size.z);
            Assert.AreEqual(ScreenSurface.Thickness, screen.transform.Find("Body").localScale.z);
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
