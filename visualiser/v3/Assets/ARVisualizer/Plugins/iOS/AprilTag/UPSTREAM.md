# Bundled AprilTag

Source: https://github.com/AprilRobotics/apriltag/tree/v3.4.5
Release: **3.4.5** (pinned; no runtime download).
Archive: https://codeload.github.com/AprilRobotics/apriltag/zip/refs/tags/v3.4.5
Archive SHA-256: `3ff112eab1a9ce6c8b59890f03d436d42ec2e261e6b7f1c4c248cd80360b4a6a`

Included: detector, quad/pose implementation, tag25h9 family, supporting common C sources/headers and required root headers. Other tag-family implementations, examples, wrappers and build systems are omitted. Upstream C/header contents are unmodified. Unity `.meta` files are local packaging metadata. The application adapter is one directory up (`AVMarkerDetector.c/.h`).

License: upstream BSD 2-Clause plus notices retained in supporting sources (including the pthreads compatibility layer's MIT notice). The complete source notices are also bundled in `Resources/ThirdPartyNotices.txt` for binary redistribution.
