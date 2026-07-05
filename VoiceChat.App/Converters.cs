using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;

namespace VoiceChat.App;

/// <summary>
/// 静音按钮转换器
/// </summary>
public class MuteButtonConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool isMuted && isMuted ? "取消静音" : "静音";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool转Visibility转换器
/// </summary>
public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isTrue)
        {
            var isInverse = parameter?.ToString()?.Equals("Inverse", StringComparison.OrdinalIgnoreCase) ?? false;
            return isInverse
                ? (isTrue ? Visibility.Collapsed : Visibility.Visible)
                : (isTrue ? Visibility.Visible : Visibility.Collapsed);
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 音量值转百分比文本
/// </summary>
/// <summary>
/// 是否在说话转颜色（绿色=说话，灰色=没说话）
/// </summary>
public class BoolToSpeakingColorConverter : MarkupExtension, IValueConverter
{
    private static readonly Brush SpeakingBrush = new SolidColorBrush(Color.FromRgb(82, 196, 26));
    private static readonly Brush SilentBrush = new SolidColorBrush(Color.FromRgb(217, 217, 217));

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool speaking && speaking ? SpeakingBrush : SilentBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// 音量值转百分比文本
/// </summary>
public class VolumeToPercentageConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is float volume)
        {
            return $"{(volume * 100):F0}%";
        }
        return "100%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

