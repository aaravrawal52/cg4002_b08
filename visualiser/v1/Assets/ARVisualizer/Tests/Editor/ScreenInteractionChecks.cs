using NUnit.Framework;
using UnityEngine;

namespace ARVisualizer.Tests
{
    internal static class ScreenInteractionChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null;
            app.Execute(new VisualizerCommand { command = command }, value => reply = value);
            Assert.IsNotNull(reply);
            return reply;
        }

        public static void Exercise(ARVisualizerApp app, ScreenSurface screen)
        {
            int initialSteps = screen.SizeSteps;
            var rayUV = new Vector2(0.7f, 0.65f);
            app.Hand.SetPointerEnabled(true);
            app.Hand.SetEditorSample(app.ARCamera.WorldToViewportPoint(screen.WorldPoint(rayUV)), null);
            var status = Send(app, "status");
            Assert.AreEqual(screen.Id, status.pointedScreenId);
            Assert.AreEqual("ray", status.interaction.kind);
            Assert.That(Vector2.Distance(rayUV, screen.InputUV), Is.LessThan(0.001f));
            Assert.IsTrue(Send(app, "adjust.enter").ok);
            Assert.IsFalse(app.PlaceModeEnabled);
            Assert.AreSame(screen, app.AdjustedScreen);
            Assert.IsTrue(Send(app, "adjust.enter").ok, "Entering again must retain the selection");

            // Selection remains locked while the user looks away.
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            Assert.IsTrue(Send(app, "adjust.grow").ok);
            Assert.AreEqual(initialSteps + 1, screen.SizeSteps);
            Assert.That(screen.Width / screen.Height, Is.EqualTo(16f / 9).Within(0.00001f));
            Assert.IsNull(screen.GetComponent<Collider>(), "Flat screens use analytic ray/touch tests");
            Assert.IsTrue(Send(app, "adjust.shrink").ok);
            Assert.AreEqual(initialSteps, screen.SizeSteps);
            var before = screen.transform.rotation;
            var rotate = Send(app, "adjust.rotate.cw");
            Assert.AreEqual(screen.CanRotate, rotate.ok);
            if (screen.CanRotate)
            {
                Assert.That(Quaternion.Angle(before, screen.transform.rotation), Is.EqualTo(5).Within(0.01f));
                Assert.IsTrue(Send(app, "adjust.rotate.ccw").ok);
            }

            var tipUV = new Vector2(0.25f, 0.3f);
            Vector2 root = app.ARCamera.WorldToViewportPoint(screen.WorldPoint(rayUV));
            app.Hand.SetEditorSample(root, screen.WorldPoint(tipUV));
            status = Send(app, "status");
            Assert.AreEqual("touch", status.interaction.kind, "Fingertip touch takes priority over the knuckle ray");
            Assert.That(Vector2.Distance(tipUV, screen.InputUV), Is.LessThan(0.001f));
            Assert.That(status.interaction.xMetres, Is.EqualTo(tipUV.x * screen.Width).Within(0.0001f));
            app.Hand.SetEditorSample(root, null);
            Assert.AreEqual("ray", Send(app, "status").interaction.kind, "Missing depth must end touch and restore ray input");

            app.Hand.SetPointerEnabled(false);
            app.Hand.SetEditorSample(root, screen.WorldPoint(tipUV));
            Assert.AreEqual("touch", Send(app, "status").interaction.kind, "Touch remains available with the ray off");
            Assert.IsTrue(Send(app, "adjust.exit").ok);
            Assert.IsFalse(Send(app, "adjust.enter").ok, "Touch alone does not select a ray target for adjustment");
            Assert.IsFalse(Send(app, "adjust.grow").ok);
            app.Hand.SetEditorSample(new Vector2(-1, -1), null);
            Assert.AreEqual("none", Send(app, "status").interaction.kind);
            Assert.AreEqual(ScreenInputKind.None, screen.InputKind);

            // A custom prefab can hide itself in response to pointing. It must not be
            // selected by the adjust command that triggered that same input update.
            System.Action<ScreenSurface, ScreenInputKind, Vector2> hideOnRay = (surface, kind, uv) =>
            { if (kind == ScreenInputKind.Ray) surface.gameObject.SetActive(false); };
            screen.InputChanged += hideOnRay;
            app.Hand.SetPointerEnabled(true);
            app.Hand.SetEditorSample(root, null);
            var disabledTarget = Send(app, "adjust.enter");
            Assert.IsFalse(disabledTarget.ok);
            Assert.AreEqual(0, disabledTarget.pointedScreenId);
            Assert.AreEqual("none", disabledTarget.interaction.kind);
            Assert.IsNull(app.InputScreen);
            Assert.IsFalse(app.AdjustModeEnabled);
            screen.InputChanged -= hideOnRay;
            screen.gameObject.SetActive(true);

            app.Hand.ClearEditorSample();
            app.SendLocal("pointer.off");
            app.SendLocal("place.enter");
        }
    }
}
