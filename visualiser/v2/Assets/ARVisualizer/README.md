# iPhone AR screen visualiser

Open **Assets/ARVisualizer/Scenes/ARVisualizer.unity** in Unity **6000.6.0f1**. The scene uses a saved HUD and screen prefab, with a runtime AR rig. The iPhone 14 Pro Max supports the LiDAR depth used for fingertip contact. Both landscape orientations are supported.

## Using both hands

In phone mode, the left-side **WI-FI GESTURES** queue shows the six most recent GestureReceiver packets, newest first, with arrival time and **L/R** hand. It records received gestures even when their action cannot run, and keeps collecting while the HUD is hidden or goggles are active. The panel is hidden in goggle mode and follows the HUD toggle. The receiver retains up to 32 receipts for the current session; reconnecting does not clear them. Empty/invalid packets and non-gesture messages are excluded. Direct UDP/local commands are not gesture receipts.

Edit **LandscapeHUD → Safe Area → Gesture History** to move or resize the panel and style its heading and rows. Its sample rows are Editor previews; Play replaces them with real receipts or “Waiting for gestures...”. The **Gesture History HUD → Rows** array lists visible rows from newest to oldest; add/remove saved Text children and their array entries to change the queue length. Runtime updates text and visibility without changing authored geometry.

The rear camera tracks **four printed glove markers**, using the bundled AprilTag `tag25h9` detector. Left root/tip are IDs **0/1**; right root/tip are **2/3**. The root marker replaces the index-MCP observation, and the tip marker replaces the fingertip observation. Each ray keeps its **3 cm inward offset in camera space**. Marker IDs determine identity immediately, including when hands cross; there is no bare-hand/Vision fallback. Open the [fixed-size A4 PDF](../../output/pdf/Glove-markers-A4.pdf) for printing (or [SVG](Markers/Glove-markers-A4.svg)) and [marker setup](Markers/README.md) for placement/calibration.

Configure **AR Visualizer → Hand Pointer Source → iPhone glove markers → Markers** in the Inspector. The component retains its existing script name/GUID for scene compatibility; its Add Component menu name is **Glove Marker Tracking**. Detection runs on the current ARFrame's existing luma plane in a background worker at 15 samples/second by default, with two AprilTag worker threads. No additional camera session, model download, OpenCV binary or network service is required.

Each hand has its own ray, marker, app menu, placement preview, move selection, adjust selection, and gallery press/drag. Both hands may operate different screens at once. A screen being adjusted, moved, or browsed in the media picker by one hand cannot be acquired by the other. Only one launcher owns a given screen at a time; point away to let its grace period expire before opening it from the other hand. Surface highlights stay visible while either hand is placing. Tracking loss never redirects that hand's commands to the other hand. Normal mode supplies one centre fallback only when **neither** hand is tracked and the brief marker-memory interval has elapsed, for the currently selected controls hand. During short dropouts, the pointer pauses instead of flashing back to the centre. Goggles require a tracked hand and lock **both** pointers on; exiting restores their individual switches.

**Commands:** prefix any command below with `left.` or `right.`, for example `left.adjust.enter`, `right.move.confirm`, or `left.ui.rightclick`. Hand selection and mouse button are independent: `left.ui.rightclick` is a right-click from the left hand. Unprefixed network commands remain right-hand commands. An alternative JSON form is `{"command":"ui.click","hand":"left"}`; conflicting selectors are rejected. The HUD shows **Controls: Left/Right** and phone buttons act on the hand selected by the last non-status command. Goggle button clicks inherit the hand that clicked them.

Open **[Resources/commands.json](Resources/commands.json)** for the complete grouped catalogue. Each entry contains a description, scope, left packet, right packet and compatibility aliases. Unity validation and both Python senders read this file; add a matching runtime handler when adding a new command. `goggle.enter/exit` affect the shared display; `screens.clear` clears the shared screen collection and both hands' pending placements. `screen.undo` cancels the issuing hand's move/placement first, otherwise removes the shared newest screen.

```json
{"id":"left-click-001","command":"left.ui.click","token":""}
```

```sh
python Tools/send_command.py 192.168.1.42 left.app.choose
python Tools/send_command.py 192.168.1.42 move.enter --hand right
python Tools/keyboard_command.py 192.168.1.42 --hand left
```

The keyboard starts with the requested hand (right by default). **Tab switches hands** and releases a held gallery press on the previous hand. Existing keys then operate the selected hand: **I** pointer, **=** placement, **P** place/confirm move, **F** choose app, **U** click/hold/scroll, **A** adjust, arrows resize/rotate, **M** move, **Enter** confirm, **X** cancel, **H** check that the selected root marker is visible. Display/clear/undo keys keep their shared effects described above.

Replies identify the issuing `hand`; top-level pointer, target, menu and mode fields describe that hand. `leftHand` and `rightHand` provide both tracking/pointer switches, placement/move/adjust selections and independent ray/touch coordinates. Each hand snapshot also includes `reportedHand` (the side encoded by the marker ID), `identityStabilized` (always false with explicit marker identities), `rootConfidence`, and `sampleAgeSeconds`. **Controls: Right** describes the hand selected for phone controls, not the identity of every detected hand. `ScreenSurface.LeftInput`, `RightInput`, `HandInputChanged`, and `HandPointerClicked` expose this to scripts. Existing Inspector interaction events expose a single primary contact, preferring touch over ray and right over left for ties. Use the hand-specific events when an app needs both contacts.

In Editor, the mouse simulates **Editor Hand** on Hand Pointer Source. Automated input can call `app.Hands.Left.SetEditorSample(...)` and `app.Hands.Right.SetEditorSample(...)` together. `ARVisualizerApp.UseHand` is a synchronous dispatch scope; **never keep it across an await**. Anchor requests capture their owner and re-enter that hand's scope only after the await finishes.

### Marker identity and tracking loss

Root and fingertip IDs must all differ and be in the `tag25h9` range **0–34**. Only configured IDs are accepted. The detector requires an exact decoded payload, a minimum decode margin and a minimum edge length; multiple visible copies of the same ID invalidate that role for the frame. An unknown marker cannot displace a configured one. Briefly missing roots hold that hand's last valid ray; missing roots or tips stop direct touch immediately. Returning markers reacquire their assigned side immediately.

Under **Hand Pointer Source → Tracking stability**, **Tracking Grace Seconds** (0.3 seconds) debounces detection loss independently for each hand. Until that interval expires after the last successful camera capture, the ray and tracked HUD state remain active at the last valid aim. Ray commands use that held aim; direct touch requires a fresh root, tip and depth sample. Set the grace to 0 to disable it, or increase it for longer interruptions (at the cost of holding old aim longer). A held 3D position leaving the camera view, an AR tracking reset, a viewport change or pausing the app clears the hold immediately. `HandPointer.IsRayHeld` distinguishes the hold from a current observation.

**Maximum Sample Age** (0.2 seconds) rejects old frames for updating aim and touch. Capture latency counts towards the grace period; repeatedly reading the same native frame cannot extend it. **Marker Memory Seconds** (0.3 seconds) sets the minimum delay before centre fallback; fallback also waits until both rays have finished their grace periods. `hand.calibrate` remains a compatibility command that checks the selected root marker; it does not change IDs, swap hands or alter pointer switches. Change the physical tags or their Inspector assignments to change the setup.

## Use

1. In normal mode, tap the right-hand **PLACE SCREEN** button (or send `place.enter`), then move the phone slowly to find flat surfaces. The same button becomes green **PLACE HERE**. The highlights use the original sample scene's feathered plane prefab.
2. Show the **root marker attached near the base of your index finger** to the rear camera. The ray follows a point **3 cm to its left for a right hand**, or **3 cm to its right for a left hand**, as seen in the phone's view. In normal mode, one ray moves to the centre when neither hand is detected. With the selected pointer off, placement uses the centre crosshair when that hand is tracked or neither hand is visible.
3. Tap green **PLACE HERE** or send `screen.place`. Confirmation is enabled only when a valid surface is targeted. A black **16 × 9 cm flat rectangle** is anchored to the surface. Placement then exits place mode, returns the button to **PLACE SCREEN**, and opens **Choose app** for the new screen. The pointer is enabled so you can select an app. Failed or cancelled placement leaves the current mode unchanged. Goggles retain their separate **Enter Place / Exit Place** and **Place Screen** controls.
4. Screens lie flush against their supporting surface. On walls and slopes their width stays level with the horizon. On horizontal surfaces they lie flat and can rotate within the surface plane. Surfaces within 15 degrees of horizontal count as horizontal.
5. Use `place.exit` to cancel placement without adding a screen. Point the ray at a screen to report face coordinates. Bring the fingertip to its face to report touch coordinates instead.
6. Point the ray at a screen and send `adjust.enter`. This locks that screen for adjustment and exits place mode. Grow, shrink or rotate it with the commands below, then send `adjust.exit`.

**Normal-mode controls:** **Pointer On / Off** and the single **Place Screen / Place Here** button sit on the right. There is no Undo button in normal mode. **Left Click** and **Right Click** send `ui.click` and `ui.rightclick` to the current ray target. **Screen Settings** locks the pointed screen and opens Choose app, Smaller, Larger, Rotate Left, Rotate Right, **Move Screen**, **Delete Screen** and Done controls. Delete removes that selected screen, its anchor and its media, even if the ray has moved to another screen. Rotation remains available for horizontal supports only. These buttons and the settings panel are hidden in goggle mode and when the HUD is hidden. Edit their saved shapes and positions under **Landscape HUD / Safe Area / Normal Actions**; the panel is a child of that row. The placement button is **Safe Area / Controls / Mode Button**; captions and confirmation colors are on **Visualizer HUD**.

Each grow step adds **1.6 cm width**; each shrink step removes the same amount. Height follows the current media's aspect ratio, or **16:9** for a black screen (0.9 cm height per step). Loading media preserves the chosen width and the screen's anchored centre/orientation; a 3:4 portrait photo on a 16 cm wide screen makes it about 21.3 cm tall. The screen remains flat. Default size is 10 steps; the range is 1–100 steps (1.6–160 cm wide). Ray/fingertip bounds and reported coordinates use the current dimensions. Rotation defaults to **5 degrees per command**, adjustable on **AR Visualizer App → Screen interaction**. Rotation is rejected for wall/sloped screens so they stay level.

Adjust mode keeps the same screen selected when the ray moves elsewhere or tracking is lost. Repeating `adjust.enter` keeps the selection; exit and enter again to select another screen. Entering place mode exits adjust mode. Undoing/clearing the selected screen also exits adjust mode. Placement requires place mode and a tracked surface; it is rejected while the ray points at an existing screen. Screens follow their AR anchors but are not saved across app sessions. The default limit is 100 screens.

### Move an existing screen

Point at a screen, open **Screen Settings**, and choose **Move Screen**. Flat surfaces are highlighted and an outline shows the destination using that screen's current dimensions. Aim at the new position and tap green **Move Here**. **Cancel Move** replaces the Screen Settings button while moving. The selected screen stays at its original position until confirmation succeeds; cancellation leaves it there.

Moving preserves the screen's ID, size, photo/video and playback state. It replaces the screen's anchor without making another screen, including when the screen-count limit is reached. Rotation is preserved between horizontal supports; moving to a wall or slope makes the screen level with the new surface. App menus are suspended while moving. Tracking loss prevents confirmation, and changing viewing modes, backgrounding the app, or deleting the selected screen cancels the move. Undo during a move cancels it; it does not delete the screen.

Wi-Fi commands are **`move.enter`**, **`move.confirm`**, and **`move.cancel`**. Enter selects the screen locked in settings/adjust mode, or the currently pointed screen; repeated enter keeps that selection. In goggles, enter by command, then use the floating **Move Here** and **Cancel Move** buttons or send commands. The last surface target is retained while aiming at the confirmation button. `screen.place` also confirms an active move. `status` includes `moveModeEnabled` and `movingScreenId` (zero when not moving).

The square **HUD − / HUD +** button in the top-left safe area hides or shows the status panels, controls and aim marker. The button stays visible and clickable. Hand tracking, the world-space ray, surface highlights, screens and Wi-Fi commands continue working while the HUD is hidden. Showing the HUD restores each panel's previous visibility.

## Goggle mode

The square button at the top centre shows a **goggles icon** in normal mode and a **phone icon** in goggle mode. Tap it to switch modes. It stays in the same position on the physical phone display, above the stereo compositor, outside the two eye views. It remains touchable with the HUD hidden or hand tracking unavailable. It has its own safe-area canvas; the default status bar sits below it.

Send `goggle.enter` for side-by-side **left and right eye views** in a phone VR viewer. The screens, hand ray and floating HUD are rendered independently from two eye positions, so nearby objects have greater binocular disparity. The HUD follows the user's view, and the hand ray can highlight its enabled buttons. Send `ui.click` to activate the currently pointed button. Send `goggle.exit` to return to the ordinary phone HUD.

Goggles use a **4:3 camera viewport and 4:3 eye images**, fitted into the two display halves without stretching or cropping to the phone's wide aspect. Black margins may remain around the fitted views. The camera projection and video/depth sampling transform are requested for that 4:3 viewport; changing only a render texture's dimensions would leave AR Foundation's phone-sized crop in place. A supported 4:3 camera format is preferred, and any format change is restored on exit. Hand coordinates and surface raycasts use the same camera geometry.

The default panel is **0.8 m** in front of the eye midpoint and up to **0.9 m** wide, with a **1280 × 960 (4:3)** floating layout. Edit **Landscape HUD → Goggle HUD → Floating panel** to change its distance, width or vertical position. **Fit Within Eye Views** reduces its width using the actual camera projection to keep all controls visible to both eyes. Top and bottom controls anchor to the taller layout; normal-mode RectTransforms are restored on exit. The existing child Rect Transforms, text, button actions and HUD toggle remain editable in the LandscapeHUD prefab. The default UI material keeps the floating controls readable in front of the scene.

**Headset calibration:** edit **Landscape HUD → Stereo Goggles** on the same prefab. The default eye separation is **0.064 m (64 mm)**. **Match Camera Field Of View** is enabled to preserve the camera's complete vertical view. Disable it only for a manually calibrated headset projection; **Vertical Field Of View** then supplies the angle (70° by default). Match eye separation to the user and the viewer's adjustable lenses. Adjust **Left Lens Centre** within the physical display half to align the image with the left lens; the right centre is mirrored. Its position is accounted for inside the fitted 4:3 view. **Distortion** controls radial lens correction; set both coefficients to zero for an undistorted comparison. **Eye Centre Offset** accounts for the position of the eye midpoint relative to the rear phone camera. These are manually adjustable defaults for an unbranded viewer, not a calibrated or QR-imported Cardboard profile. Tune them on the actual headset until a distant object fuses comfortably into one image; then check a nearby screen. The software cannot measure the viewer's physical lens adjustments.

Keep the phone horizontal and leave its **rear camera and LiDAR unobstructed** by the viewer. ARKit continues to supply tracking and camera images. The real-world feed comes from **one camera**; it is reprojected into the eye views using available human/environment depth, including depth occlusion of virtual screens. Missing depth uses a configurable **4 m video distance** without occlusion. Depth edges, newly revealed areas and areas outside the camera's field of view remain approximate; this is not a two-camera stereo capture of the real world. The virtual objects and HUD have actual geometric stereo views. **Render Scale** defaults to 0.75 to limit the cost of rendering both eyes.

Goggle mode prefers environment depth for the whole scene and falls back to the available depth source. Exiting restores the application's normal occlusion preference. An explicit **No Occlusion** setting is still respected in either mode.

Both rays are **locked on** in goggle mode; `pointer.off` is rejected and the pointer button is hidden. The bottom row normally contains **Enter Place** and **Undo**. In place mode it contains **Exit Place**, **Place Screen** and **Undo**. These controls fill the row with equal spacing; adjust **HUD → Controls → Goggle Button Spacing** in the Inspector. Both eye views render both rays; each hand produces one activation per click command. Exiting restores each pointer's previous setting, the normal-mode controls and their authored positions (the separate Place Screen and Undo controls are hidden), camera output, canvas materials, safe-area fitting and normal touch input. Once the marker's tracking grace expires, tracking loss clears the hovered button and prevents a stale click. In normal mode, `ui.click` operates the screen app menus; the phone HUD uses touch.

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

The enlarged app chooser expands upward from the **bottom edge of the selected screen**. Its option buttons span about 93% of a default screen's width, with taller targets and larger text. The launcher row uses 100% of the screen width by default, with a 4–24 cm physical width limit. Timer/flashcard management controls sit just below the screen so they do not cover the app. Edit **Launcher Width Fraction / Minimum Width / Maximum Width** on **Screen App Menu**, and the button RectTransforms under **Screen launcher** in its prefab. The app options expand upward from the bottom row.

After placement, the chooser stays available until dismissed, an app is selected, or another screen is targeted. Otherwise, the launcher stays available briefly while moving the ray from the screen to the menu. It is hidden while the screen is being adjusted. Collapsing the drop-down keeps it closed during the current visit; point away until the launcher hides and return, click **Choose app**, or send `app.choose` to reopen it. The command requires a current ray target, so looking away cannot open the menu for a previously pointed screen; in normal mode, hand loss uses the current centre target. Close an open photo-library popup before choosing another app.

The **AR photo library** is a continuous vertical list, newest first, with photo/video labels. Aim at a thumbnail and send `ui.click` to display it on that screen. To scroll, **hold left click and move the ray up/down**, then release. Use the normal HUD's **Left Click** button, hold **U** in the keyboard controller, or send `ui.press` followed by `ui.release` over Wi-Fi. A short press selects the item; a swipe never selects it. On the phone, you can also drag the gallery directly or swipe in empty left-half space while the ray points at the list. **Refresh / retry** and **Close** remain available; there are no page buttons. Thumbnails load as needed, with twelve recycled UI tiles and at most 48 decoded thumbnails retained after a load.

Scrolling works in normal and stereo goggle modes; while the popup is open, clicks and the ray are reserved for it. The top-left HUD toggle does not hide app menus. AR tracking loss cancels a held press. Losing a tracked hand cancels its drag; normal mode then uses the centre target for the next interaction. Changing modes or closing the popup also cancels a press. A 15-second timeout releases abandoned remote presses without clicking. Closing the picker or removing its screen cancels pending library work.

**Index-fingertip contact activates app-menu buttons**, including Choose app, app options, playback controls, Refresh and Close. This uses measured fingertip depth and also works with the ray off. Gallery thumbnails select when you lift your fingertip; sliding it up/down scrolls instead. Other buttons activate once on contact and require withdrawal before another activation. The phone and floating **HUD are excluded** from fingertip activation. Edit contact tolerance and swipe sensitivity on **Screen Menu Input** in the screen-app prefab; verify the feel on the iPhone.

The library floats just above the selected screen's top edge, aligned with its surface, with a 1.5 cm gap and 3 cm lift towards the viewer. Its bottom edge follows the screen's anchor and current dimensions. **Auto Size Popup** keeps its horizontal visual angle near **38 degrees** as you move closer or farther, accounting for its raised centre and viewing direction. Normal mode uses the camera position; goggles use the eye midpoint, with the same physical panel rendered for both eyes. The default physical width limits are **8 cm–4 m**; these limits take precedence at extreme distances. Edit **Popup Visual Angle**, **Popup Minimum/Maximum Width**, gap and surface offset under **Screen App Menu → Placement** in the prefab. Disable automatic sizing to use the fixed **Popup Width** instead. Both the launcher and library use **Occluded Screen UI**, so nearer real objects, including hands and arms, cover them through the existing AR occlusion system. Human-depth edge quality still needs checking on the iPhone; the Editor checks simulated depth in normal mode and both stereo eyes.

The first launch asks for iOS **Photos** access. Apple's permission prompt is system UI and must be answered using the phone's touch screen; it cannot be clicked by the AR ray. You can grant full or selected-photos access. With selected access, only those items appear; change that selection in iPhone Settings, then refresh. Denied/restricted access and empty libraries show a message in the AR popup. No Photos permission is requested until the Photos app is opened. [Apple Photos authorization](https://developer.apple.com/documentation/photos/phphotolibrary/requestauthorization(for:handler:)).

Photos are loaded upright at up to 2048 pixels on the longest side. Videos are exported to a local MP4 using PhotoKit's 720p preset, then played with Unity VideoPlayer. iCloud items may take time to download. The original library is never edited. Each screen owns its playback and temporary media; **Close app** releases it and restores a black 16:9 rectangle at the chosen width. **Choose media** opens the picker again, and **Play / Pause** controls video. Videos loop by default and pause when the app goes into the background. The screen automatically takes the photo or prepared video's display aspect ratio, including portrait, square and wide formats, so the full image fills the rectangle without added borders or cropping. Video sizing also accounts for [non-square pixels reported by Unity](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/video/videoplayer). Grow/shrink preserves the current media ratio. Rotation and real-world occlusion continue to work.

Edit **Tools → AR Visualizer → Open screen apps prefab**, or **Assets/ARVisualizer/Resources/ScreenAppMenu.prefab**. The launcher, popup, buttons, thumbnail areas, labels and positions are saved Unity UI objects. Enable **Screen launcher** or **Photo library** in Prefab Mode to preview it; runtime controls their visibility. Edit each child's Rect Transform, Image and Text. **Photo library → Media viewport** has the vertical Scroll Rect and clipping mask; its Content contains the recycled thumbnail buttons. **Media Library Grid → Tile layout** controls their runtime size, spacing and columns. **App registry** lists the drop-down entries; `photos`, `timer`, and `flashcards` are built-in app IDs, while additional entries can invoke their **Launch** Unity event with the selected ScreenSurface. The Screen prefab has a **Screen Media** component that uses the same **Body** quad for black, photos and videos. `ScreenMedia.ShowPhoto`, `ShowVideo`, `TogglePlayback` and `Clear` are available to other apps; ShowPhoto takes ownership of the supplied texture. These are local APIs, not remote file-loading commands.

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

### Timer and German flashcards

The chooser contains **Photos & videos**, **Timer**, and **German flashcards**. An empty screen opens it automatically when pointed at, in normal and goggle modes. Each screen owns its app independently; looking away or hiding the HUD does not close it. Choosing an app replaces that screen's current content. Choosing Photos preserves the current app until a photo/video is actually selected. **Close app** restores a blank screen; `app.choose` opens the chooser for any currently pointed screen.

**Timer:** a **MM:SS** countdown, initially **01:00**, with four large digit targets. Point at a digit and send **SWIPE_UP** to add its step, or **SWIPE_DOWN** to subtract. Left to right, the steps are **10 minutes, 1 minute, 10 seconds, 1 second**. Values clamp to **00:00–99:59**; steps carry across digits. Clicking or touching a digit also adds its step. Editing pauses a running timer and sets its new reset value. **Start / Pause** controls the countdown; **Reset** stops it and restores the last edited duration. At zero the screen shows **Time's up!**. This is an in-app countdown with no background alarm or notification.

**German flashcards:** 32 beginner German–English cards, including articles for nouns. **SWIPE_UP** reveals the English meaning; **SWIPE_DOWN** hides it. **SWIPE_RIGHT / SWIPE_LEFT** advance / go back, hiding the answer on the new card. The deck wraps at either end. Click the card or **Reveal / Hide** to flip it; large **Previous / Next** buttons provide the same navigation. Progress belongs to that screen for the current app session and resets when reopened.

**WRIGGLE_FINGERS** remains `ui.click`. Digit and flashcard buttons also accept measured index-fingertip contact, once per contact with withdrawal required to repeat. Missing marker/depth samples cannot repeatedly activate a held fingertip. App controls work with either hand and use the existing depth-occluded UI material in normal and stereo modes.

The `appGestures` section of [commands.json](Resources/commands.json) contains the contextual mappings. They apply only while the ray targets the open app face in free mode. Menus, movement, placement and screen settings keep their existing controls. **ZOOM_IN / ZOOM_OUT** still open settings and resize; explicit `settings.enter` still opens settings. In settings, swipe left/right rotate and **CRUMPLE_AND_TOSS** cancels the edit. For remote buttons, use `app.increase`, `app.decrease`, `app.startpause`, `app.reset`, `app.previous`, `app.next`, `app.reveal`, or `app.hide`, each with the usual `left.` / `right.` prefix. Increase/decrease use the selected or pointed digit, defaulting to minutes; the other commands target the pointed app screen.

Edit [ScreenAppView.prefab](Resources/ScreenAppView.prefab) to change the timer digits, card area, colors, text and button positions directly in the Editor. Enable **Timer** or **German Flashcards** to preview its layout. **Screen App View** holds the UI references; keep the four time digit references in place-value order. The same RectTransforms define the gesture and touch hit targets. The view follows the screen's width at 16:9, including resizing and rotation, with a small front offset for depth occlusion. [Screen.prefab](Prefabs/Screen.prefab) has **Screen App Content** references for this view and its vocabulary file. Edit [german_vocabulary.json](Resources/german_vocabulary.json) to add/change cards (`german` and `english` fields); invalid or empty entries are skipped. No Photos permission or online service is needed for these two apps.

**Tools → AR Visualizer → Prepare timer and flashcard apps** creates missing assets and performs the one-time chooser upgrade. Subsequent runs preserve authored layouts. `CountdownTimer` owns countdown rules, `ScreenAppContent` owns each screen's app state and input, and `ScreenAppView` owns the editable UI references.

### How fingertip contact is measured

The ray aims from the camera through the **point beside the root marker**; it does not estimate the finger's pointing direction. Marker IDs supply left/right identity directly. LiDAR reconstructs the root marker's position from the same camera frame; the lateral offset is applied along the phone camera's horizontal axis. The ray origin, placement target, screen hit coordinates and HUD marker all use that offset point.

Edit **Hand Pointer Source → Ray offset → Lateral Offset Metres**, initially **0.03**. When reliable root depth is unavailable, the ray uses **Fallback Root Depth Metres**, initially **0.5 m**; the physical offset is then approximate. This estimate is used only for the ray and never creates fingertip contacts.

Contact needs both configured root and tip markers, plus valid **raw LiDAR depth from that same ARFrame**. Camera intrinsics and frame pose reconstruct their positions in AR session space, then Unity transforms them into world space. **Left/Right Contact Offset** corrects the tip marker centre to the actual glove contact point using the detected marker's orientation. AprilTag pose supplies this rotation only; estimated tag distance never supplies touch depth. With zero offsets, the marker centre is the contact point. See [calibration instructions](Markers/README.md). The lateral ray offset never changes touch coordinates.

The native worker checks depth confidence, samples a locally consistent patch, and rejects implausible tip-to-root distances to reduce background-depth mistakes. Samples older than **0.2 seconds** are rejected. Contact starts within **1.2 cm** of the face, with a **1.8 cm** release distance to reduce chatter; the start tolerance is editable in the application Inspector. This is an approximate depth-based contact estimate, not a hardware touch sensor. Thin fingers, occluded tips, motion and LiDAR edges need testing on the phone.

If reliable fingertip depth is unavailable, there is **no touch event**; ray input can continue. A 2D image overlap is never used as a substitute for contact. `pointer.off` turns off ray input/visualization while hand tracking continues for direct touch. **No Occlusion** disables the AR depth manager and therefore also prevents device depth-based touches; use Human or Environment occlusion for touch.

In the Editor, the mouse simulates the root marker. Choose **Hand Pointer Source → Editor Hand** to preview either hand (default Right). Hold the left mouse button to simulate a tip at **Editor Touch Depth Metres**, initially 0.5 m from the camera. Set this to the screen's camera depth to exercise contact. A physical iPhone is needed to validate marker visibility, glove calibration and LiDAR measurements.

## Mac gesture connection

The scene's **GestureReceiver** connects to the Mac's gesture WebSocket server over Tailscale. Set **Mac Tailscale IP** and **Mac Port** on **Gesture Web Socket Receiver** (default port **8765**). The HUD's existing network row now shows **MAC Connected** in green, **MAC Connecting** in amber, or **MAC Disconnected / Connection error / Disabled** in red. **Paused** appears in amber. The same status appears on the floating goggle HUD, alongside the existing UDP listener address.

The indicator follows the WebSocket's open, close and error events; a running UDP listener or an assigned Tailscale IP does not mean the Mac is connected. An idle connection remains connected without requiring gesture packets. A network interruption appears once the WebSocket reports it; this is not a separate Tailscale tunnel/heartbeat monitor. Disabling or pausing the receiver clears its connection immediately, and re-enabling or resuming starts a new connection. Connection errors are available through `GestureWebSocketReceiver.LastError` and the Unity Console.

On **Landscape HUD → Mac connection status**, assign **Gesture Receiver** explicitly if needed. By default, the HUD finds the scene receiver that uses this app's Communication Manager. The label remains **Safe Area / Top Bar / Status / Wi-Fi Status** so its Rect Transform and typography stay editable. Customize the three status colors there, or disable **Tint Mac Connection Status** to retain the Text component's color. Scenes without a gesture receiver retain their UDP status display.

## Eleven Mac gestures

Gesture bindings and guide captions are editable in the `gestures` section of [commands.json](Resources/commands.json). Example: `{"type":"GESTURE","gesture":"pinch_and_pull","hand":"left"}`. Names are case-insensitive; omitted hand defaults to right. Both `swipt_left/right` and `swipe_left/right` work. `wriggle_fingers` remains an alias for `wiggle_fingers`. Legacy `zoom_in/out` still open settings and resize, but are omitted from the eleven-button panel.

| Gesture | Action |
| --- | --- |
| `CRUMPLE_AND_TOSS` | Cancel an edit; otherwise go back in the open menu; otherwise delete the pointed screen. Never creates a screen. |
| `PINCH_AND_PULL` | Select the pointed screen for resizing. Swipe up grows, down shrinks, one step at a time. |
| `PINCH_AND_DRAG` | Move the pointed screen. Aim the footprint at a new flat surface. |
| `PINCH_AND_ROTATE` | Select the pointed screen for rotation. Left is anticlockwise, right clockwise, by 5 degrees. Horizontal surfaces only. |
| `POINT_AND_ROTATE` | Toggle floating screen options: Choose app, Move, Resize, Rotate, Delete screen, Done. |
| `GRAB_AND_RELEASE` | Confirm an edit/move, activate a selected menu item, perform the app's primary action, or create a screen on a blank flat surface. |
| `WIGGLE_FINGERS` | Click the button under this hand's ray. |
| `SWIPE_UP` / `SWIPE_DOWN` | Resize larger/smaller; timer increase/decrease; flashcards reveal/hide; library scroll forward/backward. On displayed media, up opens the library. |
| `SWIPE_LEFT` / `SWIPE_RIGHT` | Rotate left/right; choose timer digit; previous/next flashcard, photo/video or menu item. |

Move, Resize and Rotate retain the selected screen for that hand even if the pointer leaves. **Grab** confirms; **Crumple** cancels. Cancelling restores the size and rotation from the beginning of the edit; switching between Resize and Rotate preserves that snapshot. A move retains the original anchor until confirmed; starting a move confirms earlier size/rotation edits. A status label shows the current edit even when the debug HUD is hidden. Existing size limits and media aspect ratios still apply. Enable the pointer first in phone mode; goggles always enable it.

Menus support both ray clicks and swipe selection. The green item is selected by **Grab**; **Wiggle** always clicks the ray target. In the photo library, up/down scroll one row, left/right change the selected thumbnail, and Grab opens it. Crumple closes the library or cancels a pending media load. A loading thumbnail cannot be selected until ready.

For timers, left/right selects one of four digits independently for each hand; up/down changes that digit even when the ray is elsewhere on the timer. Without a selection it uses the pointed digit, defaulting to minutes. Grab starts/pauses the timer. For flashcards, Grab flips the card. For video, Grab plays/pauses; for a photo, Grab opens the library.

Creation does not require entering place mode and never falls back to deleting the newest screen. Invalid surfaces, a busy screen owned by the other hand, open photo pickers and unfinished placement/movement reject conflicting gestures. A newly created screen still opens its app chooser.

Previous/next media uses the same newest-first order as the gallery, includes photos and videos, and wraps at either end. An inaccessible item starts from the first accessible item. Explicit `media.next` / `media.previous` commands also work on black screens. Limited Photos access uses only permitted items. The load captures the target screen, ignores repeated requests while busy, and cancels if the screen is removed. Permission/export failures keep the previous media; a video decoding failure follows the existing player behavior and clears the screen. No gallery thumbnails are downloaded for this action.

### Quiet HUD gesture guide

Hide the debug HUD with the top-left toggle. A small green strip stays on the **right safe-area edge**. Point either tracked hand at it to reveal the guide; it stays open while pointing at the panel and closes 1.2 seconds after pointing away. Centre fallback alone cannot open it. The guide works on the phone and inside both goggle views. Gestures aimed at the guide do not click, place or delete screens behind it.

Edit **LandscapeHUD → Safe Area → Gesture Guide** to change the strip, panel and rows, or **Gesture Edit Status** for the edit indicator. **Gesture Guide HUD → Close Delay** adjusts dismissal time. Keep the guide outside the HUD's `hideableContent` list. **Tools → AR Visualizer → Prepare gesture controls and guide** upgrades the saved HUD and screen-menu prefabs once; later runs preserve manual layout edits. Edit **ScreenAppMenu → Screen launcher → Screen Options** for the six screen-option buttons.

## Wi-Fi commands

Connect the phone and controller to the same LAN. Send one UTF-8 command per unicast UDP datagram to the address in the HUD, **port 7777** by default. Plain commands and JSON are accepted. Every command in the table has a `left.` and `right.` variant, listed explicitly in [commands.json](Resources/commands.json). The unprefixed examples retain right-hand compatibility:

```json
{"id":"request-001","command":"adjust.grow","token":""}
```

| Command | Effect |
| --- | --- |
| `screen.context` | Context action: exit settings, delete the pointed screen, or create on a valid blank surface |
| `settings.enter` | Enter settings and show the normal HUD panel if visible |
| `settings.grow` / `settings.shrink` | Enter settings if needed, then resize one step |
| `media.next` | Request the next photo/video for the pointed screen in free mode |
| `pointer.on` / `pointer.off` | Enable/disable the root-marker ray; touch tracking continues |
| `hand.calibrate` | Check the selected root marker; IDs determine identity and are never reassigned |
| `goggle.enter` / `goggle.exit` | Stereo eye views, floating HUD and locked-on ray / normal phone HUD |
| `ui.click` | Left-click the ray's app-menu control, screen face, or a HUD button in goggle mode |
| `ui.press` / `ui.release` | Hold/release left click on an app button or media list; move the ray vertically while held to scroll |
| `ui.rightclick` | Right-click the ray's screen face, or go back from an open app menu |
| `app.choose` | Open the app chooser on the ray-pointed screen, including one already showing media |
| `place.enter` / `place.exit` | Enter/quit highlighted-surface placement |
| `screen.place` | Place a flat screen, exit place mode, and open its app chooser |
| `move.enter` | Select the adjusted/pointed screen and highlight destination surfaces |
| `move.confirm` | Move the selected screen to the current surface target, preserving its media |
| `move.cancel` | Cancel relocation and keep the original screen position |
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
# Aim the root-marker ray at the screen first:
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

**Safe Area** fits its own anchors to the iPhone's screen margins; edit the panels inside it. **Aim Position** follows the root marker; style its Aim Marker children. The Canvas Scaler uses a 1280 × 720 reference. Edits before Play persist; edits during Play are temporary. Scene overrides and prefab edits work normally. Existing customized layouts are not rebuilt by setup tools.

## Script map for maintenance

The runtime scripts are under **Assets/ARVisualizer/Scripts**. Start with the script that owns the behavior you want to change:

| Behavior | Script |
| --- | --- |
| Application lifecycle, modes, ray/touch priority and placement targeting | `ARVisualizerApp.cs` |
| Screen relocation with media and anchor preservation | `ARVisualizerApp.Moving.cs` |
| Per-hand state and bounded synchronous dispatch | `ARVisualizerApp.Hands.cs`, `HandPointer.cs` |
| Command catalogue used by Unity and PC senders | `Resources/commands.json`, `Tools/command_catalogue.py` |
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

## Compact debug controls

The phone HUD has two compact rows at the bottom right: **Left Click / Right Click / Screen Settings**, then **Pointer / Place Screen / Gestures**. **Gestures** toggles a panel beside screen settings. The two panels can stay open together: hiding the gesture panel does not leave adjustment mode or discard its selected screen.

Choose **Left Hand** or **Right Hand**, aim that hand's ray, and tap any of the eleven gesture buttons. The buttons use the same `gestures` and `appGestures` mappings in `commands.json` as GestureReceiver, including timer digits and flashcards. The result appears below the buttons. These local tests do not send a network packet, change Mac connection status, or add fake Wi-Fi receipts to the history queue. Hiding the HUD or entering goggles closes the debug panel; its toggle is phone-only.

Edit **LandscapeHUD → Safe Area → Gesture Controls → Gesture Panel** to move/resize the panel, its hand selectors, buttons and result label. The saved **On Click** string on each button is the gesture name. Edit **Normal Actions** and **Controls** for the compact toolbar, and **Normal Actions → Screen Settings Panel** for settings. **Tools → AR Visualizer → Prepare compact debug HUD** performs the one-time prefab upgrade; subsequent runs preserve manual layout edits.

## Depth occlusion

Occlusion is enabled in both view and place modes, in normal and stereo goggle views. **Environment depth is the default**, so any nearer real-world surface measured by LiDAR can hide virtual screens, their photo/video content and floating app menus. This includes hands, gloves, furniture, walls and other objects; it does not require recognizing a person or detecting a marker. Select **AR Visualizer App → Real-world occlusion**:

- **Prefer Environment Occlusion** (default): LiDAR depth for people and other objects.
- **Prefer Human Occlusion**: human mask and depth; other objects do not occlude screens when this source is used.
- **No Occlusion**: comparison mode; also disables device depth-based touch.
- **Smooth Environment Depth**: off by default to avoid trailing depth edges on moving obstacles. Enable it if flicker matters more than edge responsiveness. Fingertip measurement always uses raw, frame-matched scene depth.
- **Occlusion Edge Inset Pixels**: defaults to **1 LiDAR pixel**. On iPhone, slightly retracts foreground depth at abrupt edges to reduce an oversized occlusion silhouette. Set it to 0 to disable this correction; larger values can expose a thin strip of virtual content behind an object.
- **Occlusion Depth Bias Metres**: defaults to **0.005 m (5 mm)**. Moves the rendered occluder depth slightly away from the camera to avoid premature hiding near real surfaces. Set it to 0 for an exact measured-depth comparison.

The iPhone camera background uses `ARKitTightOcclusion.shader` with AR Foundation's usual camera textures, display transform, depth textures and occlusion keywords. Environment depth is sampled without interpolation across object edges; a small neighbourhood retracts only depth discontinuities. Missing environment depth is never filled by a neighbouring obstacle. Camera colour is unchanged. The corrected source depth also feeds both goggle eyes, so the same settings apply in both modes, before screens and floating app menus are rendered. Raw LiDAR data used for marker positioning and fingertip contact is unchanged. Editor simulation keeps its provider background; GPU tests exercise the iPhone shader with synthetic depth edges, missing depth and a known metric tolerance. Final edge alignment still needs comparison on the iPhone, especially for thin objects and motion.

ARKit chooses one rendering source according to preference and availability; these are not merged. A device without environment-depth support falls back to human occlusion where supported. The camera's depth is written before opaque screens and depth-tested floating app menus. HUD graphics remain overlays. Occlusion follows the depth the device can measure; a missing or inaccurate depth measurement can leave virtual content visible. [Unity's occlusion requirements](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.6/manual/arkit-occlusion.html), [Apple iPhone 14 Pro Max specifications](https://support.apple.com/en-us/111846).

## Build and verify

Install Unity's matching **iOS Build Support**. Use **Tools → AR Visualizer → Configure iPhone**, then select iOS in Build Profiles, confirm the ARVisualizer scene is first, and configure your bundle ID and signing team. ARKit and Initialize XR on Startup must be enabled. Export the Xcode project and build/sign it on a Mac. The build hook links ARKit/Photos/AVFoundation, includes the AprilTag C sources/header search path, enables ARC for the native camera bridge and Photos plugins, and adds the Photos permission description. iOS 15 or newer, camera permission and local-network permission are required. Photos access is requested when opening the library.

**Rebuild and reinstall the app after these changes**, including the native marker and Photos plugins. A previous Xcode export does not automatically include new source changes.

The marker adapter uses the portable AprilTag C API and the existing ARKit frame bridge. It builds with Unity's default disabled Objective-C exceptions; no Xcode exception-setting change is needed. The build verifies that the detector and tag25h9 family were exported.

Every iOS export adds **AR Visualizer Prepare Symbol Tools** as the first build phase of both **UnityFramework** and **Unity-iPhone**. On the Mac, it restores executable permission on the exported `process_symbols.sh`, `usymtool` and `usymtoolarm64` before Unity runs its symbol processing. This runs on each build, so transferring an export from Windows cannot leave those tools without executable permission. It handles paths containing spaces, skips tools absent from that Unity version, and preserves the existing symbol-processing phases. [Unity build-phase API](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityeditor/ios/xcode/pbxproject/insertshellscriptbuildphase).

Re-export from the updated Unity project to get these fixes; an older Xcode export is unchanged. The Windows export verifies Unity compilation and Xcode project generation. Compiling the Apple native plugins and confirming signing still requires Xcode on a Mac.

**Window → General → Test Runner → EditMode → ARVisualizer.Tests** covers command parsing, UDP dispatch/correlation/deduplication, prefab dimensions and events, front-face coordinates, wall orientation, size limits, native sample layout and the simulated AR flow. The integration test places an anchored screen, points at it, locks adjustment, grows/shrinks/rotates it, verifies fingertip priority and tracking loss, preserves authored HUD layout, and exercises undo. Goggle checks cover ray hover, actual UDP button clicks, duplicate click packets, the always-available HUD toggle, retained placement targets, pointer locking, mode restoration and shutdown. With graphics enabled, they compare rendered eye positions, near/far stereo disparity, depth occlusion against a depth-disabled control, and the lens compositor.

Screen app checks cover compact bottom controls, gallery scrolling in both modes, held ray and phone swipes, fingertip button activation and drag/lift, clipped-thumbnail exclusion, a 600-item list with bounded thumbnail memory, cancelled selections, permission-denied and empty-library states, photo display and resizing, modal click priority in stereo, actual H.264 video playback and pause/resume, and temporary-file cleanup. Editor tests use sample media and simulated library replies.

Phone-input checks send actual Input System touch events for both halves, holding/dragging/releasing, GUI exclusion, disabled/lost tracking, modal back, and switching modes in both directions using the fixed overlay button. Goggle checks also verify the two/three-button bottom row and restore the normal controls' authored geometry on exit. Test the same interactions on iPhone in both landscape orientations before use.

Move checks cover cancellation, relocation at the screen-count limit, media/video preservation and goggle confirmation. Two-hand checks cover both catalogue variants, malformed/conflicting selectors, native marker identity slots and independent expiry, simultaneous contacts, per-hand click/resize/move selection, concurrent anchor creation and reply ownership, and both goggle pointer locks.

On the iPhone, check both hands together and individually, crossing hands, occlusion/loss of one hand, per-hand menu presses, both landscape orientations, wall/table placement, root-marker targeting, fingertip contact/release near face edges, hand occlusion, depth loss, adjust commands, and background/resume. Test Photos with full, selected and denied access; portrait photos/videos, iCloud downloads, cancellation, and playing media on multiple screens. For the headset, calibrate lens centres/eye separation and distortion, check fusion at near and far distances, point at every HUD and gallery button, and exit/re-enter goggle mode. Windows Unity tests cannot validate the viewer's optics, compile Apple's native frameworks, access device Photos or establish real fingertip-contact accuracy.

Implementation references: [Apple index MCP joint](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/jointname/indexmcp), [hand chirality](https://developer.apple.com/documentation/vision/vnhumanhandposeobservation/chirality), [scene-depth reconstruction](https://developer.apple.com/documentation/arkit/displaying-a-point-cloud-using-scene-depth), and the installed ARKit package's **Includes~/UnityXRNativePtrs.h** for the native session bridge.

## Marker detector development

AprilTag **3.4.5** is vendored under `Plugins/iOS/AprilTag`, with its original source notices. Provenance and archive checksum are in [AprilTag/UPSTREAM.md](Plugins/iOS/AprilTag/UPSTREAM.md); binary notices are bundled as `Resources/ThirdPartyNotices.txt`.

Run `python Tools/test_marker_detector.py` with GCC/Clang available to compile the same C detector used on iOS and test actual marker images, rotations, missing/duplicate/unknown/damaged tags, quality gates, contact offsets and print-pattern compatibility. The generator `python Tools/generate_glove_markers.py` recreates the SVG/PNG assets directly from the pinned family definition. Add `--pdf` to generate the fixed-size A4 PDF under `output/pdf` (requires ReportLab). Measure both printed 50 mm check lines; if a line measures 45 mm, the print pipeline applied about 90% scale. Use the measured fingertip black-square width in Tip Width Metres; see [print scaling guidance](Markers/README.md). Unity Editor tests exercise routing, stale frames, LiDAR requirements, ray offsets, commands and the full AR simulation. Windows iOS export checks packaging; Xcode compilation and physical glove testing still require the Mac/iPhone.
