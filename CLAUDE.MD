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

Supported controls:

- Play / pause.
- Stop and return to the first frame.
- Drag the progress slider to seek.
- Show current time and total duration.

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
