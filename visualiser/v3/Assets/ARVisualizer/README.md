# iPhone AR screen visualiser

Turn real tables and walls into interactive virtual screens. Point at a flat surface, create a screen, and run an app on it: photos and videos, a web browser, a PDF viewer, a timer, German flashcards or a sliding puzzle. Hold the iPhone in your hand (**phone mode**) or wear it in a simple headset (**goggle mode**).

- **Glove markers say where.** Printed AprilTags on each glove tell the phone where each hand is pointing.
- **Gestures say what.** A Mac recognises twelve hand gestures and sends their names to the phone over Wi-Fi.

| Start here | |
| --- | --- |
| Everyday gesture reference | [GESTURE_GUIDE.md](GESTURE_GUIDE.md) |
| Printing and fitting the glove markers | [Markers/README.md](Markers/README.md) |
| Gesture and command catalogue | [Resources/commands.json](Resources/commands.json) |
| Tutorial wording and steps | [Resources/tutorial.json](Resources/tutorial.json) |
| Design report and demo script | [visualiser_report.md](../../visualiser_report.md), [demo.md](../../demo.md) |

## Contents

1. [Quick start](#1-quick-start)
2. [Gestures](#2-gestures)
3. [Working with screens](#3-working-with-screens)
4. [Apps](#4-apps)
5. [Phone mode and the HUD](#5-phone-mode-and-the-hud)
6. [Goggle mode](#6-goggle-mode)
7. [Tutorial mode](#7-tutorial-mode)
8. [Glove markers and occlusion](#8-glove-markers-touch-and-occlusion)
9. [Network: Mac gestures and Wi-Fi commands](#9-network-mac-gestures-and-wi-fi-commands)
10. [Customising in Unity](#10-customising-in-unity)
11. [Build and test](#11-build-and-test)
12. [Script map](#12-script-map)

---

## 1. Quick start

**Requirements:** Unity **6000.6.0f1** with iOS Build Support, a Mac with Xcode, and an iPhone with LiDAR (developed on the **iPhone 14 Pro Max**, iOS 15 or newer). LiDAR depth is needed for fingertip touch and real-world occlusion.

1. Open **Assets/ARVisualizer/Scenes/ARVisualizer.unity**. The scene has a saved HUD and screen prefab; the AR rig is created at runtime.
2. Run **Tools → AR Visualizer → Configure iPhone**, select iOS in Build Profiles, keep the ARVisualizer scene first, and set your bundle ID and signing team.
3. Export the Xcode project, then build and sign it on the Mac (see [Build and test](#11-build-and-test)).
4. Print the [glove markers](../../output/pdf/Glove-markers-A4.pdf) and fit them as described in [Markers/README.md](Markers/README.md).
5. On the Mac, start the gesture service. On the phone, set the Mac's Tailscale IP (see [Network](#9-network-mac-gestures-and-wi-fi-commands)).
6. Launch the app and choose **Tutorial mode** (left square) to learn the gestures, or **Free mode** (right square) to start straight away.

**A first screen:** point at an empty table or wall until the ray turns green, then **grab and release**. A black screen appears and its app list opens. Point at an app and **click**.

Both landscape orientations are supported.

---

## 2. Gestures

Aim with a hand's ray, then perform the gesture **with that same hand**. Both hands can use every gesture. The full workflows are in [GESTURE_GUIDE.md](GESTURE_GUIDE.md).

| Gesture | Remember it as | What it does |
| --- | --- | --- |
| `click` | Select / use | Press the aimed button or highlighted menu item; otherwise the app's main action (start/pause a timer or video, flip a card, open a photo's library). Blank space does nothing. |
| `grab_and_release` | Place / confirm | Create a screen on a blank flat surface; confirm a move, edit, menu choice or a red (armed) delete. Does not operate apps. |
| `crumple_and_toss` | Cancel / remove | Cancel an edit or placement, go back from the library, or mark the pointed screen red for deletion. |
| `pinch_and_drag` | Move | Pick up the pointed screen; aim at a new surface, then grab to put it down. |
| `pinch_and_pull` | Resize | Blue glow; swipe up to enlarge, down to shrink. |
| `pinch_and_rotate` | Rotate | Pink glow; swipe left / right to turn by 5°. Screens lying on horizontal surfaces only. |
| `point_and_rotate` | Turn upright | Switch a browser or PDF screen between landscape and portrait. |
| `wiggle_fingers` | Settings | Open / close the pointed screen's settings: Choose app, Move, Resize, Rotate, Delete screen, Done. With a browser keyboard open, presses the pointed key instead. |
| `swipe_up` / `swipe_down` | More / less | Resize, change a timer digit, reveal / hide a flashcard answer, scroll a page or library. On a photo or video, up opens the library. |
| `swipe_left` / `swipe_right` | Previous / next | Previous / next item, timer look or menu choice; anticlockwise / clockwise while rotating. |

**Rules that apply everywhere**

- **0.5 s cooldown.** Gestures share a 0.5-second cooldown across both hands, including tutorial practice. Gestures arriving inside it are ignored and do not extend it. Wi-Fi commands are not affected. Change it with **AR Visualizer App → Gesture Cooldown Seconds**.
- **1 s target memory.** If the ray slips into empty space while you gesture, the gesture uses the last screen region or button that hand pointed at within the last second. A current valid target always wins. Hidden or disabled buttons, removed screens and replaced apps invalidate the memory.
- **Two-step delete.** Crumple marks a screen red; grab while still pointing at that same screen deletes it. Neither step uses target memory, and any other gesture clears the mark. The **Delete screen** button and the `screen.delete` command delete at once.
- **Menus first.** Open menus and edit modes take gestures before the app underneath. In a menu, swipes move a green highlight and click (or grab) accepts it; a highlighted choice wins over a drifting ray.
- **Edits keep their screen.** Move, resize and rotate keep the selected screen even if the ray leaves it. Cancelling restores the size and rotation from the start of the edit; switching between resize and rotate keeps that starting snapshot. A status label shows the current edit even when the HUD is hidden.

Gesture names are case-insensitive, and `swipt_left`, `swipt_right` and `wriggle_fingers` are accepted as aliases. The bindings and the guide captions live in the `gestures` and `appGestures` sections of [commands.json](Resources/commands.json); `appGestures` maps swipes to app actions while the ray is on an open app.

---

## 3. Working with screens

### Place a screen

- **By gesture:** point at an empty flat surface and **grab and release**. No place mode is needed.
- **From the phone HUD:** tap **Place Screen**, move the phone slowly to find surfaces (they are highlighted), aim, then tap the green **Place Here** (or send `place.enter`, then `screen.place`; `place.exit` cancels).

A new screen is a black flat rectangle anchored to the surface, and its app list opens straight away. Placement is rejected while the ray points at an existing screen.

- Screens lie flush against their surface, 1 mm in front of it. On walls and slopes they stay level with the horizon; on surfaces within 15° of horizontal they lie flat and can be rotated.
- Screens follow their AR anchors but are **not saved between app sessions**. The default limit is 100 screens.

### Size

New screens are sized by distance so they look about the same size wherever you place them. With the default reference of **16 × 9 cm at 0.5 m**, a screen placed 1 m away is 32 × 18 cm and one placed 2 m away is 64 × 36 cm. After placement its real size is fixed; walking closer or moving it does not resize it.

| Setting (AR Visualizer App → Initial screen size) | Default |
| --- | --- |
| Size New Screens By Distance | On (off uses the prefab size everywhere) |
| Screen Size Reference Distance | 0.5 m, for the prefab's 10-step (16 cm) size |
| Minimum / Maximum Initial Size Steps | 5–100 (8–160 cm wide) |

Each resize step changes the width by **1.6 cm** (1–100 steps, so 1.6–160 cm wide). Height follows the current media's shape, or 16:9 (0.9 cm per step) for a blank screen. Loading media keeps the width, centre and orientation; a 3:4 portrait photo on a 16 cm wide screen makes it about 21.3 cm tall. Goggles measure distance from the eye midpoint, phone mode from the camera.

### Move, resize, rotate and delete

| To… | By gesture | From the phone HUD | Command |
| --- | --- | --- | --- |
| Move | Pinch and drag, aim, grab (crumple cancels) | Screen Settings → **Move Screen**, aim, **Move Here** / **Cancel Move** | `move.enter`, `move.confirm`, `move.cancel` |
| Resize | Pinch and pull, swipe up / down, grab (crumple restores) | Screen Settings → Smaller / Larger | `adjust.enter`, `adjust.grow`, `adjust.shrink`, `adjust.exit` |
| Rotate (flat screens) | Pinch and rotate, swipe left / right, grab (crumple restores) | Screen Settings → Rotate Left / Right | `adjust.rotate.cw`, `adjust.rotate.ccw` |
| Turn portrait (browser / PDF) | Point and rotate | — | `screen.orientation`, `screen.vertical`, `screen.horizontal` |
| Delete | Crumple, then grab at the same red screen | Screen Settings → **Delete Screen** | `screen.delete` |

- **Moving** keeps the screen's ID, size, media and playback, and replaces only its anchor (so it works at the screen limit). Rotation is kept between horizontal surfaces; a wall or slope makes the screen level. The screen stays put until the move is confirmed. Tracking loss blocks confirmation; switching display mode, backgrounding the app or deleting the screen cancels the move, and undo during a move cancels it rather than deleting.
- **Adjust mode** keeps the same screen selected if the ray moves away or tracking is lost. Entering place mode, undoing or clearing that screen exits it.
- **Rotation** is 5° per step (**AR Visualizer App → Screen interaction**) and is refused for wall and sloped screens.
- **Edit glow:** pink while rotating, blue while resizing, red when marked for deletion. It follows the screen's shape and is hidden behind nearer real objects. Edit colours, opacity and width on **Screen Edit Aura** in `Screen.prefab`; **Editor Preview** shows it without Play mode.

### Wall depth tolerance

Screen faces and their UI allow **2 cm of rendering depth tolerance** (material property **Mounting surface depth tolerance (metres)**), so small differences between the fitted plane and a rough wall's measured depth do not hide the screen as you approach. It only affects drawing, not position or input. Clearly nearer objects still hide the screen, but real things inside that 2 cm band cannot be told apart from the wall. Keep the same value on the face and app UI materials.

---

## 4. Apps

### Choosing and closing apps

Pointing at a blank screen opens its **app list** automatically (phone and goggle modes). For a screen already running an app: **wiggle fingers → Choose app → click**, send `app.choose`, or press **F** in the keyboard controller. The list and the floating **Screen Options** panel always match the screen's width.

- Each screen runs its own app; looking away or hiding the HUD does not close it. Choosing an app replaces the current one (choosing Photos keeps it until an item is actually picked).
- **Choose app** / **Close app** controls sit just below the screen (4–24 cm wide by default: **Screen App Menu → Launcher Width Fraction / Minimum / Maximum Width**). Close app restores a blank 16:9 screen at the chosen width.
- After placement the list stays open until dismissed, an app is chosen or another screen is targeted. Collapsing it keeps it closed for the current visit.
- **Goggles:** hold a tracked ray on any app-list or screen-settings button for **3 seconds** to press it without a gesture. A bar inside the button fills; it presses once per visit. Moving away, losing tracking or leaving goggles resets it, and the centre fallback cannot start a hold. Change the time and colour under **Screen App Menu → Goggle mode: point and hold**.

### Photos and videos

The library is a scrolling list of your iPhone photos and videos, newest first. Click a thumbnail to show it on the screen.

| Action | How |
| --- | --- |
| Scroll | Swipe up / down; hold left click and move the ray; drag with a fingertip; or drag on the phone |
| Select | Click a thumbnail, or swipe left / right to highlight one and click |
| Previous / next item on the screen | Swipe left / right while pointing at the screen (`media.previous`, `media.next`) |
| Play / pause a video | Click the screen, or **Play / Pause** below it |
| Choose another item | Swipe up or click on a photo, or **Choose media** below the screen |
| Close the library | Crumple, right-click or **Close** |

- The first launch of Photos asks for iOS permission. That prompt is system UI and must be tapped on the phone. With **selected photos** access only those items appear; change the selection in iPhone Settings and press **Refresh**. Denied access and an empty library show a message.
- Photos load upright at up to 2048 px. Videos are exported to a local 720p MP4 (iCloud items may take a while) and loop by default, pausing when the app is in the background. The original library is never changed.
- The screen takes the item's own shape (portrait, square, wide), with no borders or cropping, including videos with non-square pixels.
- The library floats just above the screen and keeps a horizontal visual angle of about **38°** as you move (physical width 8 cm–4 m). Change this under **Screen App Menu → Placement**; turn off **Auto Size Popup** to use a fixed **Popup Width**.
- It uses twelve recycled tiles and keeps at most 48 decoded thumbnails. An abandoned remote press is released after 15 seconds.
- In the Editor, eight labelled generated photos stand in for the library.

### Timer

A **MM:SS** countdown, starting at 01:00, with digits filling the screen and no buttons.

- **Set:** point at a digit and swipe up or down. The highlighted digit is the one that changes, and gaps belong to the nearest digit. Steps are **10 min, 1 min, 10 s, 1 s**; values carry and clamp to 00:00–99:59. Touching a digit also adds its step. Editing pauses the timer and becomes its reset value.
- **Start, pause, resume:** click. Click a finished timer to reset it. `app.reset` stops and resets at any time.
- **Look:** swipe left / right. Each look gives the screen its own shape; the countdown keeps running and the screen remembers its look.

| Look | Shape | Layout |
| --- | --- | --- |
| Blocks | 16:9, rounded corners | Four digit tiles, progress bar along the bottom |
| Orbit | Circle | Time inside a ring that empties clockwise |
| Capsule | 3.2:1 pill | Small progress ring in the left cap, time along the bar |
| Tower | 3:4 portrait | Minutes above seconds, split by the progress bar |

The accent colour is the look's own while ready or running, amber when paused and pulsing coral at **Time's up!** There is no background alarm or notification.

### German flashcards

32 beginner German–English cards, shown one at a time. **Swipe up / down** shows / hides the English, **swipe right / left** goes to the next / previous card (the deck wraps), and **click** or a fingertip touch flips the card. Colours follow the noun's article: **der** blue, **die** rose, **das** green, amber for other words. Edit [german_vocabulary.json](Resources/german_vocabulary.json) (`german` and `english` fields) to change the cards.

### Sliding puzzle

Numbered tiles on a **3 × 3, 4 × 4 or 5 × 5** board, with a move counter and a clock that runs only while playing.

| State | Click | Swipe up / down | Swipe left / right |
| --- | --- | --- | --- |
| Playing | Pause | Slide the tile next to the gap up / down | Slide a tile left / right |
| Ready or paused | Start / resume | Larger / smaller board | Left: new game |
| Solved | Start a new game | Larger / smaller board | Left: new game |

Tiles in their final place take the board's accent (teal, violet or amber by size). Every deal is solvable, and reopening the app deals a new game at that screen's last size.

### Sound effects

The timer, flashcards and puzzle answer each action with a short sound, played from their own screen.

| App | Sounds |
| --- | --- |
| Timer | Step up / down per digit change; rising chime on start, falling on pause; soft tone on reset; swish on a look change; a tick on each of the last three seconds; three beeps at zero, repeated up to five times until reset |
| Flashcards | Rising blip on reveal, falling on hide; swish for next card, darker swish for previous |
| Puzzle | Wooden tap per slide; rattle for a new deal; step up / down for a board-size change; start / pause chimes; fanfare when solved |

A dull knock marks an action that cannot happen. **AR Visualizer App → Sound → App Sound Volume** sets the loudness (0 is off; video sound is separate). The iPhone's **silent switch mutes these sounds**. They are synthesised in `AppSounds.cs`, so there are no audio files to import.

### Web browser

Each screen has its own page and history.

| Do this | How |
| --- | --- |
| Follow a link, press a toolbar button or key | Click |
| Scroll | Swipe up (down the page) / swipe down (up the page) |
| Back / forward | Swipe left / right, or the toolbar |
| Type an address or search | Click the address bar, type on the AR keyboard, then Enter |
| Turn portrait / landscape | Point and rotate |

- The toolbar has Back, Next, Reload, Home, the address bar and **Keys** (opens the keyboard for fields the browser cannot detect).
- The **AR keyboard** pops out below the screen at **1.5 times its width**, with large captions and visible Shift. The ray and fingertip reach it through the screen. Set its width, gap and caption sizes on **Screen Browser View → Pop-up keyboard**. Close it before opening the screen settings.
- **Portrait mode** turns the screen about its centre, keeping its long side, and lays the page out again. Doing it again restores the exact earlier size. Opening another page or PDF keeps it portrait; choosing a different app returns the screen to landscape.
- **Web video** on iOS uses Vuplex's fallback video path: up to **360p** on YouTube, with no FairPlay DRM or `blob:`/MediaSource support ([details](https://support.vuplex.com/articles/fallback-video/)).
- Pop-up windows open in the current page, so sign-in flows that need a separate window are not supported. Native pickers may need the phone's touch screen. Cookies and cache are shared between screens.
- The Windows Editor shows a mock page; live browsing needs an iPhone build.

### PDF viewer

1. Install the app and launch it once.
2. In **Files → Browse → On My iPhone → Vis-Test1 → PDFs**, copy in your `.pdf` files. The folder name follows the app's Product Name, and files must be directly inside `PDFs`.
3. Choose **PDF viewer**, then click a document in the explorer. **Refresh** picks up changes, and returning from Files refreshes it too.

Scroll with swipe up / down and click PDF controls with the ray or a fingertip. **Keys** gives the AR keyboard for search and password fields, and **Files** (or **Choose PDF** below the screen) reopens the explorer. Left / right swipes move through web history, not PDF pages. Files are kept when a screen is cleared or deleted. Check multipage rendering on the iPhone, because the Editor uses a mock.

---

## 5. Phone mode and the HUD

Hold the phone sideways with the rear camera facing your workspace.

| Control | Where | What it does |
| --- | --- | --- |
| **HUD − / HUD +** | Top left | Hide or show the panels, controls and aim marker. Tracking, rays, screens and Wi-Fi keep working. |
| **Mode switch** (goggles / phone icon) | Top centre | Switch between phone and goggle mode. Always touchable, even with the HUD hidden. |
| **Left Click / Right Click / Screen Settings** | Bottom right, top row | Click the ray's target; open the pointed screen's settings panel (Choose app, Smaller, Larger, Rotate Left / Right, Move Screen, Delete Screen, Done). |
| **Pointer / Place Screen / Gestures** | Bottom right, second row | Ray on or off; place mode (turns into green **Place Here**); the gesture test panel. |
| **Wi-Fi Gestures** | Left | The six most recent gestures received from the Mac, with time and hand. |
| Gesture guide strip | Right edge (HUD hidden) | Point a tracked hand at it to show a gesture guide for the current situation. |
| Tutorial bar | Left edge (free mode) | Point at it for 3 s, or tap it, to reopen the tutorial. |

**Tapping the display as a click.** With the pointer on, touching an empty part of the **left half** of the display left-clicks the ray's target, and the **right half** right-clicks it (the exact centre counts as right). A left press on a menu is held until release, so you can swipe on the phone to scroll a library. Visible HUD panels keep their normal touch behaviour. Split clicks are off in goggle mode, with the pointer off, or without AR tracking. Right clicks close the library or app list (**Screen App Menu → Right Click Goes Back**).

**Gesture test panel.** Choose **Left Hand** or **Right Hand**, aim that hand's ray and tap any of the twelve gesture buttons. They use the same mappings as the Mac but send no network packet and do not appear in the history.

**Gesture history.** Keeps up to 32 gestures per session (six shown) and records them even when their action could not run. It ignores empty or invalid packets and direct commands, and is hidden in goggle mode.

---

## 6. Goggle mode

Mount the phone sideways in a Cardboard-style headset, leaving the **rear camera and LiDAR uncovered**. Tap the top-centre mode switch or send `goggle.enter` (`goggle.exit` to return).

- **Two eye views.** Screens, rays and the HUD are drawn from each eye position. The room comes from **one camera** and is shifted for each eye using depth, so edges, newly uncovered areas and places without depth are approximate (missing depth uses a 4 m video distance). Camera and eye images are **4:3**, fitted into each half of the display with black margins.
- **Floating HUD.** It sits 0.8 m in front of you, up to 0.9 m wide (1280 × 960 layout), and is used with the ray. Text is 2 points larger than in phone mode (**Goggle HUD → Font Size Increase**). **Fit Within Eye Views** narrows it so both eyes see every control. Entering free mode from the tutorial hides the HUD; the toggle shows it again.
- **Pointers.** Both rays are always on (`pointer.off` is rejected). Ray ends show a small white cross (**AR Visualizer App → Ray Tip Size**). Without a tracked hand, a centre pointer with only a round tip targets what is straight ahead.
- **Buttons.** The bottom row shows **Enter Place** and **Undo**, or **Exit Place**, **Place Screen** and **Undo** in place mode (**HUD → Controls → Goggle Button Spacing**). Click a button with `click` or `ui.click`; app-list and settings buttons also accept a 3-second point-and-hold. To place with the floating button, aim at a surface first: the last valid surface point is kept while you aim at the button.
- **Exiting** restores each pointer's previous switch, the phone controls and their layout, and normal touch input.

### Fitting the headset

Edit **Landscape HUD → Stereo Goggles**. These are adjustable defaults for an unbranded viewer, not a calibrated profile.

| Setting | Default | Notes |
| --- | --- | --- |
| Eye separation | 0.064 m | Match the user and the viewer's lens spacing |
| Match Camera Field Of View | On | Turn off only for a calibrated projection; then **Vertical Field Of View** (70°) applies |
| Left Lens Centre | — | Align the image with the left lens; the right one is mirrored |
| Distortion | — | Radial lens correction; set both to zero to compare |
| Eye Centre Offset | — | Eye midpoint relative to the rear camera |
| Render Scale | 0.75 | Limits the cost of drawing both eyes |

Tune until a distant object fuses into one image, then check a nearby screen. Panel distance, width and height are under **Landscape HUD → Goggle HUD → Floating panel**.

---

## 7. Tutorial mode

The app starts with two large squares: **Tutorial mode** (left) and **Free mode** (right). Tap one; in goggles, point at it and click. Nothing acts behind this choice.

1. **Practise.** A looping clip in the top-left corner shows the gesture, and the panel at the lower centre explains it. Each correct gesture fills a third of the small bar under that hand's marker (green pop-up). A different gesture gets an amber pop-up naming it.
2. **What it does.** After three correct gestures by one hand, the panel explains the gesture's use. Doing it once more moves on.
3. **Guided tasks.** After all twelve gestures, five short guides use them on real screens. Each step shows the gesture's clip and ends when the visualiser shows the result.

   | Guide | Steps |
   | --- | --- |
   | Place and delete a screen | Grab on an empty surface; crumple to mark the screen red, then grab to delete |
   | Resize a screen | Pinch and pull; swipe up / down; grab to keep |
   | Rotate a screen on a table or floor | Pinch and rotate; swipe left / right; grab to keep |
   | Open the screen settings | Wiggle fingers; wiggle again to close |
   | Turn a screen vertical and back | Point and rotate at a browser screen, twice |

   A guide asks for a screen first if none suits, opens the browser by itself for the last guide, and goes back a step if an edit is cancelled. A finished step shows **DONE**.
4. **Finish.** Swipe left: the tutorial slides out to the left and the free-mode HUD comes in from the right. Screens made in the guides stay.

- Practice gestures never reach the visualiser; in the guided tasks gestures act for real.
- **Skip Gesture** (or **Skip Guide**) and **Exit Tutorial** are always available at the bottom right, for example when the gesture receiver is offline (the panel then shows a warning). In goggles, resting a ray on either button for 3 s presses it; a tip above them says so.
- All wording, the gesture order, the number of repetitions, the guides and the hold times are in [tutorial.json](Resources/tutorial.json). Placeholders such as `{step}`, `{total}`, `{required}` and `{seconds}` are filled in at runtime. Each guide step has a `goal` that ends it; `"skipIfMet": true` marks a preparation step.
- Turn off **Tutorial HUD → Start With Mode Choice** to start in free mode. **Tools → AR Visualizer → Prepare tutorial mode** adds the layout to the HUD and re-links the clips in `Media/Tutorial`.

---

## 8. Glove markers and occlusion

### Markers and pointing

The rear camera tracks **four AprilTag `tag25h9` markers**: left root / tip IDs **0 / 1**, right root / tip **2 / 3**. The root marker sits at the base of the index finger and the tip marker on the fingertip. Identity comes from the ID, so crossed hands stay correct. There is no bare-hand fallback. Printing, placement and calibration are in [Markers/README.md](Markers/README.md) ([A4 PDF](../../output/pdf/Glove-markers-A4.pdf), [SVG](Markers/Glove-markers-A4.svg)).

- **Ray.** Each ray aims from the camera through a point **3 cm beside the root marker** (to its left for the right hand, to its right for the left hand, as seen by the phone). It does not estimate the finger's direction. Set **Hand Pointer Source → Ray offset → Lateral Offset Metres** (0.03) and **Fallback Root Depth Metres** (0.5 m, used without depth).
- **Detection** runs on the AR frame's existing image in a background worker at **15 samples/s** with two threads. No extra camera session, model or network service is needed. Configure the IDs under **Hand Pointer Source → iPhone glove markers → Markers** (the component is listed as **Glove Marker Tracking**).
- **Robustness.** Only the configured IDs are accepted; duplicates of an ID are ignored for that frame. One damaged bit can be corrected. A new marker needs a decode margin of **30** (**Minimum Margin**); a track seen in the last 0.5 s continues at 20. Corrected tags and sudden jumps need two consistent sightings within 0.2 s. Keep tags flat on a matte backing.
- **Short dropouts.** **Tracking Grace Seconds** (0.5 s) keeps a hand's ray at its last on-screen position after the marker is lost; 0 disables it. **Maximum Sample Age** (0.2 s) rejects old frames. Touch always needs a fresh sample. An AR reset, a viewport change or pausing the app clears the hold.
- **Centre pointer.** When neither hand is tracked, one centre pointer serves the selected controls hand, in both modes.
- `hand.calibrate` only checks that the selected root marker is visible; it never changes IDs.

### Real-world occlusion

Nearer real objects measured by LiDAR (hands, furniture, walls) hide screens, their content and their menus, in both modes. HUD graphics stay on top. Choose the source under **AR Visualizer App → Real-world occlusion**:

| Setting | Default | Effect |
| --- | --- | --- |
| Prefer Environment Occlusion | ✓ | LiDAR depth for people and objects (goggles always prefer it) |
| Prefer Human Occlusion | | Only people hide screens |
| No Occlusion | | Comparison mode; also disables fingertip touch |
| Smooth Environment Depth | Off | Less flicker but trailing edges on moving objects |
| Occlusion Edge Inset Pixels | 1 | Pulls foreground edges in slightly on iPhone; 0 disables |
| Occlusion Depth Bias Metres | 0.005 | Avoids hiding content too early near real surfaces |

The iPhone background uses `ARKitTightOcclusion.shader`, which avoids blending depth across object edges and never fills missing depth from a neighbouring obstacle. The same corrected depth feeds both goggle eyes. Edge quality still needs checking on the phone, especially for thin and moving objects ([Unity occlusion requirements](https://docs.unity3d.com/Packages/com.unity.xr.arkit@6.6/manual/arkit-occlusion.html)).

---

## 9. Network: Mac gestures and Wi-Fi commands

### Mac gesture connection

The scene's **GestureReceiver** connects to the Mac's gesture WebSocket server over Tailscale. Set **Mac Tailscale IP** and **Mac Port** (default **8765**) on **Gesture Web Socket Receiver**. Packets look like this:

```json
{"type":"GESTURE","gesture":"click","hand":"left"}
```

`hand` defaults to `right` if missing. The HUD status row shows **MAC Connected** (green), **Connecting** or **Paused** (amber), or **Disconnected / Connection error / Disabled** (red). It follows the WebSocket's own events; there is no separate heartbeat. Pausing or disabling the receiver drops the connection, and resuming reconnects. Errors are in `GestureWebSocketReceiver.LastError` and the Console. Colours and the receiver reference are under **Landscape HUD → Mac connection status**.

### Wi-Fi (UDP) commands for debugging

Send one UTF-8 command per UDP datagram to the address shown in the HUD, **port 7777**. Plain text or JSON is accepted:

```json
{"id":"request-001","command":"left.adjust.grow","token":""}
```

- **Hands.** Prefix a command with `left.` or `right.`, or add `"hand":"left"`. Unprefixed commands use the right hand. `left.ui.rightclick` is a right-click from the left hand. Shared effects (`goggle.*`, `screens.clear`) apply to both hands.
- **Catalogue.** [commands.json](Resources/commands.json) lists every command with its description, scope, left / right packets and aliases. Unity and both Python senders read it; add a runtime handler when you add a command.
- **Replies** go back to the sender after the command finishes. They include `ok`, `message`, the issuing `hand`, mode flags, `screenCount`, `pointedScreenId`, `interaction` (`kind` none / ray / touch, `u`, `v`, metres), `adjustedScreen`, menu and goggle state, and `leftHand` / `rightHand` snapshots. Screen ID 0 means no target.
- **Reliability.** Use a new request ID for every command (including each `status` poll) and reuse an ID only to retry. The phone remembers the last 128 replies and ignores duplicates in progress; plain commands have no de-duplication. Wait for each reply, because UDP is unordered.
- **Limits.** Packets up to 4 KB, a 64-packet queue and eight per frame, handled on Unity's main thread. An optional **Shared Token** (`--token` on the sender) is checked, but the transport is unencrypted: use it only on a trusted network. Allow iOS **Local Network** access when asked.

| Common commands | Effect |
| --- | --- |
| `status` | Modes, counts, selected screen and ray / touch coordinates |
| `pointer.on` / `pointer.off` | Ray on / off (touch keeps working) |
| `place.enter`, `screen.place`, `place.exit` | Place mode, place a screen, cancel |
| `screen.undo` / `screens.clear` | Remove the newest screen (or cancel a pending placement) / remove all |
| `adjust.enter` / `.grow` / `.shrink` / `.rotate.cw` / `.rotate.ccw` / `adjust.exit` | Lock the pointed screen and edit it |
| `move.enter` / `move.confirm` / `move.cancel` | Move the pointed or locked screen |
| `ui.click`, `ui.rightclick`, `ui.press` / `ui.release` | Click the ray's target; hold to scroll a library |
| `app.choose` | Open the app list on the pointed screen |
| `app.increase` / `.decrease` / `.previous` / `.next` / `.reveal` / `.hide` / `.startpause` / `.reset` | App actions on the pointed screen |
| `media.next` / `media.previous` | Next / previous photo or video |
| `browser.*` | Browser and PDF actions |
| `goggle.enter` / `goggle.exit` | Switch display mode |

The old `cube.place`, `cube.undo` and `cubes.clear` still work as aliases, and replies keep a `cubeCount` field.

**Senders** (Python 3, from the project root):

```sh
python Tools/send_command.py 192.168.1.42 status
python Tools/send_command.py 192.168.1.42 move.enter --hand right
python Tools/keyboard_command.py 192.168.1.42 --hand left
```

| Keyboard controller key | Action |
| --- | --- |
| Tab | Switch hands |
| I | Pointer on / off |
| = / P | Place mode / place or confirm a move |
| F | Choose app |
| U | Tap to click; hold and move the ray to scroll |
| A, arrows, Enter, X | Adjust, resize / rotate, confirm, cancel |
| M | Move |
| H | Check the selected root marker |

---

## 10. Customising in Unity

Edit prefabs with Play mode stopped; edits made during Play are lost. Setup tools add new parts once and keep your later layout changes.

| To change… | Edit |
| --- | --- |
| Phone HUD panels, buttons and crosshair | **Landscape HUD** (Tools → AR Visualizer → Open HUD prefab) |
| Mode switch button | `DisplayModeButton.prefab`; its position is in `PhoneControls.prefab` → Safe Area |
| HUD toggle button | `HUDToggleButton.prefab` (Safe Area → HUD Toggle Button) |
| Screen body, input and per-screen app settings | `Prefabs/Screen.prefab` (Open screen prefab) |
| App list, settings panel, photo / PDF explorer | `Resources/ScreenAppMenu.prefab` (Open screen apps prefab) |
| Timer looks, flashcard and puzzle layouts | `Resources/ScreenAppView.prefab` |
| Browser toolbar, page area and keyboard | `Resources/ScreenBrowserView.prefab` |
| Tutorial layout | Landscape HUD → **Tutorial**, **Tutorial Left / Right Hand** |
| Goggle calibration | Landscape HUD → **Stereo Goggles** |

**Landscape HUD layout.** Use a landscape Game view such as 1280 × 720 (the Canvas Scaler reference). **Safe Area** fits the iPhone's margins, so edit the panels inside it.

| Part | Object |
| --- | --- |
| Header / status | Safe Area → Top Bar |
| Placement information | Safe Area → Placement Panel |
| Buttons | Safe Area → Controls, Normal Actions (Screen Settings Panel inside) |
| Gesture test panel | Safe Area → Gesture Controls → Gesture Panel |
| Gesture history | Safe Area → Gesture History (rows listed newest first in **Gesture History HUD → Rows**) |
| Gesture guide | Safe Area → Gesture Guide (**Close Delay** 1.2 s) |
| Crosshair | Aim Position → Aim Marker |

Buttons use saved **On Click ()** events, and **Hideable Content** lists what the HUD toggle hides (keep the toggle and the gesture guide out of it). Extra buttons can call `EnterAdjustMode`, `GrowScreen`, `RotateScreenClockwise`, `ClearScreens`, `EnterGoggleMode` and the other public methods on **Visualizer HUD**. The mode switch's canvas must stay above the stereo compositor (sorting orders 30010 and 30000).

**Apps.** **Screen App View → Timer looks** lists each look's root, aspect, corner radius, accent, digits (in place-value order) and progress ring or bar; add an entry to add a look. Shapes use the sprite-free **Rounded Rect Graphic** and **Ring Graphic** components. **App registry** on the screen-app menu lists the app list; `photos`, `timer`, `flashcards`, `puzzle`, `browser` and `pdf` are built in, and other entries can call their **Launch** event with the screen. Browser options (home URL, texture width 1280, 15 fps, scroll step) are on **Screen Browser** in `Screen.prefab`.

**iOS look.** Panels use a 9-sliced squircle sprite (`UI/Squircle.png`; the Image's **Pixels Per Unit Multiplier** sets the corner size, 48 ÷ multiplier). **HUD Typeface** applies the system font at runtime. For SF Pro everywhere, import it from [developer.apple.com/fonts](https://developer.apple.com/fonts/) and run **Tools → AR Visualizer → Apply iOS HUD style**. Keep the font files out of a public repository.

**Screen API.** `ScreenSurface` uses (u, v) from **(0, 0) at the bottom left to (1, 1) at the top right**, seen from the front. Local X is width, Y is height and −Z faces the viewer. A fingertip contact beats a ray, and the nearest screen wins. It exposes `Id`, `InputKind`, `InputUV`, `InputMetres`, `CanRotate` and `IsAdjusting`; the per-hand `LeftInput`, `RightInput`, `HandInputChanged` and `HandPointerClicked`; and the Inspector events **Ray Pointed / Exited**, **Touch Started / Moved / Ended** and **Left / Right Clicked**.

```csharp
screen.InputChanged += (surface, kind, uv) =>
{
    // kind is None, Ray or Touch; uv is normalised to the current screen size.
};
```

Other apps can use `ScreenMedia.ShowPhoto`, `ShowVideo`, `TogglePlayback`, `Clear` and `SetFaceShape(aspect, cornerRadius)`. `ARVisualizerApp.UseHand` is a synchronous scope: never keep it across an `await`.

**Setup tools** (Tools → AR Visualizer): Configure iPhone, Create or open scene, Add editable HUD to current scene, the **Open … prefab** shortcuts, and the one-time **Prepare …** upgrades (normal controls and flat screens, scrolling media controls, timer and flashcard apps, browser app, gesture controls and guide, compact debug HUD, tutorial mode), plus **Apply iOS HUD style** and **Add gesture history to HUD prefab**. The Editor also runs the needed upgrades by itself after loading newer scripts.

---

## 11. Build and test

### Build for iPhone

1. Install Unity's **iOS Build Support** and run **Tools → AR Visualizer → Configure iPhone**.
2. In Build Profiles choose iOS, keep the ARVisualizer scene first, set your bundle ID and signing team, and make sure **ARKit** and **Initialize XR on Startup** are on.
3. Export the Xcode project, then build and sign it on a Mac. **Re-export after every change**; an old export does not pick up new native code.

The build hook links ARKit, Photos and AVFoundation, adds the AprilTag sources, enables ARC for the native plugins, adds the Photos permission text and turns on Files sharing for the PDFs folder. It also adds an **AR Visualizer Prepare Symbol Tools** build phase that restores the execute permission on Unity's symbol tools, so exports copied from Windows still build. The app needs camera and Local Network permission; Photos permission is asked only when the library opens.

### Tests

Run **Window → General → Test Runner → EditMode → ARVisualizer.Tests**. The suite covers command parsing and UDP handling, screen geometry and input, placement, moving and editing, both hands, phone touches, goggle mode and stereo rendering, occlusion, the apps, browser and PDF flows, the tutorial and the HUD prefabs.

Editor tests cannot check headset optics, Apple's native frameworks, the Photos library or real LiDAR touch accuracy. **On the iPhone**, check:

- both hands, singly and together, crossed and partly hidden, in both landscape orientations;
- wall and table placement, root-marker aiming, and fingertip touch near screen edges;
- occlusion by hands and objects, and loss of depth;
- Photos with full, selected and denied access, portrait media, iCloud items and several playing screens;
- web pages, keyboard entry, web video and multipage PDFs;
- the headset: lens centres, eye separation and distortion, comfort near and far, every HUD and library button, and leaving and re-entering goggles;
- backgrounding and resuming the app.

### Marker detector

AprilTag **3.4.5** is included under `Plugins/iOS/AprilTag` with its notices ([UPSTREAM.md](Plugins/iOS/AprilTag/UPSTREAM.md)).

- `python Tools/test_marker_detector.py` (needs GCC or Clang) compiles the same C detector and tests real marker images, rotations, damaged and unknown tags, quality gates and contact offsets.
- `python Tools/generate_glove_markers.py` recreates the marker SVG / PNG files. Add `--pdf` for the A4 PDF (needs ReportLab).
- Measure the printed 50 mm check lines. If one measures 45 mm the printer scaled to about 90%; enter the measured fingertip square width in **Tip Width Metres** ([print scaling](Markers/README.md)).

---

## 12. Script map

All runtime scripts are in **Assets/ARVisualizer/Scripts**.

| Behaviour | Script |
| --- | --- |
| App lifecycle, modes, ray / touch priority, placement | `ARVisualizerApp.cs` |
| Gesture meaning, edits, orientation, deletion | `ARVisualizerApp.Gestures.cs` |
| Command routing, cooldown and replies | `ARVisualizerApp.Commands.cs`, `CommandProtocol.cs`, `Resources/commands.json` |
| Moving screens | `ARVisualizerApp.Moving.cs` |
| Per-hand state | `ARVisualizerApp.Hands.cs`, `HandPointer.cs` |
| Marker tracking | `HandPointerSource.cs`, `GloveMarkerSettings.cs` |
| AR session, camera and pose setup | `ARSceneRig.cs` |
| Network | `GestureWebSocketReceiver.cs`, `CommunicationManager.cs` |
| One screen: size, input, events | `ScreenSurface.cs`, `ScreenEditAura.cs` |
| App list, settings, explorers | `ScreenAppMenu.cs`, `MediaLibraryGrid.cs`, `ScreenMenuInput.cs` |
| Photos, videos and PDFs | `PhotoLibrary.cs`, `ScreenMedia.cs`, `PdfLibrary.cs` |
| Timer, flashcards, puzzle and sounds | `ScreenAppContent.cs`, `ScreenAppView.cs`, `CountdownTimer.cs`, `SlidingPuzzle.cs`, `AppSounds.cs` |
| Browser and PDF viewer | `ScreenBrowser.cs`, `ScreenBrowserView.cs` |
| HUD, goggles and phone input | `VisualizerHUD.cs`, `GoggleHUD.cs`, `StereoGoggles.cs`, `NormalScreenControls.cs`, `PhonePointerClicks.cs` |
| Tutorial | `TutorialHUD.cs`, `TutorialSession.cs`, `TutorialScript.cs` |

Keep the update order in `RefreshInteractionTargets` (menus first, then screen faces, then placement), so a menu click can never reach a screen behind it. Asynchronous placement and library requests use generation counters to drop cancelled results, and media textures and temporary files have a single owner that releases them. Run the EditMode suite after changing these flows.
