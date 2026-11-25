using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AlKatta;

/// <summary>true -> Collapsed, false -> Visible (for busy/empty toggles).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>int count &gt; 0 -> Visible, else Collapsed.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (value is int n && n > 0) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>int count == 0 -> Visible, else Collapsed (empty-state placeholder).</summary>
public sealed class EmptyCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (value is int n && n > 0) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>true -> Visible, false -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>Live rate (kbps as double) -> "0 Kbps" / "842 Kbps" / "12.4 Mbps".</summary>
public sealed class RateConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        double kbps = value is double d ? d : value is int i ? i : 0;
        if (kbps <= 0) return "0 Kbps";
        if (kbps < 1000) return $"{kbps:N0} Kbps";
        return $"{kbps / 1000.0:N1} Mbps";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

/// <summary>First visible character of a name, uppercased — for the avatar badge.</summary>
public sealed class FirstLetterConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var s = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(s)) return "•";
        return char.ToUpperInvariant(s[0]).ToString();
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}
