using OpenCvSharp;
using System.Globalization;
using System.IO;

namespace JouVisionEasyTrain.Services;

public sealed record FrameExtractionOptions(
    string VideoPath,
    string OutputFolder,
    double TargetFramesPerSecond,
    string ImageExtension,
    int RotationDegrees = 0,
    TimeSpan? StartTime = null,
    TimeSpan? EndTime = null);

public sealed record FrameExtractionProgress(
    double Percent,
    int SavedFrameCount,
    long CurrentFrame,
    long TotalFrames);

public sealed record VideoInfo(
    double FramesPerSecond,
    long TotalFrames);

public sealed class FrameExtractionResult
{
    public required int SavedFrameCount { get; init; }
    public required string OutputFolder { get; init; }
}

public static class VideoFrameExtractor
{
    public static VideoInfo ReadVideoInfo(string videoPath)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException("无法打开视频文件，请确认视频格式是否受支持。");
        }

        var sourceFps = capture.Fps;
        if (sourceFps <= 0 || double.IsNaN(sourceFps))
        {
            sourceFps = 30;
        }

        var totalFrames = (long)Math.Max(0, capture.FrameCount);
        return new VideoInfo(sourceFps, totalFrames);
    }

    public static Task<FrameExtractionResult> ExtractAsync(
        FrameExtractionOptions options,
        IProgress<FrameExtractionProgress> progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => Extract(options, progress, cancellationToken), cancellationToken);
    }

    private static FrameExtractionResult Extract(
        FrameExtractionOptions options,
        IProgress<FrameExtractionProgress> progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputFolder);

        using var capture = new VideoCapture(options.VideoPath);
        if (!capture.IsOpened())
        {
            throw new InvalidOperationException("无法打开视频文件，请确认视频格式是否受支持。");
        }

        var sourceFps = capture.Fps;
        if (sourceFps <= 0 || double.IsNaN(sourceFps))
        {
            sourceFps = 30;
        }

        var totalFrames = (long)Math.Max(0, capture.FrameCount);
        var startFrame = GetBoundedFrameIndex(options.StartTime, sourceFps, totalFrames, 0);
        var endFrameExclusive = GetBoundedFrameIndex(options.EndTime, sourceFps, totalFrames, totalFrames);
        if (totalFrames <= 0)
        {
            startFrame = 0;
            endFrameExclusive = long.MaxValue;
        }

        if (endFrameExclusive <= startFrame)
        {
            throw new InvalidOperationException("导出终点必须晚于导出起点。");
        }

        if (startFrame > 0)
        {
            capture.Set(VideoCaptureProperties.PosFrames, startFrame);
        }

        var framesToProcess = endFrameExclusive == long.MaxValue
            ? 0
            : Math.Max(0, endFrameExclusive - startFrame);
        var frameStep = options.TargetFramesPerSecond >= sourceFps
            ? 1
            : sourceFps / options.TargetFramesPerSecond;

        var videoName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var nextFrameToSave = (double)startFrame;
        var currentFrame = startFrame;
        var savedCount = 0;

        using var frame = new Mat();
        while (currentFrame < endFrameExclusive && capture.Read(frame))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!frame.Empty() && currentFrame + 0.0001 >= nextFrameToSave)
            {
                savedCount++;
                var fileName = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{videoName}_{savedCount:000000}.{options.ImageExtension}");
                var outputPath = Path.Combine(options.OutputFolder, fileName);

                using var outputFrame = RotateFrame(frame, options.RotationDegrees);
                if (!Cv2.ImWrite(outputPath, outputFrame))
                {
                    throw new IOException($"保存图片失败：{outputPath}");
                }

                nextFrameToSave += frameStep;
            }

            currentFrame++;
            var processedFrames = Math.Max(0, currentFrame - startFrame);
            var percent = framesToProcess > 0
                ? Math.Min(100, processedFrames * 100d / framesToProcess)
                : 0;
            progress.Report(new FrameExtractionProgress(percent, savedCount, processedFrames, framesToProcess));
        }

        progress.Report(new FrameExtractionProgress(100, savedCount, Math.Max(0, currentFrame - startFrame), framesToProcess));

        return new FrameExtractionResult
        {
            SavedFrameCount = savedCount,
            OutputFolder = options.OutputFolder
        };
    }

    private static long GetBoundedFrameIndex(TimeSpan? time, double framesPerSecond, long totalFrames, long fallback)
    {
        if (!time.HasValue)
        {
            return fallback;
        }

        var frameIndex = (long)Math.Floor(Math.Max(0, time.Value.TotalSeconds) * framesPerSecond);
        return totalFrames > 0 ? Math.Clamp(frameIndex, 0, totalFrames) : frameIndex;
    }

    private static Mat RotateFrame(Mat source, int rotationDegrees)
    {
        var normalizedRotation = ((rotationDegrees % 360) + 360) % 360;
        if (normalizedRotation == 0)
        {
            return source.Clone();
        }

        var rotated = new Mat();
        var rotateCode = normalizedRotation switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            270 => RotateFlags.Rotate90Counterclockwise,
            _ => throw new InvalidOperationException("只支持 0、90、180、270 度旋转。")
        };
        Cv2.Rotate(source, rotated, rotateCode);
        return rotated;
    }
}
