# iPhone AR screen visualiser

Open **Assets/ARVisualizer/Scenes/ARVisualizer.unity** in Unity **6000.6.0f1**. The scene uses a saved HUD and screen prefab, with a runtime AR rig. The iPhone 14 Pro Max supports the LiDAR depth used for fingertip contact. Both landscape orientations are supported.

## Use

1. Enter place mode with **ENTER PLACE** or `place.enter`, then move the phone slowly to find flat surfaces. The highlights use the original sample scene's feathered plane prefab.
2. Show the **base knuckle of your index finger (MCP)** to the rear camera. The ray follows a point **3 cm to its left for a right hand**, or **3 cm to its right for a left hand**, as seen in the phone's view. With the pointer off, placement uses the centre crosshair.
3. Send `screen.place` or tap **PLACE SCREEN**. A black **16 × 9 × 0.5 cm** screen is anchored to the surface.
4. Screens lie flush against their supporting surface. On walls and slopes their width stays level with the horizon. On horizontal surfaces they lie flat and can rotate within the surface plane. Surfaces within 15 degrees of horizontal count as horizontal.
5. Exit place mode with `place.exit`. Point the knuckle ray at a screen to report face coordinates. Bring the fingertip to its face to report touch coordinates instead.
6. Point the ray at a screen and send `adjust.enter`. This locks that screen for adjustment and exits place mode. Grow, shrink or rotate it with the commands below, then send `adjust.exit`.

Each grow step adds **1.6 cm width and 0.9 cm height**; each shrink step removes the same amounts. The ratio stays **16:9**, and thickness stays **0.5 cm**. Default size is 10 steps; the range is 1–100 steps (1.6 × 0.9 cm through 160 × 90 cm). Rotation defaults to **15 degrees per command**, adjustable on **AR Visualizer App → Screen interaction**. Rotation is rejected for wall/sloped screens so they stay level.

Adjust mode keeps the same screen selected when the ray moves elsewhere or tracking is lost. Repeating `adjust.enter` keeps the selection; exit and enter again to select another screen. Entering place mode exits adjust mode. Undoing/clearing the selected screen also exits adjust mode. Placement requires place mode and a tracked surface; it is rejected while the ray points at an existing screen. Screens follow their AR anchors but are not saved across app sessions. The default limit is 100 screens.

The square **HUD − / HUD +** button in the top-left safe area hides or shows the status panels, controls and aim marker. The button stays visible and clickable. Hand tracking, the world-space ray, surface highlights, screens and Wi-Fi commands continue working while the HUD is hidden. Showing the HUD restores each panel's previous visibility.

## Goggle mode

Send `goggle.enter` for side-by-side **left and right eye views** in a phone VR viewer. The screens, hand ray and floating HUD are rendered independently from two eye positions, so nearby objects have greater binocular disparity. The HUD follows the user's view, and the hand ray can highlight its enabled buttons. Send `ui.click` to activate the currently pointed button. Send `goggle.exit` to return to the ordinary phone HUD.

The default panel is **0.8 m** in front of the eye midpoint and up to **0.9 m** wide. Edit **Landscape HUD → Goggle HUD → Floating panel** to change its distance, width or vertical position. **Fit Within Eye Views** reduces its width if needed to keep all controls visible to both eyes. The existing child Rect Transforms, text, button actions and HUD toggle remain editable in the LandscapeHUD prefab. The default UI material keeps the floating controls readable in front of the scene.

**Headset calibration:** edit **Landscape HUD → Stereo Goggles** on the same prefab. The default eye separation is **0.064 m (64 mm)**, with a **70° vertical field of view**. Match eye separation to the user and the viewer's adjustable lenses. Adjust **Left Lens Centre** to align the image with the left lens; the right centre is mirrored. **Distortion** controls radial lens correction; set both coefficients to zero for an undistorted comparison. **Eye Centre Offset** accounts for the position of the eye midpoint relative to the rear phone camera. These are manually adjustable defaults for an unbranded viewer, not a calibrated or QR-imported Cardboard profile. Tune them on the actual headset until a distant object fuses comfortably into one image; then check a nearby screen. The software cannot measure the viewer's physical lens adjustments.

Keep the phone horizontal and leave its **rear camera and LiDAR unobstructed** by the viewer. ARKit continues to supply tracking and camera images. The real-world feed comes from **one camera**; it is reprojected into the eye views using available human/environment depth, including depth occlusion of virtual screens. Missing depth uses a configurable **4 m video distance** without occlusion. Depth edges, newly revealed areas and areas outside the camera's field of view remain approximate; this is not a two-camera stereo capture of the real world. The virtual objects and HUD have actual geometric stereo views. **Render Scale** defaults to 0.75 to limit the cost of rendering both eyes.

Goggle mode prefers environment depth for the whole scene and falls back to the available depth source. Exiting restores the application's normal occlusion preference. An explicit **No Occlusion** setting is still respected in either mode.

The ray is **locked on** in goggle mode; `pointer.off` is rejected and the pointer button is disabled. Both eye views share one hand-ray target and one button activation. Exiting restores the pointer's previous setting, the original camera output, canvas layout/materials, safe-area fitting and normal touch input. Tracking loss clears the hovered button and prevents a stale click. Normal mode rejects `ui.click`.

The top-left HUD toggle can also be targeted and clicked by command. It stays available when the other panels are hidden. Screen tracking, placement and Wi-Fi continue while the HUD is hidden.

To use the floating **Place Screen** button, enter place mode, aim at a flat surface, then move the ray onto the button and send `ui.click`. The last valid surface point is retained while targeting HUD controls, so clicking the button places there. Looking away from the HUD resumes normal world targeting. Tracking loss, leaving place mode or switching display modes clears the retained point. UI controls consume the ray instead of pointing at a screen behind them; direct fingertip touches still use their separate contact measurement.

The click reply confirms delivery to the button. Actions such as adding an AR anchor can finish asynchronously; use subsequent `status` replies or the HUD to check the result. Give each intentional click a new request ID. Retransmit the same ID only to retry that click; the normal UDP deduplication prevents two activations.

```json
{"id":"goggle-mode-1","command":"goggle.enter"}
{"id":"hud-click-1","command":"ui.click"}
{"id":"normal-mode-1","command":"goggle.exit"}
```

These are three separate datagrams. `status` includes `goggleModeEnabled`, `hudVisible`, and `pointedHUDButton` (empty when no enabled control is targeted). Custom local controls can call `EnterGoggleMode` or `ExitGoggleMode` on VisualizerHUD.

## Screen prefab and interaction API

Use **Tools → AR Visualizer → Open screen prefab**, or open **Assets/ARVisualizer/Prefabs/Screen.prefab**.

- **Screen** has the `ScreenSurface` component and a Box Collider. **Size Steps** controls the initial dimensions. Keep the root scale at one.
- **Body** is a unit mesh fitted to the screen's dimensions. Its material is **Assets/ARVisualizer/Materials/ScreenBlack.mat**, using the unlit **ARVisualizer/Screen** shader. It remains black during pointing, touching and adjustment. You can change the mesh/material or add decorative children.
- The root sits at the mounting surface. Local **X** is face width, **Y** is height, and **−Z** points outward toward the viewer. Body and collider dimensions are maintained by the component; extra children are yours to style.

Coordinates use **(u,v) = (0,0) at bottom-left and (1,1) at top-right**, viewed from the front. `InputMetres` measures from that same corner. Only the front face accepts ray hits. The nearest intersected screen receives ray input. A valid fingertip contact takes priority over ray input, including when the ray is pointing at a different screen.

`ScreenSurface` exposes **Id, InputKind, InputUV, InputMetres, CanRotate** and **IsAdjusting**. The Inspector shows live input coordinates during Play. Its saved Unity events are **Ray Pointed, Ray Exited, Touch Started, Touch Moved, Touch Ended**. Connect these to your own screen logic, or subscribe in code:

```csharp
screen.InputChanged += (surface, kind, uv) =>
{
    // kind is None, Ray or Touch; uv is normalized in the current screen size.
};
```

Contacts end when depth/tracking is lost, the finger leaves the face, the app pauses, or the screen is disabled or removed. Inspector events can disable the screen without starting a pending ray/touch interaction or reporting stale coordinates. Resizing updates its face coordinates and collider together. The Wi-Fi `status` reply provides the current interaction too; no unsolicited network event stream is started.

### How fingertip contact is measured

The ray aims from the camera through the **point beside the index knuckle**; it does not estimate the finger's pointing direction. Vision identifies left versus right hands. LiDAR reconstructs the knuckle's position from the same camera frame, and the lateral offset is applied along the phone camera's horizontal axis. The ray origin, placement target, screen hit coordinates and HUD marker all use that offset point. An unknown hand side uses no lateral offset until identified.

Edit **Hand Pointer Source → Ray offset → Lateral Offset Metres**, initially **0.03**. When reliable knuckle depth is unavailable, the ray uses **Fallback Root Depth Metres**, initially **0.5 m**; the physical offset is then approximate. This estimate is used only for the ray and never creates fingertip contacts.

Fingertip contact uses a separate index-tip observation and **raw LiDAR depth from the same ARFrame**. Camera intrinsics and that frame's camera pose reconstruct a position in AR session space, which is transformed into Unity world space. The lateral ray offset never changes fingertip-touch coordinates.

The native worker checks depth confidence, samples a locally consistent patch, and rejects implausible tip-to-knuckle distances to reduce background-depth mistakes. Samples older than **0.2 seconds** are rejected. Contact starts within **1.2 cm** of the face, with a **1.8 cm** release distance to reduce chatter; the start tolerance is editable in the application Inspector. This is an approximate depth-based contact estimate, not a hardware touch sensor. Thin fingers, occluded tips, motion and LiDAR edges need testing on the phone.

If reliable fingertip depth is unavailable, there is **no touch event**; ray input can continue. A 2D image overlap is never used as a substitute for contact. `pointer.off` turns off ray input/visualization while hand tracking continues for direct touch. **No Occlusion** disables the AR depth manager and therefore also prevents device depth-based touches; use Human or Environment occlusion for touch.

In the Editor, the mouse simulates the knuckle. Choose **Hand Pointer Source → Editor Hand** to preview either hand (default Right). Hold the left mouse button to simulate a tip at **Editor Touch Depth Metres**, initially 0.5 m from the camera. Set this to the screen's camera depth to exercise contact. A physical iPhone is needed to validate Vision, handedness and LiDAR measurements.

## Wi-Fi commands

Connect the phone and controller to the same LAN. Send one UTF-8 command per unicast UDP datagram to the address in the HUD, **port 7777** by default. Plain commands and JSON are accepted:

```json
{"id":"request-001","command":"adjust.grow","token":""}
```

| Command | Effect |
| --- | --- |
| `pointer.on` / `pointer.off` | Enable/disable the knuckle ray; touch tracking continues |
| `goggle.enter` / `goggle.exit` | Stereo eye views, floating HUD and locked-on ray / normal phone HUD |
| `ui.click` | Click the enabled HUD button under the ray in goggle mode |
| `place.enter` / `place.exit` | Enter/quit highlighted-surface placement |
| `screen.place` | Place the screen prefab at the valid surface target |
| `screen.undo` | Remove the newest screen, or cancel pending placement |
| `screens.clear` | Remove all screens and cancel pending placement |
| `adjust.enter` / `adjust.exit` | Lock the ray-pointed screen for adjustment / release it |
| `adjust.grow` / `adjust.shrink` | Increase/decrease the selected screen by one size step |
| `adjust.rotate.cw` / `adjust.rotate.ccw` | Rotate the selected screen; horizontal supports only |
| `status` | Return mode, counts, selected screen and current ray/touch coordinates |

The earlier `cube.place`, `cube.undo` and `cubes.clear` commands remain aliases for screen operations. The legacy `cubeCount` reply field mirrors `screenCount`.

A reply includes `ok`, `message`, pointer/place/adjust states, `screenCount`, `pointedScreenId`, `interaction` and `adjustedScreen`. Example interaction:

```json
{"screenId":1,"kind":"touch","u":0.25,"v":0.3,"xMetres":0.04,"yMetres":0.027}
```

`kind` is **none**, **ray**, or **touch**. Screen ID **0** means no target; other IDs are unique within a session. The selected screen snapshot contains its ID, width, height, thickness (metres) and whether it can rotate.

Use the dependency-free Python 3 sender from the project root:

```sh
python Tools/send_command.py 192.168.1.42 place.enter
python Tools/send_command.py 192.168.1.42 screen.place
python Tools/send_command.py 192.168.1.42 place.exit
# Aim the knuckle ray at the screen first:
python Tools/send_command.py 192.168.1.42 adjust.enter
python Tools/send_command.py 192.168.1.42 adjust.grow
python Tools/send_command.py 192.168.1.42 adjust.shrink
python Tools/send_command.py 192.168.1.42 adjust.rotate.cw
python Tools/send_command.py 192.168.1.42 status
python Tools/send_command.py 192.168.1.42 adjust.exit
```

Allow iOS **Local Network** access when prompted, then retry. Replies go to the sender's address/port after completion. The sender retries on the same socket with the same request ID; the receiver caches the last 128 completed requests and suppresses pending duplicates. Use an ID only for retries of the same operation, and a **new ID for every status poll**. Plain commands have no deduplication. Wait for each reply before the next command; UDP is not ordered or guaranteed.

`CommunicationManager` is the replaceable transport template. It dispatches commands on Unity's main thread, limits packets to 4 KB, queues up to 64 packets, and dispatches up to eight per frame. It closes on pause/disable and reopens on resume. Set **Shared Token** and use the sender's `--token` option if needed. This development transport is unencrypted; replace it for use beyond a trusted LAN.

## Edit the HUD in Unity

Stop Play, then expand **AR Visualizer → Landscape HUD**, or use **Tools → AR Visualizer → Open HUD prefab**. Select a landscape Game view such as 1280 × 720.

The separate square button prefab is **Assets/ARVisualizer/Prefabs/HUDToggleButton.prefab**, also available from **Tools → AR Visualizer → Open HUD toggle prefab**. Its instance is under **Safe Area → HUD Toggle Button**. Edit its Rect Transform for position/size, Image for its background, and Caption/State children for typography. The default square is 64 × 64 canvas units. The header leaves space beside it. The HUD's **Hideable Content** list controls which roots are hidden; keep the toggle outside those roots. Existing custom panels can be added to that list.

| What to edit | Object under Landscape HUD |
| --- | --- |
| Header | Safe Area → Top Bar |
| Placement information | Safe Area → Placement Panel |
| All buttons together | Safe Area → Controls |
| Individual button | Controls → Pointer / Mode / Place Screen / Undo Button |
| Button typography | Button → Caption |
| Crosshair | Aim Position → Aim Marker |

Use **Rect Transform / Rect tool (T)** for positions and sizes, **Image** for colors and sprites, and **Text** for typography. Source sprites with **Image Type → Sliced** support rounded, resizable panels. Select **Landscape HUD** for messages, state colors and **View / Scan / No hand / Ready** previews; Ctrl+Z undoes previews. Live labels update their content during Play while authored geometry and typography are preserved.

Buttons use saved **On Click ()** events. Additional buttons can call `EnterAdjustMode`, `ExitAdjustMode`, `GrowScreen`, `ShrinkScreen`, `RotateScreenClockwise`, `RotateScreenCounterclockwise`, or `ClearScreens` on the HUD.

**Safe Area** fits its own anchors to the iPhone's screen margins; edit the panels inside it. **Aim Position** follows the knuckle; style its Aim Marker children. The Canvas Scaler uses a 1280 × 720 reference. Edits before Play persist; edits during Play are temporary. Scene overrides and prefab edits work normally. Existing customized layouts are not rebuilt by setup tools.

## Depth occlusion

Occlusion is enabled in both view and place modes. A recognized nearer hand can hide the screen behind it. Select **AR Visualizer App → Real-world occlusion**:

- **Prefer Human Occlusion** (default): human mask and depth.
- **Prefer Environment Occlusion**: LiDAR depth for people and other objects.
- **No Occlusion**: comparison mode; also disables device depth-based touch.
- **Smooth Environment Depth**: reduces rendered depth flicker. Fingertip measurement always uses raw, frame-matched scene depth.

ARKit chooses one rendering source according to preference and availability; these are not merged. The camera's depth is written before opaque screens. HUD graphics remain overlays. [Unity's occlusion requirements](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.6/manual/arkit-occlusion.html), [Apple iPhone 14 Pro Max specifications](https://support.apple.com/en-us/111846).

## Build and verify

Install Unity's matching **iOS Build Support**. Use **Tools → AR Visualizer → Configure iPhone**, then select iOS in Build Profiles, confirm the ARVisualizer scene is first, and configure your bundle ID and signing team. ARKit and Initialize XR on Startup must be enabled. Export the Xcode project and build/sign it on a Mac. The build hook links Vision/ARKit and enables ARC for the native hand plugin. iOS 15 or newer, camera permission and local-network permission are required.

**Rebuild and reinstall the app after these changes**, including the updated native hand plugin. A previous Xcode export does not automatically include new source changes.

**Window → General → Test Runner → EditMode → ARVisualizer.Tests** covers command parsing, UDP dispatch/correlation/deduplication, prefab dimensions and events, front-face coordinates, wall orientation, size limits, native sample layout and the simulated AR flow. The integration test places an anchored screen, points at it, locks adjustment, grows/shrinks/rotates it, verifies fingertip priority and tracking loss, preserves authored HUD layout, and exercises undo. Goggle checks cover ray hover, actual UDP button clicks, duplicate click packets, the always-available HUD toggle, retained placement targets, pointer locking, mode restoration and shutdown. With graphics enabled, they compare rendered eye positions, near/far stereo disparity, depth occlusion against a depth-disabled control, and the lens compositor.

On the iPhone, check both landscape orientations, wall/table placement, knuckle targeting, fingertip contact/release near face edges, hand occlusion, depth loss, adjust commands, and background/resume. For the headset, calibrate lens centres/eye separation and distortion, check fusion at near and far distances, point at every HUD button, and exit/re-enter goggle mode. Windows Unity tests cannot validate the viewer's optics, compile Apple's native frameworks or establish real fingertip-contact accuracy.

Implementation references: [Apple index MCP joint](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/jointname/indexmcp), [hand chirality](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/chirality), [scene-depth reconstruction](https://developer.apple.com/documentation/arkit/displaying-a-point-cloud-using-scene-depth), and the installed ARKit package's **Includes~/UnityXRNativePtrs.h** for the native session bridge.
