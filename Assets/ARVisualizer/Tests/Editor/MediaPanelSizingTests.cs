using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    public sealed class MediaPanelSizingTests
    {
        [TestCase(0.25f, 0)]
        [TestCase(1f, 0)]
        [TestCase(3f, 0)]
        [TestCase(0.5f, 40)]
        [TestCase(1.5f, 40)]
        public void PanelMaintainsVisualAngleAtDifferentDistancesAndViewAngles(float distance, float yaw)
        {
            var rotation = Quaternion.Euler(0, yaw, 0);
            var bottom = new Vector3(0.1f, -0.15f, distance);
            float width = ScreenAppMenu.WidthForVisualAngle(Vector3.zero, bottom, rotation, 1.5f, 38, 0.08f, 4);
            var centre = bottom + rotation * Vector3.up * (width / 3);
            float angle = Vector3.Angle(centre - rotation * Vector3.right * width * 0.5f,
                centre + rotation * Vector3.right * width * 0.5f);
            Assert.That(angle, Is.EqualTo(38).Within(0.03f));
        }
        [Test] public void PhysicalLimitsBoundVeryNearFarAndEdgeOnPanels()
        {
            Assert.AreEqual(0.08f, ScreenAppMenu.WidthForVisualAngle(Vector3.zero, Vector3.forward * 0.01f, Quaternion.identity, 1.5f, 38, 0.08f, 4));
            Assert.AreEqual(4, ScreenAppMenu.WidthForVisualAngle(Vector3.zero, Vector3.forward * 50, Quaternion.identity, 1.5f, 38, 0.08f, 4));
            float width = ScreenAppMenu.WidthForVisualAngle(Vector3.zero, Vector3.forward, Quaternion.Euler(0, 90, 0), 1.5f, 38, 0.08f, 4);
            Assert.That(width, Is.InRange(0.08f, 4));
        }
    }
}
