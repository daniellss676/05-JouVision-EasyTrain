using JouVisionEasyTrain.Services;
using Microsoft.Win32;
using OpenCvSharp;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace JouVisionEasyTrain;

public partial class MainWindow : System.Windows.Window
{
    private const string PlaySymbol = "▶";
    private const string PauseSymbol = "⏸";
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _lastOutputFolder;
    private double? _sourceFramesPerSecond;
    private readonly DispatcherTimer _playbackTimer;
    private VideoCapture? _playbackCapture;
    private double _playbackFramesPerSecond = 30;
    private TimeSpan _playbackDuration = TimeSpan.Zero;
    private TimeSpan? _segmentStartTime;
    private TimeSpan? _segmentEndTime;
    private int _previewRotationDegrees;
    private bool _isPlaybackSeeking;
    private bool _isVideoLoaded;
    private bool _isVideoPlaying;

    public MainWindow()
    {
        InitializeComponent();
        PlayPauseButton.Content = PlaySymbol;

        _playbackTimer = new DispatcherTimer();
        _playbackTimer.Tick += PlaybackTimer_Tick;
    }

    private void SelectVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择视频文件",
            Filter = "视频文件|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.m4v|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        VideoPathTextBox.Text = dialog.FileName;
        LoadVideoForPlayback(dialog.FileName);
        OutputFolderTextBox.Clear();
        _lastOutputFolder = null;
        OpenOutputButton.IsEnabled = false;
        _sourceFramesPerSecond = null;
        SourceFrameRateTextBlock.Text = "读取中...";

        try
        {
            var videoInfo = VideoFrameExtractor.ReadVideoInfo(dialog.FileName);
            _sourceFramesPerSecond = videoInfo.FramesPerSecond;
            SourceFrameRateTextBlock.Text = $"{videoInfo.FramesPerSecond:0.###} FPS，约 {videoInfo.TotalFrames} 帧";
        }
        catch (Exception ex)
        {
            SourceFrameRateTextBlock.Text = "读取失败";
            MessageBox.Show(this, ex.Message, "视频信息读取失败", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var videoFolder = Path.GetDirectoryName(dialog.FileName);
        var videoName = Path.GetFileNameWithoutExtension(dialog.FileName);
        OutputFolderTextBox.Text = Path.Combine(videoFolder ?? string.Empty, $"{videoName}_frames");

        StatusTextBlock.Text = "已选择视频，可以设置 FPS 后开始生成。";
    }

    private void SelectOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择图片输出文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        OutputFolderTextBox.Text = dialog.FolderName;
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCreateOptions(out var options))
        {
            return;
        }

        SetRunningState(true);
        _cancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<FrameExtractionProgress>(UpdateProgress);

        try
        {
            _lastOutputFolder = null;
            ProgressBar.Value = 0;
            ProgressTextBlock.Text = "0%";
            StatusTextBlock.Text = "正在拆分视频，请等待...";
            var result = await VideoFrameExtractor.ExtractAsync(
                options,
                progress,
                _cancellationTokenSource.Token);

            _lastOutputFolder = result.OutputFolder;
            StatusTextBlock.Text = $"生成完成，共保存 {result.SavedFrameCount} 张图片。输出目录：{result.OutputFolder}";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "已取消生成。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "生成失败", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "生成失败，请检查视频文件和输出目录。";
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            SetRunningState(false);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        StatusTextBlock.Text = "正在取消...";
    }

    private void OpenOutputButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _lastOutputFolder ?? OutputFolderTextBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "输出文件夹不存在。", "无法打开", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private bool TryCreateOptions(out FrameExtractionOptions options)
    {
        options = default!;

        var videoPath = VideoPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
        {
            MessageBox.Show(this, "请先选择有效的视频文件。", "缺少视频", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var outputFolder = OutputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            MessageBox.Show(this, "请选择图片输出文件夹。", "缺少输出目录", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!double.TryParse(FrameRateTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var targetFps) &&
            !double.TryParse(FrameRateTextBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out targetFps))
        {
            MessageBox.Show(this, "目标帧率必须是数字，例如 1、2、5。", "帧率无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (targetFps <= 0)
        {
            MessageBox.Show(this, "目标帧率必须大于 0。", "帧率无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        if (!_sourceFramesPerSecond.HasValue)
        {
            try
            {
                var videoInfo = VideoFrameExtractor.ReadVideoInfo(videoPath);
                _sourceFramesPerSecond = videoInfo.FramesPerSecond;
                SourceFrameRateTextBlock.Text = $"{videoInfo.FramesPerSecond:0.###} FPS，约 {videoInfo.TotalFrames} 帧";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "视频信息读取失败", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        if (targetFps > _sourceFramesPerSecond.Value)
        {
            MessageBox.Show(
                this,
                $"设置帧率不能超过视频原始帧率。当前视频原始帧率为 {_sourceFramesPerSecond.Value:0.###} FPS。",
                "帧率无效",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (_segmentStartTime.HasValue &&
            _segmentEndTime.HasValue &&
            _segmentEndTime.Value <= _segmentStartTime.Value)
        {
            MessageBox.Show(this, "导出终点必须晚于导出起点。", "导出范围无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var imageExtension = ((ComboBoxItem)ImageFormatComboBox.SelectedItem).Content?.ToString() ?? "jpg";

        options = new FrameExtractionOptions(
            videoPath,
            outputFolder,
            targetFps,
            imageExtension,
            _previewRotationDegrees,
            _segmentStartTime,
            _segmentEndTime);
        return true;
    }

    private void UpdateProgress(FrameExtractionProgress progress)
    {
        ProgressBar.Value = progress.Percent;
        ProgressTextBlock.Text = $"{progress.Percent:0}%";

        var totalText = progress.TotalFrames > 0 ? progress.TotalFrames.ToString(CultureInfo.InvariantCulture) : "未知";
        SummaryTextBlock.Text = $"已读取 {progress.CurrentFrame}/{totalText} 帧，已保存 {progress.SavedFrameCount} 张图片。";
    }

    private void SetRunningState(bool isRunning)
    {
        StartButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning;
        OpenOutputButton.IsEnabled = !isRunning && Directory.Exists(_lastOutputFolder ?? OutputFolderTextBox.Text.Trim());
        SelectVideoButton.IsEnabled = !isRunning;
        SelectOutputButton.IsEnabled = !isRunning;
        VideoPathTextBox.IsEnabled = !isRunning;
        OutputFolderTextBox.IsEnabled = !isRunning;
        FrameRateTextBox.IsEnabled = !isRunning;
        ImageFormatComboBox.IsEnabled = !isRunning;
        SetPlaybackControlsEnabled(!isRunning && _isVideoLoaded);

        StartButton.Content = isRunning ? "生成中..." : "▶  开始生成";
    }

    private void LoadVideoForPlayback(string videoPath)
    {
        _playbackTimer.Stop();
        _isVideoPlaying = false;
        _isPlaybackSeeking = false;
        _isVideoLoaded = false;
        _playbackCapture?.Dispose();
        _playbackCapture = null;
        VideoPreviewImage.Source = null;
        VideoPositionSlider.Value = 0;
        VideoPositionSlider.Maximum = 1;
        VideoTimeTextBlock.Text = "00:00 / 00:00";
        PlayPauseButton.Content = PlaySymbol;
        _previewRotationDegrees = 0;
        ApplyPreviewRotation();
        _segmentStartTime = null;
        _segmentEndTime = null;
        UpdateSegmentRangeText();
        UpdateSegmentMarkers();

        try
        {
            _playbackCapture = new VideoCapture(videoPath);
            if (!_playbackCapture.IsOpened())
            {
                throw new InvalidOperationException("无法打开视频预览。");
            }

            _playbackFramesPerSecond = _playbackCapture.Fps;
            if (_playbackFramesPerSecond <= 0 || double.IsNaN(_playbackFramesPerSecond))
            {
                _playbackFramesPerSecond = 30;
            }

            var totalFrames = Math.Max(0, _playbackCapture.FrameCount);
            _playbackDuration = totalFrames > 0
                ? TimeSpan.FromSeconds(totalFrames / _playbackFramesPerSecond)
                : TimeSpan.Zero;

            VideoPositionSlider.Maximum = Math.Max(1, _playbackDuration.TotalSeconds);
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(1000d / _playbackFramesPerSecond, 15, 100));
            _isVideoLoaded = true;
            PlayerPlaceholderTextBlock.Visibility = Visibility.Collapsed;
            RenderCurrentFrame(resetAfterRead: true);
            VideoTimeTextBlock.Text = $"00:00 / {GetPlaybackDurationText()}";
            UpdateSegmentMarkers();
            SetPlaybackControlsEnabled(_cancellationTokenSource is null);
        }
        catch (Exception ex)
        {
            _playbackCapture?.Dispose();
            _playbackCapture = null;
            _isVideoLoaded = false;
            PlayerPlaceholderTextBlock.Text = "视频预览加载失败";
            PlayerPlaceholderTextBlock.Visibility = Visibility.Visible;
            SetPlaybackControlsEnabled(false);
            MessageBox.Show(this, ex.Message, "视频播放失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlaybackTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isVideoLoaded || _playbackCapture is null)
        {
            return;
        }

        using var frame = new Mat();
        if (!_playbackCapture.Read(frame) || frame.Empty())
        {
            StopPlayback(resetPosition: true);
            return;
        }

        VideoPreviewImage.Source = CreateBitmapSource(frame);
        UpdatePlaybackPosition();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isVideoLoaded)
        {
            return;
        }

        if (_isVideoPlaying)
        {
            _playbackTimer.Stop();
            _isVideoPlaying = false;
            PlayPauseButton.Content = PlaySymbol;
            UpdatePlaybackPosition();
            return;
        }

        _playbackTimer.Start();
        _isVideoPlaying = true;
        PlayPauseButton.Content = PauseSymbol;
    }

    private void RotatePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isVideoLoaded)
        {
            return;
        }

        _previewRotationDegrees = (_previewRotationDegrees + 90) % 360;
        ApplyPreviewRotation();
    }

    private void StopPlaybackButton_Click(object sender, RoutedEventArgs e)
    {
        StopPlayback(resetPosition: true);
    }

    private void SetSegmentStartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isVideoLoaded)
        {
            return;
        }

        _segmentStartTime = GetSelectedPlaybackTime();
        _segmentEndTime = null;
        UpdateSegmentRangeText();
        UpdateSegmentMarkers();
    }

    private void SetSegmentEndButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isVideoLoaded)
        {
            return;
        }

        if (!_segmentStartTime.HasValue)
        {
            MessageBox.Show(this, "请先设置导出起点。", "缺少起点", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectedTime = GetSelectedPlaybackTime();
        if (selectedTime <= _segmentStartTime.Value)
        {
            MessageBox.Show(this, "导出终点必须晚于导出起点。", "导出范围无效", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _segmentEndTime = selectedTime;
        UpdateSegmentRangeText();
        UpdateSegmentMarkers();
    }

    private void VideoPositionSlider_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSegmentMarkers();
    }

    private void SegmentRangeCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSegmentMarkers();
    }

    private void VideoPositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isPlaybackSeeking = true;
    }

    private void VideoPositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        SeekPlayback();
        _isPlaybackSeeking = false;
        UpdatePlaybackPosition();
    }

    private void VideoPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isPlaybackSeeking)
        {
            VideoTimeTextBlock.Text = $"{FormatDuration(TimeSpan.FromSeconds(VideoPositionSlider.Value))} / {GetPlaybackDurationText()}";
            UpdatePlaybackProgressMarker();
        }
    }

    private void StopPlayback(bool resetPosition)
    {
        _playbackTimer.Stop();
        _isVideoPlaying = false;
        PlayPauseButton.Content = PlaySymbol;

        if (resetPosition)
        {
            SeekCapture(TimeSpan.Zero);
            RenderCurrentFrame(resetAfterRead: true);
            VideoPositionSlider.Value = 0;
            VideoTimeTextBlock.Text = $"00:00 / {GetPlaybackDurationText()}";
        }
        else
        {
            UpdatePlaybackPosition();
        }
    }

    private void SeekPlayback()
    {
        if (!_isVideoLoaded || _playbackCapture is null)
        {
            return;
        }

        SeekCapture(TimeSpan.FromSeconds(VideoPositionSlider.Value));
        RenderCurrentFrame(resetAfterRead: false);
        UpdatePlaybackPosition();
    }

    private void UpdatePlaybackPosition()
    {
        if (!_isVideoLoaded || _playbackCapture is null || _isPlaybackSeeking)
        {
            return;
        }

        var currentTime = GetCurrentPlaybackTime();
        VideoPositionSlider.Value = Math.Min(currentTime.TotalSeconds, VideoPositionSlider.Maximum);
        VideoTimeTextBlock.Text = $"{FormatDuration(currentTime)} / {GetPlaybackDurationText()}";
        UpdatePlaybackProgressMarker();
    }

    private string GetPlaybackDurationText()
    {
        return _playbackDuration > TimeSpan.Zero ? FormatDuration(_playbackDuration) : "00:00";
    }

    private TimeSpan GetCurrentPlaybackTime()
    {
        if (_playbackCapture is null)
        {
            return TimeSpan.Zero;
        }

        var frameIndex = Math.Max(0, _playbackCapture.PosFrames);
        return TimeSpan.FromSeconds(frameIndex / _playbackFramesPerSecond);
    }

    private TimeSpan GetSelectedPlaybackTime()
    {
        return TimeSpan.FromSeconds(Math.Clamp(VideoPositionSlider.Value, 0, VideoPositionSlider.Maximum));
    }

    private void UpdateSegmentRangeText()
    {
        var startText = _segmentStartTime.HasValue ? FormatDuration(_segmentStartTime.Value) : "视频开始";
        var endText = _segmentEndTime.HasValue ? FormatDuration(_segmentEndTime.Value) : "视频结束";
        SegmentRangeTextBlock.Text = $"导出范围：{startText} - {endText}";
    }

    private void UpdateSegmentMarkers()
    {
        UpdateTimelineBase();
        UpdatePlaybackProgressMarker();
        SetSegmentMarker(SegmentStartMarkerDot, _segmentStartTime);
        SetSegmentMarker(SegmentEndMarkerDot, _segmentEndTime);
        UpdateSegmentRangeLine();
    }

    private void SetSegmentMarker(FrameworkElement marker, TimeSpan? markerTime)
    {
        if (!markerTime.HasValue || VideoPositionSlider.Maximum <= 0 || SegmentRangeCanvas.ActualWidth <= 0)
        {
            marker.Visibility = Visibility.Collapsed;
            return;
        }

        var markerSeconds = Math.Clamp(markerTime.Value.TotalSeconds, VideoPositionSlider.Minimum, VideoPositionSlider.Maximum);
        var range = VideoPositionSlider.Maximum - VideoPositionSlider.Minimum;
        var ratio = range > 0 ? (markerSeconds - VideoPositionSlider.Minimum) / range : 0;
        var markerX = ratio * SegmentRangeCanvas.ActualWidth;

        Canvas.SetLeft(marker, markerX - marker.Width / 2);
        Canvas.SetTop(marker, 2);
        marker.Visibility = Visibility.Visible;
    }

    private void UpdateTimelineBase()
    {
        var width = SegmentRangeCanvas.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        TimelineBaseLine.Width = width;
        TimelineTickLine.Width = width;
        Canvas.SetTop(TimelineBaseLine, 10);
        Canvas.SetTop(TimelineTickLine, 12);
    }

    private void UpdatePlaybackProgressMarker()
    {
        if (!_isVideoLoaded || VideoPositionSlider.Maximum <= 0 || SegmentRangeCanvas.ActualWidth <= 0)
        {
            PlaybackProgressLine.Visibility = Visibility.Collapsed;
            PlaybackPositionThumb.Visibility = Visibility.Collapsed;
            return;
        }

        var currentX = GetTimelineX(TimeSpan.FromSeconds(VideoPositionSlider.Value));
        PlaybackProgressLine.Width = Math.Max(0, currentX);
        Canvas.SetLeft(PlaybackProgressLine, 0);
        Canvas.SetTop(PlaybackProgressLine, 10);
        PlaybackProgressLine.Visibility = Visibility.Visible;

        Canvas.SetLeft(PlaybackPositionThumb, currentX - PlaybackPositionThumb.Width / 2);
        Canvas.SetTop(PlaybackPositionThumb, 2);
        PlaybackPositionThumb.Visibility = Visibility.Visible;
    }

    private void UpdateSegmentRangeLine()
    {
        if (!_segmentStartTime.HasValue ||
            !_segmentEndTime.HasValue ||
            VideoPositionSlider.Maximum <= 0 ||
            SegmentRangeCanvas.ActualWidth <= 0)
        {
            SegmentRangeLine.Visibility = Visibility.Collapsed;
            return;
        }

        var startX = GetTimelineX(_segmentStartTime.Value);
        var endX = GetTimelineX(_segmentEndTime.Value);
        if (endX <= startX)
        {
            SegmentRangeLine.Visibility = Visibility.Collapsed;
            return;
        }

        Canvas.SetLeft(SegmentRangeLine, startX);
        Canvas.SetTop(SegmentRangeLine, 10);
        SegmentRangeLine.Width = endX - startX;
        SegmentRangeLine.Visibility = Visibility.Visible;
    }

    private double GetTimelineX(TimeSpan time)
    {
        var markerSeconds = Math.Clamp(time.TotalSeconds, VideoPositionSlider.Minimum, VideoPositionSlider.Maximum);
        var range = VideoPositionSlider.Maximum - VideoPositionSlider.Minimum;
        var ratio = range > 0 ? (markerSeconds - VideoPositionSlider.Minimum) / range : 0;
        return ratio * SegmentRangeCanvas.ActualWidth;
    }

    private void SeekCapture(TimeSpan position)
    {
        if (_playbackCapture is null)
        {
            return;
        }

        var boundedSeconds = Math.Clamp(position.TotalSeconds, 0, VideoPositionSlider.Maximum);
        _playbackCapture.Set(VideoCaptureProperties.PosFrames, boundedSeconds * _playbackFramesPerSecond);
    }

    private void RenderCurrentFrame(bool resetAfterRead)
    {
        if (_playbackCapture is null)
        {
            return;
        }

        var frameIndex = _playbackCapture.PosFrames;
        using var frame = new Mat();
        if (!_playbackCapture.Read(frame) || frame.Empty())
        {
            return;
        }

        VideoPreviewImage.Source = CreateBitmapSource(frame);

        if (resetAfterRead)
        {
            _playbackCapture.Set(VideoCaptureProperties.PosFrames, frameIndex);
        }
    }

    private static BitmapSource CreateBitmapSource(Mat frame)
    {
        using var convertedFrame = new Mat();
        Mat displayFrame;
        PixelFormat pixelFormat;

        if (frame.Channels() == 3)
        {
            displayFrame = frame;
            pixelFormat = PixelFormats.Bgr24;
        }
        else if (frame.Channels() == 4)
        {
            Cv2.CvtColor(frame, convertedFrame, ColorConversionCodes.BGRA2BGR);
            displayFrame = convertedFrame;
            pixelFormat = PixelFormats.Bgr24;
        }
        else
        {
            Cv2.CvtColor(frame, convertedFrame, ColorConversionCodes.GRAY2BGR);
            displayFrame = convertedFrame;
            pixelFormat = PixelFormats.Bgr24;
        }

        var bitmap = BitmapSource.Create(
            displayFrame.Width,
            displayFrame.Height,
            96,
            96,
            pixelFormat,
            null,
            displayFrame.Data,
            (int)(displayFrame.Step() * displayFrame.Height),
            (int)displayFrame.Step());
        bitmap.Freeze();
        return bitmap;
    }

    private static string FormatDuration(TimeSpan timeSpan)
    {
        return timeSpan.TotalHours >= 1
            ? timeSpan.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : timeSpan.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private void SetPlaybackControlsEnabled(bool isEnabled)
    {
        RotatePreviewButton.IsEnabled = isEnabled;
        PlayPauseButton.IsEnabled = isEnabled;
        VideoPositionSlider.IsEnabled = isEnabled;
        SetSegmentStartButton.IsEnabled = isEnabled;
        SetSegmentEndButton.IsEnabled = isEnabled;
    }

    private void ApplyPreviewRotation()
    {
        VideoPreviewRotateTransform.Angle = _previewRotationDegrees;
    }

    protected override void OnClosed(EventArgs e)
    {
        _playbackTimer.Stop();
        _playbackCapture?.Dispose();
        base.OnClosed(e);
    }
}
