using JouVisionEasyTrain.Services;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace JouVisionEasyTrain;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _cancellationTokenSource;
    private string? _lastOutputFolder;
    private double? _sourceFramesPerSecond;

    public MainWindow()
    {
        InitializeComponent();
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

        var imageExtension = ((ComboBoxItem)ImageFormatComboBox.SelectedItem).Content?.ToString() ?? "jpg";

        options = new FrameExtractionOptions(videoPath, outputFolder, targetFps, imageExtension);
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

        StartButton.Content = isRunning ? "生成中..." : "开始生成";
    }
}
