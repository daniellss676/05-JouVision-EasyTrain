# CLAUDE.MD

## Project Overview

JouVision EasyTrain is a WPF desktop application targeting `net8.0-windows`.
It is used to select a video file, preview playback, and export frames at a
target FPS for training image datasets.

## Main Files

- `MainWindow.xaml`: Main WPF UI layout.
- `MainWindow.xaml.cs`: Main window event handling, frame extraction workflow,
  and video preview playback logic.
- `Services/VideoFrameExtractor.cs`: OpenCV-based video information reading and
  frame extraction.
- `JouVisionEasyTrain.csproj`: WPF project file and package references.

## Existing Features

- Select a source video file.
- Read source video FPS and total frame count with OpenCV.
- Select an output folder.
- Configure target FPS.
- Select output image format: `jpg` or `png`.
- Export frames to images using the naming pattern:
  `videoName_000001.jpg`.
- Show extraction progress and saved frame count.
- Cancel frame extraction.
- Open the output folder after generation.

## Video Playback Feature

The main window includes a right-side video preview player.
The current layout follows a card-based structure: source/output settings and
progress cards on the left, preview and range selection on the right, and a
bottom action bar for output information and commands.

Supported controls:

- Play / pause with a single symbol button.
- Rotate the preview image in 90 degree steps with a transparent rotate icon
  button.
- Drag the progress slider to seek.
- Show current time and total duration.
- Set the current playback position as the export start time.
- Set the current playback position as the export end time.
- Show red dot markers on the progress bar for selected start and end
  positions.

Implementation notes:

- Do not use WPF `MediaElement` for preview playback.
- Preview playback uses OpenCvSharp `VideoCapture`, the same decoding path used
  by frame extraction.
- Frames are read from OpenCV `Mat`, converted to WPF `BitmapSource`, frozen,
  and displayed in `VideoPreviewImage`.
- This avoids video corruption/garbled playback caused by system codec or WPF
  `MediaElement` rendering compatibility issues.
- Playback controls are disabled while frame extraction is running.
- The OpenCV `VideoCapture` used for preview is disposed when loading another
  video and when the main window closes.

## Segment Export

The export workflow supports optional start and end times selected from the
video preview.

- `SetSegmentStartButton_Click` stores the current preview time as the export
  start and clears any previous end marker.
- `SetSegmentEndButton_Click` stores the current preview time as the export end.
- The end marker can only be set after a start marker, and it must be later than
  the start time.
- `TryCreateOptions` validates that the end time is later than the start time
  when both are set.
- `FrameExtractionOptions` carries optional `StartTime` and `EndTime` values.
- `FrameExtractionOptions` also carries the current preview rotation in
  `RotationDegrees` so exported images match the rotated preview.
- `VideoFrameExtractor` converts those times to frame indexes, seeks the
  capture to the start frame, and stops reading at the end frame.
- `VideoFrameExtractor` rotates each saved frame with OpenCV before writing it
  when `RotationDegrees` is 90, 180, or 270.
- Progress is calculated against the selected segment rather than the full
  video when a bounded range is used.
- `SegmentStartMarkerDot` and `SegmentEndMarkerDot` are drawn on a transparent
  canvas over the slider to visualize the selected range.

## Build

Use:

```powershell
dotnet build .\JouVisionEasyTrain.csproj -c Debug
```

Current verification result:

- Build succeeds.
- 0 warnings.
- 0 errors.

## Development Notes

- Keep video decoding behavior consistent between preview and extraction by
  using OpenCV for both paths.
- Avoid changing generated files under `bin/`, `obj/`, or `.vs/` manually.
- `.vs/`, `bin/`, and `obj/` may change after local builds.
- The project currently has mojibake text in some existing XAML/C# Chinese
  strings. Avoid broad text rewrites unless intentionally fixing encoding.
