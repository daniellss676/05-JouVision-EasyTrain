using OpenCvSharp;
using System.Globalization;
using System.IO;

namespace JouVisionEasyTrain.Services;

public sealed record FrameExtractionOptions(
    string VideoPath,
    string OutputFolder,
    double TargetFramesPerSecond,
    string ImageExtension);

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
        var frameStep = options.TargetFramesPerSecond >= sourceFps
            ? 1
            : sourceFps / options.TargetFramesPerSecond;

        var videoName = Path.GetFileNameWithoutExtension(options.VideoPath);
        var nextFrameToSave = 0d;
        var currentFrame = 0L;
        var savedCount = 0;

        using var frame = new Mat();
        while (capture.Read(frame))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!frame.Empty() && currentFrame + 0.0001 >= nextFrameToSave)
            {
                savedCount++;
                var fileName = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{videoName}_{savedCount:000000}.{options.ImageExtension}");
                var outputPath = Path.Combine(options.OutputFolder, fileName);

                if (!Cv2.ImWrite(outputPath, frame))
                {
                    throw new IOException($"保存图片失败：{outputPath}");
                }

                nextFrameToSave += frameStep;
            }

            currentFrame++;
            var percent = totalFrames > 0
                ? Math.Min(100, currentFrame * 100d / totalFrames)
                : 0;
            progress.Report(new FrameExtractionProgress(percent, savedCount, currentFrame, totalFrames));
        }

        progress.Report(new FrameExtractionProgress(100, savedCount, currentFrame, totalFrames));

        return new FrameExtractionResult
        {
            SavedFrameCount = savedCount,
            OutputFolder = options.OutputFolder
        };
    }
}
