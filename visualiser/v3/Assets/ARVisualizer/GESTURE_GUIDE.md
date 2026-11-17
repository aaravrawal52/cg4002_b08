# Visualiser gesture guide

**Point to use, pinch to edit, grab to place, crumple to cancel.**

Each hand remembers its last screen region or button for **1 second** if the ray slips into empty space while gesturing. A current valid target takes priority. Deletion never uses this memory: **crumple marks a screen red; point at that same screen and grab to delete it**. Another gesture cancels the red mark. Keep at least 0.5 seconds between gestures.

Aim with either hand's ray, then perform the gesture with that same hand. Both hands use the same controls. Enable the pointer in phone mode; it is always enabled in goggle mode. Keep your hand's tracking markers visible to the phone.

New to the gestures? Choose **Tutorial mode** when the app starts, or point at the amber bar on the left edge for three seconds in free mode. The tutorial shows a clip of each gesture and lets you practise it safely.

## The 12 gestures

| Gesture | Remember it as | What it does |
| --- | --- | --- |
| `click` | **Select / use** | Activate a menu choice or aimed button. On an app, start/pause a timer or video, flip a flashcard, or open a photo's library. |
| `grab_and_release` | **Place / confirm** | Create a screen on a blank flat surface. During a move or edit, confirm the result. Also accepts a menu selection. |
| `crumple_and_toss` | **Cancel / mark for removal** | Cancel an edit or placement, go back in the library, or mark a pointed screen red. Point at that red screen and grab-and-release to confirm deletion. |
| `pinch_and_drag` | **Move** | Pick up the pointed screen for moving. Aim at a new flat surface, then grab-and-release to place it. |
| `pinch_and_pull` | **Resize** | Select the screen for resizing. Swipe up to enlarge or down to shrink. |
| `pinch_and_rotate` | **Rotate the screen** | Select the screen for rotation. Swipe left for anticlockwise or right for clockwise. |
| `point_and_rotate` | **Turn upright** | Turn the pointed web browser or PDF screen vertical, or back to horizontal. The page lays itself out again. |
| `wiggle_fingers` | **Screen settings** | Open/close the pointed screen's settings: Choose app, Move, Resize, Rotate, Delete screen, Done. |
| `swipe_up` | **More / reveal** | Increase a size or timer digit, reveal an answer, or scroll forward through the library. On displayed media, open the library. |
| `swipe_down` | **Less / hide** | Decrease a size or timer digit, hide an answer, or scroll backward through the library. |
| `swipe_left` | **Previous / turn left** | Previous item or timer look. During rotation, turn anticlockwise. |
| `swipe_right` | **Next / turn right** | Next item or timer look. During rotation, turn clockwise. |

**Click operates apps; grab-and-release handles placement and confirmation.** Clicking blank space does nothing. Wiggle fingers opens controls; point-and-rotate turns a browser or PDF upright; pinch-and-rotate turns a screen lying on a table.

## Create a screen and open an app

1. Aim at an unoccupied flat surface and **grab-and-release**. If the surface is not ready, scan it slowly or use the HUD's Place Screen button to see surface highlights.
2. The new screen opens its app chooser automatically. An empty screen also opens the chooser when you point at it again.
3. Aim at an app and **click**, or swipe through the choices and click the highlighted choice.

To switch an existing screen's app, aim at it, **wiggle fingers**, and choose **Choose app**. Opening or closing the chooser does not replace its current content; choosing another app does.

## Use menus without precise aiming

Swipe to highlight a choice, then **click**. The highlighted choice takes priority over the ray, so a small aiming wobble does not select the wrong item. **Crumple** closes the library; on a pointed screen or its launcher/settings menu, it marks the screen red for deletion.

In the photo library, **left/right** moves between thumbnails; **up** scrolls forward a row and **down** scrolls back. Click opens the selected photo or video. Wait for its thumbnail to finish loading. The initial iPhone Photos permission prompt still needs a screen tap.

## Move, resize or rotate

Point at a screen and use the matching **pinch** gesture. The selected screen stays locked to that hand, so you can stop aiming precisely at it while editing.

- **Move:** aim at the destination surface.
- **Resize:** swipe up/down. Each step changes width by **1.6 cm** and preserves the current app/media proportions; a 16:9 screen changes height by **0.9 cm**.
- **Rotate:** swipe left/right in **5° steps**. Rotation is supported on horizontal surfaces; wall-mounted screens remain level.

**Grab-and-release saves; crumple cancels.** Cancelling a move keeps the old position. Cancelling a resize/rotation restores the size and rotation from the start of that edit. Switching between resize and rotate keeps that original snapshot; starting a move saves earlier size/rotation changes. The other hand cannot take over an active edit.

## Operate the apps

| App | Click on its face | Swipe up/down | Swipe left/right |
| --- | --- | --- | --- |
| **Timer** | Start/pause/resume; reset after it finishes | Increase/decrease the digit nearest the ray: tens of minutes, minutes, tens of seconds, seconds | Previous/next timer look; countdown continues |
| **German flashcards** | Flip between question and answer | Reveal/hide the answer | Previous/next card |
| **Slider puzzle** | Start/pause/resume; new game after solving | Playing: slide a tile up/down. Not playing: larger/smaller board (3×3, 4×4, 5×5) | Playing: slide a tile left/right. Not playing: left deals a new game |
| **Photo** | Open the media library | Up opens the library; down has no action | Previous/next photo or video |
| **Video** | Play/pause | Up opens the library; down has no action | Previous/next photo or video |

When a menu or edit is open, swipes control that menu or edit instead of the app underneath.

## Delete, get help and test

**Browser:** choose Browser from the app menu. **Click** activates the pointed link, toolbar button or keyboard key. **Swipe up/down** scrolls down/up; **swipe left/right** goes back/forward. Click the address bar to enter a website or search, then **Enter / Go**. A web text field opens the AR keyboard, which pops out below the screen at 1.5 times its width; use **Keys** to show it manually and **Hide keys** to dismiss it. **Point and rotate** turns the browser upright like a phone, and back. The same controls work with either hand and in goggles. Fingertip contact also activates controls. Crumple retains its usual cancel/delete action.

**PDF viewer:** copy PDFs into **Files → On My iPhone → Vis-Test1 → PDFs**, then choose **PDF viewer** on a screen. The explorer works like the photo library: point and **click** a filename tile, or swipe to highlight a file and click to open it. **Swipe up/down** scrolls through the document; **point and rotate** turns it upright for reading. Click **Files** on its toolbar to choose another PDF; **Refresh** updates the file list after changes in Files. You can also use fingertip contact. Screen resizing/moving and both hands work as usual, including in goggles.

- **Delete:** point at a screen and crumple-and-toss to show a red aura. Then point at that same screen and grab-and-release to delete it. Pointing away cannot confirm; another gesture cancels the mark. The explicit **wiggle fingers → Delete screen** button still deletes directly.
- **Help:** hide the debug HUD, then aim at the small green strip on the right edge. Point away to dismiss the guide.
- **Test gestures:** in the phone's debug HUD, open **Gestures**, choose Left or Right Hand, and tap a gesture button.

## Wi-Fi names

GestureReceiver accepts the names in the table, ignoring case. `swipt_left`, `swipt_right` and `wriggle_fingers` remain aliases; `wriggle_fingers` opens screen settings too.

```json
{"type":"GESTURE","gesture":"click","hand":"right"}
```

Use `"hand":"left"` for the left hand. An omitted hand defaults to right. Bindings, packet examples and on-screen captions are in [commands.json](Resources/commands.json).
