using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LyricDrop.Models;
using LyricDrop.ViewModels;
using Wpf.Ui.Controls;

namespace LyricDrop.Views;

public partial class SettingsWindow : FluentWindow
{
    private static SettingsWindow? _instance;
    private readonly LyricPlayer _player;
    private bool _populating;

    public SettingsWindow(LyricPlayer player)
    {
        InitializeComponent();
        _player = player;
        DataContext = player;

        Populate();
        Closed += (_, _) => _instance = null;
    }

    public static void ShowSingleton(LyricPlayer player, Window? owner = null)
    {
        if (_instance is null)
        {
            _instance = new SettingsWindow(player);
            if (owner is not null) _instance.Owner = owner;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
    }

    private void Populate()
    {
        _populating = true;
        ThemeBox.Items.Clear();
        foreach (var t in LyricThemeExtensions.All)
        {
            var item = new ComboBoxItem { Content = t.Label(), Tag = t };
            ThemeBox.Items.Add(item);
            if (t == _player.LyricTheme) ThemeBox.SelectedItem = item;
        }

        foreach (ComboBoxItem item in LineCountBox.Items)
        {
            if ((string?)item.Tag == _player.LyricLineCount.ToString(CultureInfo.InvariantCulture))
            {
                LineCountBox.SelectedItem = item;
                break;
            }
        }

        FontBox.Items.Clear();
        FontBox.Items.Add(new ComboBoxItem { Content = "（系统默认）", Tag = string.Empty });
        foreach (var f in Fonts.SystemFontFamilies.OrderBy(x => x.Source))
        {
            FontBox.Items.Add(new ComboBoxItem { Content = f.Source, Tag = f.Source });
        }
        var current = _player.LyricFontName ?? string.Empty;
        var match = FontBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == current);
        if (match is not null) FontBox.SelectedItem = match;
        else if (!string.IsNullOrEmpty(current)) FontBox.Text = current;

        _populating = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (ThemeBox.SelectedItem is ComboBoxItem { Tag: LyricTheme t })
            _player.LyricTheme = t;
    }

    private void OnLineCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (LineCountBox.SelectedItem is ComboBoxItem { Tag: string s } &&
            int.TryParse(s, out var n))
            _player.LyricLineCount = n;
    }

    private void OnFontChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_populating) return;
        if (FontBox.SelectedItem is ComboBoxItem item)
            _player.LyricFontName = (string?)item.Tag ?? string.Empty;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
