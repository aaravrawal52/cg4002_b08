# Glove marker setup

For printing, use **[Glove-markers-A4.pdf](../../../output/pdf/Glove-markers-A4.pdf)**. It has a fixed **210 x 297 mm** A4 page, vector markers and a no-scaling viewer preference. Select **A4 portrait** and **Actual size / 100%** in a PDF viewer. Turn off **Fit**, **Shrink to printable area** and any additional printer-driver scaling. The preference is advisory; measure the horizontal and vertical 50 mm check lines after printing.

The supplied root markers have a 28 mm black square; tip markers have a 14 mm black square. Their complete white-bordered patches are 36 mm and 18 mm wide. **Glove-markers-A4.svg** and the individual SVGs also define these exact physical sizes, but browser printing can apply page margins, headers/footers or another scaling step even when the dialog says 100%. PNGs are reference images; their print size depends on your print application.

### If the 50 mm line prints at about 45 mm

That indicates approximately **90% scaling** (`45 / 50`). The expected printed root black square would be **25.2 mm** (`28 x 0.9`), consistent with a measurement of about 25 mm. This is a print-scale mismatch; the source artwork's dimensions are correct.

You may keep those printed markers. Root width is not a configured physical-size input: root position uses LiDAR, with the existing estimated-depth fallback for the ray. Measure the **fingertip marker's black square**, excluding its white border, and set **Tip Width Metres** to that measurement divided by 1000. At exactly 90% scale a nominal 14 mm fingertip square becomes **12.6 mm**, so enter **0.0126 m**; prefer your actual measurement. Contact offsets are physical glove measurements and should be calibrated separately.

To restore the nominal sizes, first try the PDF's Actual size option. If the same print workflow still makes a 50 mm line measure 45 mm, a compensating custom scale is **111.1%** (`100 x 50 / 45`). Print one check copy and measure again; do not apply that correction automatically to a different viewer or printer. If horizontal and vertical check lines differ, use the actual horizontal and vertical black-square measurements to diagnose nonuniform scaling before using contact-offset calibration.

| Marker | Location | ID |
| --- | --- | --- |
| Left root | Near the left index-finger base | 0 |
| Left fingertip | Near the left fingertip | 1 |
| Right root | Near the right index-finger base | 2 |
| Right fingertip | Near the right fingertip | 3 |

Use the printed **tag25h9** patterns, not QR codes, ArUco or another AprilTag family. Keep the white border. Put each marker on a small flat, matte backing so the glove does not stretch or crease its square. Keep labels/printing outside the white border. The marker's printed TOP should point towards the fingertip. Mount on a surface visible to the rear camera; turning the printed face away stops detection. Avoid glare and motion blur. Markers on opposite sides of a glove need separate support/configuration; do not print duplicate copies of an ID expecting them to fuse.

On **AR Visualizer → Hand Pointer Source → iPhone glove markers → Markers**, change the four IDs only when using different tag25h9 patterns. Invalid or repeated assignments disable tracking with a status message. Both roots can track simultaneously without seeing bare skin. A root alone supports its ray; direct touch needs the matching tip marker and valid LiDAR on both markers. Lost/old markers do not borrow the other hand's pose or keep old contacts active.

## Contact calibration

Start with **Left Contact Offset** and **Right Contact Offset** at zero. Touch then corresponds to the 3D centre of the tip marker, which may differ from the finger pad. Place a virtual screen on an accessible surface and adjust the offsets to match the point that physically contacts that surface.

Offsets are in **metres in the printed marker's local axes**, so they rotate with the finger: **X** towards the printed right edge, **Y** towards printed TOP, **Z** out of the printed face. Negative Z goes into the glove. For example, `(0, 0.008, -0.005)` means 8 mm towards the fingertip and 5 mm into the glove; this is only an example, not a measured glove preset. Keep each axis within 5 cm. Calibrate each glove separately and repeat if you move the marker.

**Tip Width Metres** is the measured printed BLACK square width excluding white border (nominal: `0.014`). It is used to solve the marker orientation for nonzero contact offsets. It never substitutes estimated distance for missing LiDAR. Accurate contact still depends on marker visibility, LiDAR confidence, marker rigidity and the existing 1.2 cm touch tolerance.

## Quality and performance

The detector checks exact payloads (no bit-error correction), minimum decode margin and minimum edge length. Start with **Minimum Margin 30**, **Minimum Edge Pixels 14**, **Quad Decimate 1**, **Samples Per Second 15**. Raise the margin to reject weaker contrast. Raising decimation reduces square-detection work but can miss small tags. Before relaxing quality gates, improve lighting or increase marker size. If resizing fingertip markers, update Tip Width Metres. Test at the intended hand-to-phone distance on the iPhone 14 Pro Max.

For intermittent tracked/untracked flicker, **Hand Pointer Source → Tracking stability → Tracking Grace Seconds** defaults to **0.3 seconds**. Each ray keeps its last valid aim through short missed detections, then disappears if that hand's root marker has not returned. Increase this slightly if necessary; 0 disables the hold. Larger values retain old aim longer, including for ray clicks. The timer starts at the successful camera capture, so processing latency uses part of the window. Direct touch still stops immediately when a required marker or its depth is missing; no fingertip contacts are held.

The iPhone detects marker positions; Mac gesture packets still select actions. If the Mac recognizer uses camera-based bare-hand detection, it also needs a separate glove-compatible input solution.
