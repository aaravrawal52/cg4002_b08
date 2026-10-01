# iPhone AR screen visualiser

Open **Assets/ARVisualizer/Scenes/ARVisualizer.unity** in Unity **6000.6.0f1**. The scene uses a saved HUD and screen prefab, with a runtime AR rig. The iPhone 14 Pro Max supports the LiDAR depth used for fingertip contact. Both landscape orientations are supported.

## Use

1. In normal mode, tap the right-hand **PLACE SCREEN** button (or send `place.enter`), then move the phone slowly to find flat surfaces. The same button becomes green **PLACE HERE**. The highlights use the original sample scene's feathered plane prefab.
2. Show the **base knuckle of your index finger (MCP)** to the rear camera. The ray follows a point **3 cm to its left for a right hand**, or **3 cm to its right for a left hand**, as seen in the phone's view. In normal mode, the ray automatically moves to the centre when no hand is detected. With the pointer off, placement still uses the centre crosshair.
3. Tap green **PLACE HERE** or send `screen.place`. Confirmation is enabled only when a valid surface is targeted. A black **16 × 9 cm flat rectangle** is anchored to the surface. Placement then exits place mode, returns the button to **PLACE SCREEN**, and opens **Choose app** for the new screen. The pointer is enabled so you can select an app. Failed or cancelled placement leaves the current mode unchanged. Goggles retain their separate **Enter Place / Exit Place** and **Place Screen** controls.
4. Screens lie flush against their supporting surface. On walls and slopes their width stays level with the horizon. On horizontal surfaces they lie flat and can rotate within the surface plane. Surfaces within 15 degrees of horizontal count as horizontal.
5. Use `place.exit` to cancel placement without adding a screen. Point the ray at a screen to report face coordinates. Bring the fingertip to its face to report touch coordinates instead.
6. Point the ray at a screen and send `adjust.enter`. This locks that screen for adjustment and exits place mode. Grow, shrink or rotate it with the commands below, then send `adjust.exit`.

**Normal-mode controls:** **Pointer On / Off** and the single **Place Screen / Place Here** button sit on the right. There is no Undo button in normal mode. **Left Click** and **Right Click** send `ui.click` and `ui.rightclick` to the current ray target. **Screen Settings** locks the pointed screen and opens Choose app, Smaller, Larger, Rotate Left, Rotate Right, **Delete Screen** and Done controls. Delete removes that selected screen, its anchor and its media, even if the ray has moved to another screen. Rotation remains available for horizontal supports only. These buttons and the settings panel are hidden in goggle mode and when the HUD is hidden. Edit their saved shapes and positions under **Landscape HUD / Safe Area / Normal Actions**; the panel is a child of that row. The placement button is **Safe Area / Controls / Mode Button**; captions and confirmation colors are on **Visualizer HUD**.

Each grow step adds **1.6 cm width**; each shrink step removes the same amount. Height follows the current media's aspect ratio, or **16:9** for a black screen (0.9 cm height per step). Loading media preserves the chosen width and the screen's anchored centre/orientation; a 3:4 portrait photo on a 16 cm wide screen makes it about 21.3 cm tall. The screen remains flat. Default size is 10 steps; the range is 1–100 steps (1.6–160 cm wide). Ray/fingertip bounds and reported coordinates use the current dimensions. Rotation defaults to **5 degrees per command**, adjustable on **AR Visualizer App → Screen interaction**. Rotation is rejected for wall/sloped screens so they stay level.

Adjust mode keeps the same screen selected when the ray moves elsewhere or tracking is lost. Repeating `adjust.enter` keeps the selection; exit and enter again to select another screen. Entering place mode exits adjust mode. Undoing/clearing the selected screen also exits adjust mode. Placement requires place mode and a tracked surface; it is rejected while the ray points at an existing screen. Screens follow their AR anchors but are not saved across app sessions. The default limit is 100 screens.

The square **HUD − / HUD +** button in the top-left safe area hides or shows the status panels, controls and aim marker. The button stays visible and clickable. Hand tracking, the world-space ray, surface highlights, screens and Wi-Fi commands continue working while the HUD is hidden. Showing the HUD restores each panel's previous visibility.

## Goggle mode

The square button at the top centre shows a **goggles icon** in normal mode and a **phone icon** in goggle mode. Tap it to switch modes. It stays in the same position on the physical phone display, above the stereo compositor, outside the two eye views. It remains touchable with the HUD hidden or hand tracking unavailable. It has its own safe-area canvas; the default status bar sits below it.

Send `goggle.enter` for side-by-side **left and right eye views** in a phone VR viewer. The screens, hand ray and floating HUD are rendered independently from two eye positions, so nearby objects have greater binocular disparity. The HUD follows the user's view, and the hand ray can highlight its enabled buttons. Send `ui.click` to activate the currently pointed button. Send `goggle.exit` to return to the ordinary phone HUD.

Goggles use a **4:3 camera viewport and 4:3 eye images**, fitted into the two display halves without stretching or cropping to the phone's wide aspect. Black margins may remain around the fitted views. The camera projection and video/depth sampling transform are requested for that 4:3 viewport; changing only a render texture's dimensions would leave AR Foundation's phone-sized crop in place. A supported 4:3 camera format is preferred, and any format change is restored on exit. Hand coordinates and surface raycasts use the same camera geometry.

The default panel is **0.8 m** in front of the eye midpoint and up to **0.9 m** wide, with a **1280 × 960 (4:3)** floating layout. Edit **Landscape HUD → Goggle HUD → Floating panel** to change its distance, width or vertical position. **Fit Within Eye Views** reduces its width using the actual camera projection to keep all controls visible to both eyes. Top and bottom controls anchor to the taller layout; normal-mode RectTransforms are restored on exit. The existing child Rect Transforms, text, button actions and HUD toggle remain editable in the LandscapeHUD prefab. The default UI material keeps the floating controls readable in front of the scene.

**Headset calibration:** edit **Landscape HUD → Stereo Goggles** on the same prefab. The default eye separation is **0.064 m (64 mm)**. **Match Camera Field Of View** is enabled to preserve the camera's complete vertical view. Disable it only for a manually calibrated headset projection; **Vertical Field Of View** then supplies the angle (70° by default). Match eye separation to the user and the viewer's adjustable lenses. Adjust **Left Lens Centre** within the physical display half to align the image with the left lens; the right centre is mirrored. Its position is accounted for inside the fitted 4:3 view. **Distortion** controls radial lens correction; set both coefficients to zero for an undistorted comparison. **Eye Centre Offset** accounts for the position of the eye midpoint relative to the rear phone camera. These are manually adjustable defaults for an unbranded viewer, not a calibrated or QR-imported Cardboard profile. Tune them on the actual headset until a distant object fuses comfortably into one image; then check a nearby screen. The software cannot measure the viewer's physical lens adjustments.

Keep the phone horizontal and leave its **rear camera and LiDAR unobstructed** by the viewer. ARKit continues to supply tracking and camera images. The real-world feed comes from **one camera**; it is reprojected into the eye views using available human/environment depth, including depth occlusion of virtual screens. Missing depth uses a configurable **4 m video distance** without occlusion. Depth edges, newly revealed areas and areas outside the camera's field of view remain approximate; this is not a two-camera stereo capture of the real world. The virtual objects and HUD have actual geometric stereo views. **Render Scale** defaults to 0.75 to limit the cost of rendering both eyes.

Goggle mode prefers environment depth for the whole scene and falls back to the available depth source. Exiting restores the application's normal occlusion preference. An explicit **No Occlusion** setting is still respected in either mode.

The ray is **locked on** in goggle mode; `pointer.off` is rejected and the pointer button is hidden. The bottom row normally contains **Enter Place** and **Undo**. In place mode it contains **Exit Place**, **Place Screen** and **Undo**. These controls fill the row with equal spacing; adjust **HUD → Controls → Goggle Button Spacing** in the Inspector. Both eye views share one hand-ray target and one button activation. Exiting restores the pointer's previous setting, the normal-mode controls and their authored positions (the separate Place Screen and Undo controls are hidden), camera output, canvas materials, safe-area fitting and normal touch input. Tracking loss clears the hovered button and prevents a stale click. In normal mode, `ui.click` operates the screen app menus; the phone HUD uses touch.

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

### Phone touches as ray clicks

In **normal mode**, enable the hand pointer and touch an empty part of the phone display: the **left half** sends a left click and the **right half** sends a right click, at the **ray's current target** (centre of the display when no hand is detected). Exactly the centre belongs to the right half. A left press over an app menu is held until release: tap to select, or swipe up/down on the phone to scroll the ray-targeted media list. Screen-face clicks and right clicks fire once per new contact. Crossing the centre does not create another click. Each half is measured against the whole landscape display in either orientation. The touch does not reposition the ray.

Visible HUD panels, buttons and AR menus are excluded from the split zones. They retain ordinary touch behavior. The aim marker does not block a click zone. Split clicks are disabled in goggle mode, with the pointer off, or when AR tracking is unavailable. Losing hand tracking in normal mode switches to the centre target.

Left clicks activate pointed app-menu controls. Right clicks close the photo library or collapse an open app dropdown; they never activate a left-click button. **Screen App Menu → Right Click Goes Back** can disable this default. When targeting a screen face, `ScreenSurface` dispatches **Left Clicked** / **Right Clicked** Inspector events with normalized face coordinates, plus the C# `PointerClicked(screen, button, uv)` event. Future apps can attach their own behavior. This is distinct from direct fingertip contact and does not synthesize operating-system mouse input. `ui.click` and `ui.rightclick` use the same routing over Wi-Fi.

### Screen apps and Photos

Point the hand ray at a blank screen to open the **Choose app** drop-down automatically. Point at **Photos & videos** and send **`ui.click`**. For a screen already showing media, point at its face or playback controls and send **`app.choose`** (press **F** in the keyboard controller). Opening the chooser preserves the current media; selecting another item replaces it. This works in normal and goggle modes.

The compact app and playback buttons sit along the **bottom edge of the selected screen**. Their row scales with the screen width, using 90% by default, with a 4–24 cm physical width limit. Edit **Launcher Width Fraction / Minimum Width / Maximum Width** on **Screen App Menu**, and the button RectTransforms under **Screen launcher** in its prefab. The app options expand upward from the bottom row.

After placement, the chooser stays available until dismissed, an app is selected, or another screen is targeted. Otherwise, the launcher stays available briefly while moving the ray from the screen to the menu. It is hidden while the screen is being adjusted. Collapsing the drop-down keeps it closed during the current visit; point away until the launcher hides and return, click **Choose app**, or send `app.choose` to reopen it. The command requires a current ray target, so looking away cannot open the menu for a previously pointed screen; in normal mode, hand loss uses the current centre target. Close an open photo-library popup before choosing another app.

The **AR photo library** is a continuous vertical list, newest first, with photo/video labels. Aim at a thumbnail and send `ui.click` to display it on that screen. To scroll, **hold left click and move the ray up/down**, then release. Use the normal HUD's **Left Click** button, hold **U** in the keyboard controller, or send `ui.press` followed by `ui.release` over Wi-Fi. A short press selects the item; a swipe never selects it. On the phone, you can also drag the gallery directly or swipe in empty left-half space while the ray points at the list. **Refresh / retry** and **Close** remain available; there are no page buttons. Thumbnails load as needed, with twelve recycled UI tiles and at most 48 decoded thumbnails retained after a load.

Scrolling works in normal and stereo goggle modes; while the popup is open, clicks and the ray are reserved for it. The top-left HUD toggle does not hide app menus. AR tracking loss cancels a held press. Losing a tracked hand cancels its drag; normal mode then uses the centre target for the next interaction. Changing modes or closing the popup also cancels a press. A 15-second timeout releases abandoned remote presses without clicking. Closing the picker or removing its screen cancels pending library work.

**Index-fingertip contact activates app-menu buttons**, including Choose app, app options, playback controls, Refresh and Close. This uses measured fingertip depth and also works with the ray off. Gallery thumbnails select when you lift your fingertip; sliding it up/down scrolls instead. Other buttons activate once on contact and require withdrawal before another activation. The phone and floating **HUD are excluded** from fingertip activation. Edit contact tolerance and swipe sensitivity on **Screen Menu Input** in the screen-app prefab; verify the feel on the iPhone.

The library floats just above the selected screen's top edge, aligned with its surface, with a 1.5 cm gap and 3 cm lift towards the viewer. Its bottom edge follows the screen's anchor and current dimensions. **Auto Size Popup** keeps its horizontal visual angle near **38 degrees** as you move closer or farther, accounting for its raised centre and viewing direction. Normal mode uses the camera position; goggles use the eye midpoint, with the same physical panel rendered for both eyes. The default physical width limits are **8 cm–4 m**; these limits take precedence at extreme distances. Edit **Popup Visual Angle**, **Popup Minimum/Maximum Width**, gap and surface offset under **Screen App Menu → Placement** in the prefab. Disable automatic sizing to use the fixed **Popup Width** instead. Both the launcher and library use **Occluded Screen UI**, so nearer real objects, including hands and arms, cover them through the existing AR occlusion system. Human-depth edge quality still needs checking on the iPhone; the Editor checks simulated depth in normal mode and both stereo eyes.

The first launch asks for iOS **Photos** access. Apple's permission prompt is system UI and must be answered using the phone's touch screen; it cannot be clicked by the AR ray. You can grant full or selected-photos access. With selected access, only those items appear; change that selection in iPhone Settings, then refresh. Denied/restricted access and empty libraries show a message in the AR popup. No Photos permission is requested until the Photos app is opened. [Apple Photos authorization](https://developer.apple.com/documentation/photos/phphotolibrary/requestauthorization(for:handler:)).

Photos are loaded upright at up to 2048 pixels on the longest side. Videos are exported to a local MP4 using PhotoKit's 720p preset, then played with Unity VideoPlayer. iCloud items may take time to download. The original library is never edited. Each screen owns its playback and temporary media; **Close app** releases it and restores a black 16:9 rectangle at the chosen width. **Choose media** opens the picker again, and **Play / Pause** controls video. Videos loop by default and pause when the app goes into the background. The screen automatically takes the photo or prepared video's display aspect ratio, including portrait, square and wide formats, so the full image fills the rectangle without added borders or cropping. Video sizing also accounts for [non-square pixels reported by Unity](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/video/videoplayer). Grow/shrink preserves the current media ratio. Rotation and real-world occlusion continue to work.

Edit **Tools → AR Visualizer → Open screen apps prefab**, or **Assets/ARVisualizer/Resources/ScreenAppMenu.prefab**. The launcher, popup, buttons, thumbnail areas, labels and positions are saved Unity UI objects. Enable **Screen launcher** or **Photo library** in Prefab Mode to preview it; runtime controls their visibility. Edit each child's Rect Transform, Image and Text. **Photo library → Media viewport** has the vertical Scroll Rect and clipping mask; its Content contains the recycled thumbnail buttons. **Media Library Grid → Tile layout** controls their runtime size, spacing and columns. **App registry** lists the drop-down entries; `photos` is the built-in app ID, while additional entries can invoke their **Launch** Unity event with the selected ScreenSurface. The Screen prefab has a **Screen Media** component that uses the same **Body** quad for black, photos and videos. `ScreenMedia.ShowPhoto`, `ShowVideo`, `TogglePlayback` and `Clear` are available to other apps; ShowPhoto takes ownership of the supplied texture. These are local APIs, not remote file-loading commands.

In Editor Play mode, the library supplies eight clearly labelled generated photos to exercise the UI. Device photo-library access requires an iPhone. The test-only sample video is under Tests/Editor and is excluded from device builds. `status` adds `pointedAppButton`, `appMenuScreenId`, `appChooserOpen`, and `photoLibraryOpen`; it does not expose asset identifiers, filenames or media bytes over Wi-Fi. Use a fresh command ID for each intended click and reuse the same socket/ID only for a retry.

Use **Tools → AR Visualizer → Open screen prefab**, or open **Assets/ARVisualizer/Prefabs/Screen.prefab**.

- **Screen** has the `ScreenSurface` component and uses analytical plane intersections for ray/touch input, without physics colliders. **Size Steps** controls the initial dimensions. Keep the root scale at one.
- **Body** is one quad (four vertices, two triangles) fitted to the screen's dimensions, using **Assets/ARVisualizer/Materials/ScreenMedia.mat**. The same renderer shows black or media, with no separate backing mesh, shadows or light probes. You can change the mesh/material or add decorative children.
- The root sits at the mounting surface. Local **X** is face width, **Y** is height, and **−Z** points outward toward the viewer. The quad sits 1 mm in front of the mounting plane to avoid flicker. Its dimensions are maintained by the component; extra children are yours to style.

Coordinates use **(u,v) = (0,0) at bottom-left and (1,1) at top-right**, viewed from the front. `InputMetres` measures from that same corner. Only the front face accepts ray hits. The nearest intersected screen receives ray input. A valid fingertip contact takes priority over ray input, including when the ray is pointing at a different screen.

`ScreenSurface` exposes **Id, InputKind, InputUV, InputMetres, CanRotate** and **IsAdjusting**. The Inspector shows live input coordinates during Play. Its saved Unity events are **Ray Pointed, Ray Exited, Touch Started, Touch Moved, Touch Ended**. Connect these to your own screen logic, or subscribe in code:

```csharp
screen.InputChanged += (surface, kind, uv) =>
{
    // kind is None, Ray or Touch; uv is normalized in the current screen size.
};
```

Contacts end when depth/tracking is lost, the finger leaves the face, the app pauses, or the screen is disabled or removed. Inspector events can disable the screen without starting a pending ray/touch interaction or reporting stale coordinates. Resizing updates the quad and its face coordinates together. The Wi-Fi `status` reply provides the current interaction too; no unsolicited network event stream is started.

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
| `ui.click` | Left-click the ray's app-menu control, screen face, or a HUD button in goggle mode |
| `ui.press` / `ui.release` | Hold/release left click on an app button or media list; move the ray vertically while held to scroll |
| `ui.rightclick` | Right-click the ray's screen face, or go back from an open app menu |
| `app.choose` | Open the app chooser on the ray-pointed screen, including one already showing media |
| `place.enter` / `place.exit` | Enter/quit highlighted-surface placement |
| `screen.place` | Place a flat screen, exit place mode, and open its app chooser |
| `screen.undo` | Remove the newest screen, or cancel pending placement |
| `screens.clear` | Remove all screens and cancel pending placement |
| `adjust.enter` / `adjust.exit` | Lock the ray-pointed screen for adjustment / release it |
| `adjust.grow` / `adjust.shrink` | Increase/decrease the selected screen by one size step |
| `adjust.rotate.cw` / `adjust.rotate.ccw` | Rotate the selected screen; horizontal supports only |
| `status` | Return mode, counts, selected screen and current ray/touch coordinates |

The earlier `cube.place`, `cube.undo` and `cubes.clear` commands remain aliases for screen operations. The legacy `cubeCount` reply field mirrors `screenCount`.

A reply includes `ok`, `message`, pointer/place/adjust states, `screenCount`, `pointedScreenId`, `interaction` and `adjustedScreen`. `appChooserOpen` reports whether the app drop-down is expanded; `appMenuScreenId` identifies its target. Example interaction:

```json
{"screenId":1,"kind":"touch","u":0.25,"v":0.3,"xMetres":0.04,"yMetres":0.027}
```

`kind` is **none**, **ray**, or **touch**. Screen ID **0** means no target; other IDs are unique within a session. The selected screen snapshot contains its ID, width, height, thickness (now zero, in metres) and whether it can rotate.

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

For keyboard control on Windows, run `python Tools/keyboard_command.py 192.168.1.42`. Point at a screen and press **F** to send `app.choose`. Tap **U** to select an app-menu button, or hold **U** and move the ray up/down to scroll the media list. The controller sends `ui.press`/`ui.release` for app menus and falls back to `ui.click` for a screen face or goggle HUD button. Keep its console focused while using the keyboard. The one-command sender accepts `app.choose`, `ui.press` and `ui.release` too. These press/release commands apply to app menus only; continue using `ui.click` for instantaneous clicks elsewhere.

Allow iOS **Local Network** access when prompted, then retry. Replies go to the sender's address/port after completion. The sender retries on the same socket with the same request ID; the receiver caches the last 128 completed requests and suppresses pending duplicates. Use an ID only for retries of the same operation, and a **new ID for every status poll**. Plain commands have no deduplication. Wait for each reply before the next command; UDP is not ordered or guaranteed.

`CommunicationManager` is the replaceable transport template. It dispatches commands on Unity's main thread, limits packets to 4 KB, queues up to 64 packets, and dispatches up to eight per frame. It closes on pause/disable and reopens on resume. Set **Shared Token** and use the sender's `--token` option if needed. This development transport is unencrypted; replace it for use beyond a trusted LAN.

## Edit the HUD in Unity

Stop Play, then expand **AR Visualizer → Landscape HUD**, or use **Tools → AR Visualizer → Open HUD prefab**. Select a landscape Game view such as 1280 × 720.

The top-centre mode switch is **Assets/ARVisualizer/Prefabs/DisplayModeButton.prefab**, also available from **Tools → AR Visualizer → Open display mode button prefab**. Edit its Image and **Icon** child there; the icon has editable size, color and stroke width, and its phone/goggles shape follows the current mode. To position or resize the button, open **PhoneControls.prefab** and select **Safe Area → DisplayModeButton**, or edit that instance under **AR Visualizer → PhoneControls** in the scene (default size: 64 × 64 canvas units). PhoneControls contains the separate overlay canvas and safe-area fitter. Keep its sorting order above the stereo compositor (defaults: 30010 and 30000) and outside the floating LandscapeHUD.

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

## Script map for maintenance

The runtime scripts are under **Assets/ARVisualizer/Scripts**. Start with the script that owns the behavior you want to change:

| Behavior | Script |
| --- | --- |
| Application lifecycle, modes, ray/touch priority and placement targeting | `ARVisualizerApp.cs` |
| Wi-Fi/local command routing, placement requests and command replies | `ARVisualizerApp.Commands.cs` |
| Runtime camera, AR session, tracking origin and pose input setup | `ARSceneRig.cs` |
| App chooser, popup positioning and media-selection workflow | `ScreenAppMenu.cs` |
| Gallery layout, thumbnail loading and cache limits | `MediaLibraryGrid.cs` |
| Ray/fingertip press, drag, release and contact thresholds | `ScreenMenuInput.cs` |
| Phone split-zone input and the HUD's holdable left-click button | `PhonePointerClicks.cs`, `PointerHoldButton.cs` |
| Photo-library requests and cached-image decoding | `PhotoLibrary.cs` |
| Photo/video display, aspect ratio and owned-media cleanup | `ScreenMedia.cs` |
| HUD labels and buttons, floating layout and screen settings | `VisualizerHUD.cs`, `GoggleHUD.cs`, `NormalScreenControls.cs` |

`ARVisualizerApp.Commands.cs` is part of the same application component, using C# `partial`; it does not need another component in the scene. `ARSceneRig` is an internal setup helper. Inspector settings stay on the existing components, and their serialized field names and button callbacks are preserved. Existing prefabs need no migration for this cleanup.

`RefreshInteractionTargets` keeps the shared update order for frames and incoming commands: menus first, screen-face input next, then newly opened menus and surface placement. Preserve this order when adding input features so a UI click cannot reach a screen behind it. Placement and library requests keep generation counters to discard cancelled asynchronous results. Media loading has explicit ownership: the grid releases its thumbnail textures, and each `ScreenMedia` releases its displayed texture/video and temporary file. Run the existing EditMode suite after changing these flows.

## Depth occlusion

Occlusion is enabled in both view and place modes. A recognized nearer hand can hide the screen behind it. Select **AR Visualizer App → Real-world occlusion**:

- **Prefer Human Occlusion** (default): human mask and depth.
- **Prefer Environment Occlusion**: LiDAR depth for people and other objects.
- **No Occlusion**: comparison mode; also disables device depth-based touch.
- **Smooth Environment Depth**: reduces rendered depth flicker. Fingertip measurement always uses raw, frame-matched scene depth.

ARKit chooses one rendering source according to preference and availability; these are not merged. The camera's depth is written before opaque screens. HUD graphics remain overlays. [Unity's occlusion requirements](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.6/manual/arkit-occlusion.html), [Apple iPhone 14 Pro Max specifications](https://support.apple.com/en-us/111846).

## Build and verify

Install Unity's matching **iOS Build Support**. Use **Tools → AR Visualizer → Configure iPhone**, then select iOS in Build Profiles, confirm the ARVisualizer scene is first, and configure your bundle ID and signing team. ARKit and Initialize XR on Startup must be enabled. Export the Xcode project and build/sign it on a Mac. The build hook links Vision/ARKit/Photos/AVFoundation, enables ARC for the native hand and Photos plugins, and adds the Photos permission description. iOS 15 or newer, camera permission and local-network permission are required. Photos access is requested when opening the library.

**Rebuild and reinstall the app after these changes**, including the native hand and Photos plugins. A previous Xcode export does not automatically include new source changes.

The hand plugin uses Vision's return values and `NSError` handling, so it builds with Unity's default disabled Objective-C exceptions; no Xcode exception-setting change is needed. [Apple's Vision error handling](https://developer.apple.com/documentation/vision/vnimagerequesthandler/perform(_:)?language=objc).

Every iOS export adds **AR Visualizer Prepare Symbol Tools** as the first build phase of both **UnityFramework** and **Unity-iPhone**. On the Mac, it restores executable permission on the exported `process_symbols.sh`, `usymtool` and `usymtoolarm64` before Unity runs its symbol processing. This runs on each build, so transferring an export from Windows cannot leave those tools without executable permission. It handles paths containing spaces, skips tools absent from that Unity version, and preserves the existing symbol-processing phases. [Unity build-phase API](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/ios/xcode/pbxproject/insertshellscriptbuildphase).

Re-export from the updated Unity project to get these fixes; an older Xcode export is unchanged. The Windows export verifies Unity compilation and Xcode project generation. Compiling the Apple native plugins and confirming signing still requires Xcode on a Mac.

**Window → General → Test Runner → EditMode → ARVisualizer.Tests** covers command parsing, UDP dispatch/correlation/deduplication, prefab dimensions and events, front-face coordinates, wall orientation, size limits, native sample layout and the simulated AR flow. The integration test places an anchored screen, points at it, locks adjustment, grows/shrinks/rotates it, verifies fingertip priority and tracking loss, preserves authored HUD layout, and exercises undo. Goggle checks cover ray hover, actual UDP button clicks, duplicate click packets, the always-available HUD toggle, retained placement targets, pointer locking, mode restoration and shutdown. With graphics enabled, they compare rendered eye positions, near/far stereo disparity, depth occlusion against a depth-disabled control, and the lens compositor.

Screen app checks cover compact bottom controls, gallery scrolling in both modes, held ray and phone swipes, fingertip button activation and drag/lift, clipped-thumbnail exclusion, a 600-item list with bounded thumbnail memory, cancelled selections, permission-denied and empty-library states, photo display and resizing, modal click priority in stereo, actual H.264 video playback and pause/resume, and temporary-file cleanup. Editor tests use sample media and simulated library replies.

Phone-input checks send actual Input System touch events for both halves, holding/dragging/releasing, GUI exclusion, disabled/lost tracking, modal back, and switching modes in both directions using the fixed overlay button. Goggle checks also verify the two/three-button bottom row and restore the normal controls' authored geometry on exit. Test the same interactions on iPhone in both landscape orientations before use.

On the iPhone, check both landscape orientations, wall/table placement, knuckle targeting, fingertip contact/release near face edges, hand occlusion, depth loss, adjust commands, and background/resume. Test Photos with full, selected and denied access; portrait photos/videos, iCloud downloads, cancellation, and playing media on multiple screens. For the headset, calibrate lens centres/eye separation and distortion, check fusion at near and far distances, point at every HUD and gallery button, and exit/re-enter goggle mode. Windows Unity tests cannot validate the viewer's optics, compile Apple's native frameworks, access device Photos or establish real fingertip-contact accuracy.

Implementation references: [Apple index MCP joint](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/jointname/indexmcp), [hand chirality](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/chirality), [scene-depth reconstruction](https://developer.apple.com/documentation/arkit/displaying-a-point-cloud-using-scene-depth), and the installed ARKit package's **Includes~/UnityXRNativePtrs.h** for the native session bridge.
