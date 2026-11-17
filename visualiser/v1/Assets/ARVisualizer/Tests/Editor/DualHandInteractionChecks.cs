using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARVisualizer.Tests
{
    internal static class DualHandInteractionChecks
    {
        static CommandReply Send(ARVisualizerApp app, string command)
        {
            CommandReply reply = null;
            app.Execute(new VisualizerCommand { command = command }, result => reply = result);
            Assert.IsNotNull(reply, command + " should complete immediately");
            return reply;
        }

        static IEnumerator Frames()
        {
            int frame = Time.frameCount;
            float deadline = Time.realtimeSinceStartup + 3;
            while (Time.frameCount < frame + 2 && Time.realtimeSinceStartup < deadline) yield return null;
        }

        static void Aim(ARVisualizerApp app, TrackedHand side, ScreenSurface screen, Vector2 uv, Vector3? tip = null)
        {
            var camera = app.ARCamera;
            var hand = app.Hands.GetHand(side);
            Vector3 viewport = camera.WorldToViewportPoint(screen.WorldPoint(uv));
            viewport.z = 0.25f;
            var root = camera.ViewportToWorldPoint(viewport)
                + camera.transform.right * (side == TrackedHand.Right ? 0.03f : -0.03f);
            hand.SetEditorSample(camera.WorldToViewportPoint(root), tip, side, root);
        }

        static void AimSurface(ARVisualizerApp app, TrackedHand side)
        {
            using var scope = app.UseHand(side);
            for (int y = 1; y <= 9; ++y)
            for (int x = 1; x <= 9; ++x)
            {
                app.Hand.SetEditorSample(new Vector2((side == TrackedHand.Right ? 10 - x : x) / 10f, y / 10f), null);
                Send(app, side.ToString().ToLowerInvariant() + ".status");
                if (app.HasTarget) return;
            }
            Assert.Fail("No surface for " + side);
        }

        public static IEnumerator Exercise(ARVisualizerApp app)
        {
            Send(app, "right.screens.clear");
            Send(app, "left.pointer.on");
            Send(app, "right.pointer.on");
            var prefab = AssetDatabase.LoadAssetAtPath<ScreenSurface>("Assets/ARVisualizer/Prefabs/Screen.prefab");
            var placed = (List<ScreenSurface>)typeof(ARVisualizerApp).GetField("placed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
            var rightScreen = Object.Instantiate(prefab);
            var leftScreen = Object.Instantiate(prefab);
            rightScreen.Initialize(2001, false);
            leftScreen.Initialize(2002, false);
            placed.Add(rightScreen); placed.Add(leftScreen);
            rightScreen.transform.SetPositionAndRotation(app.ARCamera.ViewportToWorldPoint(new Vector3(0.7f, 0.5f, 0.6f)), app.ARCamera.transform.rotation);
            leftScreen.transform.SetPositionAndRotation(app.ARCamera.ViewportToWorldPoint(new Vector3(0.3f, 0.5f, 0.6f)), app.ARCamera.transform.rotation);
            yield return Frames();

            var rightUV = new Vector2(0.7f, 0.9f);
            var leftUV = new Vector2(0.3f, 0.9f);
            Aim(app, TrackedHand.Right, rightScreen, rightUV);
            Aim(app, TrackedHand.Left, leftScreen, leftUV);
            Send(app, "right.status");
            var status = Send(app, "left.status");
            Assert.AreEqual(2001, status.rightHand.pointedScreenId);
            Assert.AreEqual(2002, status.leftHand.pointedScreenId);
            Assert.That(Vector2.Distance(rightUV, rightScreen.RightInput.UV), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(leftUV, leftScreen.LeftInput.UV), Is.LessThan(0.001f));
            Assert.AreNotSame(app.MenuFor(TrackedHand.Left), app.MenuFor(TrackedHand.Right));
            yield return Frames();
            Assert.IsTrue(app.transform.Find("Hand ray").GetComponent<LineRenderer>().enabled);
            Assert.IsTrue(app.transform.Find("Left hand ray").GetComponent<LineRenderer>().enabled);

            TrackedHand clickedHand = TrackedHand.Unknown;
            PointerEventData.InputButton clickedButton = PointerEventData.InputButton.Middle;
            leftScreen.HandPointerClicked += (_, hand, button, uv) => { clickedHand = hand; clickedButton = button; };
            Assert.IsTrue(Send(app, "left.ui.rightclick").ok);
            Assert.AreEqual(TrackedHand.Left, clickedHand);
            Assert.AreEqual(PointerEventData.InputButton.Right, clickedButton);

            var leftMenu = app.MenuFor(TrackedHand.Left);
            var rightMenu = app.MenuFor(TrackedHand.Right);
            UnityEngine.UI.Button Option(ScreenAppMenu menu)
            {
                foreach (var button in menu.ActiveCanvas.GetComponentsInChildren<UnityEngine.UI.Button>())
                    if (button.name == "Photos & videos") return button;
                Assert.Fail("Missing app option"); return null;
            }
            void AimOption(TrackedHand side, ScreenAppMenu menu)
            {
                var rect = (RectTransform)Option(menu).transform;
                var viewport = app.ARCamera.WorldToViewportPoint(rect.TransformPoint(rect.rect.center));
                // Unknown chirality skips offset for this synthetic UI sample.
                app.Hands.GetHand(side).SetEditorSample(viewport, null);
            }
            Canvas.ForceUpdateCanvases();
            var previousTexture = app.ARCamera.targetTexture;
            var render = RenderTexture.GetTemporary(1280, 720, 24);
            app.ARCamera.targetTexture = render;
            app.ARCamera.Render();
            app.ARCamera.targetTexture = previousTexture;
            RenderTexture.ReleaseTemporary(render);
            AimOption(TrackedHand.Left, leftMenu);
            AimOption(TrackedHand.Right, rightMenu);
            Assert.IsTrue(Send(app, "left.ui.press").ok);
            Assert.IsTrue(leftMenu.MenuInput.IsRayPressed);
            Assert.IsTrue(Send(app, "right.ui.release").ok);
            Assert.IsTrue(leftMenu.MenuInput.IsRayPressed, "Other hand release cannot end this press");
            Assert.IsTrue(Send(app, "right.ui.press").ok);
            Assert.IsTrue(Send(app, "left.ui.release").ok);
            Assert.IsTrue(leftMenu.IsModal);
            Assert.IsTrue(rightMenu.MenuInput.IsRayPressed);
            Assert.IsTrue(Send(app, "right.ui.release").ok);
            Assert.IsTrue(rightMenu.IsModal);
            leftMenu.Close();
            Assert.IsTrue(rightMenu.IsModal, "Closing one picker cannot close the other");
            rightMenu.Close();
            Aim(app, TrackedHand.Right, rightScreen, rightUV);
            Aim(app, TrackedHand.Left, leftScreen, leftUV);

            // Turning off/loss of one hand must not alter the other hand's ray or contact.
            Send(app, "left.pointer.off");
            Assert.IsTrue(app.RightHand.IsTracked && app.RightHand.PointerEnabled);
            status = Send(app, "left.ui.click");
            Assert.IsFalse(status.ok);
            Send(app, "left.pointer.on");
            app.LeftHand.SetEditorSample(new Vector2(-1, -1), null);
            Assert.IsFalse(Send(app, "left.ui.click").ok, "A missing hand cannot borrow the tracked right ray");
            Assert.IsTrue(Send(app, "right.ui.click").ok);

            Aim(app, TrackedHand.Left, leftScreen, leftUV);
            Assert.IsTrue(Send(app, "left.adjust.enter").ok);
            Assert.IsTrue(Send(app, "right.adjust.enter").ok);
            int rightSteps = rightScreen.SizeSteps, leftSteps = leftScreen.SizeSteps;
            Assert.IsTrue(Send(app, "left.adjust.grow").ok);
            Assert.AreEqual(leftSteps + 1, leftScreen.SizeSteps);
            Assert.AreEqual(rightSteps, rightScreen.SizeSteps);
            Assert.IsTrue(Send(app, "right.adjust.shrink").ok);
            Assert.AreEqual(rightSteps - 1, rightScreen.SizeSteps);

            // Touch coordinates are separate even while both hands address the same screen.
            var touchUV = new Vector2(0.2f, 0.65f);
            Aim(app, TrackedHand.Left, rightScreen, leftUV, rightScreen.WorldPoint(touchUV));
            status = Send(app, "left.status");
            Assert.AreEqual("touch", status.interaction.kind);
            Assert.AreEqual(2001, status.interaction.screenId);
            Assert.AreEqual(ScreenInputKind.Ray, rightScreen.RightInput.Kind);
            Assert.That(Vector2.Distance(touchUV, rightScreen.LeftInput.UV), Is.LessThan(0.001f));

            Assert.IsTrue(Send(app, "left.move.enter").ok, "Move uses the left locked selection");
            Assert.IsTrue(Send(app, "right.move.enter").ok);
            status = Send(app, "left.status");
            Assert.AreEqual(2002, status.leftHand.movingScreenId);
            Assert.AreEqual(2001, status.rightHand.movingScreenId);
            Send(app, "right.move.cancel");
            Assert.IsTrue(Send(app, "left.status").moveModeEnabled);
            Send(app, "left.move.cancel");
            Aim(app, TrackedHand.Right, rightScreen, rightUV);
            Assert.IsTrue(Send(app, "right.adjust.enter").ok);
            Aim(app, TrackedHand.Left, rightScreen, leftUV);
            Assert.IsFalse(Send(app, "left.move.enter").ok, "The other hand owns this adjustment");

            Send(app, "left.screens.clear");
            Assert.AreEqual(0, app.ScreenCount);
            Send(app, "left.place.enter");
            Send(app, "right.place.enter");
            AimSurface(app, TrackedHand.Left);
            AimSurface(app, TrackedHand.Right);
            CommandReply leftReply = null, rightReply = null;
            app.Execute(new VisualizerCommand { id = "left-placement", command = "left.screen.place" }, r => leftReply = r);
            app.Execute(new VisualizerCommand { id = "right-placement", command = "right.screen.place" }, r => rightReply = r);
            float until = Time.realtimeSinceStartup + 8;
            while ((leftReply == null || rightReply == null) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(leftReply); Assert.IsNotNull(rightReply);
            Assert.IsTrue(leftReply.ok, "Left: " + leftReply.message); Assert.IsTrue(rightReply.ok, "Right: " + rightReply.message);
            Assert.AreEqual("left", leftReply.hand); Assert.AreEqual("right", rightReply.hand);
            Assert.AreEqual("left-placement", leftReply.id); Assert.AreEqual("right-placement", rightReply.id);
            Assert.AreEqual(2, app.ScreenCount, "Both asynchronous requests retain their hand state");
            Assert.AreNotSame(app.MenuFor(TrackedHand.Left).Target, app.MenuFor(TrackedHand.Right).Target);

            Send(app, "left.pointer.off");
            Send(app, "right.goggle.enter");
            Assert.IsTrue(app.LeftHand.PointerLockedOn && app.RightHand.PointerLockedOn);
            Assert.IsFalse(Send(app, "left.pointer.off").ok);
            Assert.IsFalse(Send(app, "right.pointer.off").ok);
            app.MenuFor(TrackedHand.Left).Close();
            app.MenuFor(TrackedHand.Right).Close();
            var hud = app.GetComponentInChildren<VisualizerHUD>();
            hud.SetHUDVisible(true);
            yield return Frames();
            Canvas.ForceUpdateCanvases();
            app.ARCamera.Render();
            app.GoggleHUD.Stereo.LeftEye.Render();
            app.GoggleHUD.Stereo.RightEye.Render();
            var toggle = (RectTransform)hud.GetComponentInChildren<HUDVisibilityToggle>(true).transform;
            var mode = (RectTransform)hud.transform.Find("Safe Area/Controls/Mode Button");
            app.LeftHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(toggle.TransformPoint(toggle.rect.center)), null);
            app.RightHand.SetEditorSample(app.ARCamera.WorldToViewportPoint(mode.TransformPoint(mode.rect.center)), null);
            Assert.AreEqual(toggle.name, Send(app, "left.status").pointedHUDButton);
            Assert.AreEqual(mode.name, Send(app, "right.status").pointedHUDButton);
            Assert.IsTrue(Send(app, "left.ui.click").ok);
            Assert.IsFalse(hud.IsHUDVisible);
            Assert.IsFalse(Send(app, "right.ui.click").ok, "The other hand cannot click a now-hidden HUD control");
            Assert.IsTrue(Send(app, "left.ui.click").ok);
            Assert.IsTrue(hud.IsHUDVisible);
            Send(app, "right.goggle.exit");
            Assert.IsFalse(app.LeftHand.PointerEnabled);
            Assert.IsTrue(app.RightHand.PointerEnabled);
            Send(app, "right.screens.clear");
            app.LeftHand.ClearEditorSample();
            app.RightHand.ClearEditorSample();
        }
    }
}
