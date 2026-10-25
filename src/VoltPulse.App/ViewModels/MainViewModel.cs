using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VoltPulse.App.ViewModels;

/// <summary>One entry in the left navigation rail.</summary>
public sealed partial class NavItem : ObservableObject
{
    public string Label { get; }
    public Geometry Icon { get; }
    public ObservableObject Page { get; }

    [ObservableProperty]
    private bool _isCurrent;

    public NavItem(string label, Geometry icon, ObservableObject page)
    {
        Label = label;
        Icon = icon;
        Page = page;
    }
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ComingSoonViewModel _comingSoon;

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty]
    private ObservableObject? _currentPage;

    public event Action<string>? NavigationRequested;

    public MainViewModel(LiveViewModel live, DashboardViewModel graphs, HealthViewModel health, SessionsViewModel sessions,
        InsightsViewModel insights, PowerUsageViewModel power, AlertsViewModel alerts, ReportsViewModel reports,
        SettingsViewModel settings, AboutViewModel about, ComingSoonViewModel comingSoon)
    {
        _comingSoon = comingSoon;
        NavItems =
        [
            new NavItem("Live", (Geometry)App.Current.Resources["IconBolt"], live),
            new NavItem("Graphs", (Geometry)App.Current.Resources["IconWave"], graphs),
            new NavItem("Health", (Geometry)App.Current.Resources["IconRing"], health),
            new NavItem("Sessions", (Geometry)App.Current.Resources["IconClock"], sessions),
            new NavItem("Insights", (Geometry)App.Current.Resources["IconBulb"], insights),
            new NavItem("Power usage", (Geometry)App.Current.Resources["IconChip"], power),
            new NavItem("Alerts", (Geometry)App.Current.Resources["IconBell"], alerts),
            new NavItem("Reports", (Geometry)App.Current.Resources["IconDoc"], reports),
            new NavItem("Settings", (Geometry)App.Current.Resources["IconGear"], settings),
            new NavItem("About", (Geometry)App.Current.Resources["IconInfo"], about),
        ];
        NavItems[0].IsCurrent = true;
        Serilog.Log.Information("Initial page: {Page}", NavItems[0].Label);
        CurrentPage = NavItems[0].Page;
    }

    private ComingSoonViewModel Placeholder(string title)
    {
        _comingSoon.Register(title);
        return _comingSoon;
    }

    [RelayCommand]
    private void Navigate(NavItem item)
    {
        if (item is null) return;
        Serilog.Log.Information("Navigate to {Label} (NavigateCommand)", item.Label);
        foreach (var nav in NavItems)
        {
            nav.IsCurrent = nav == item;
        }
        CurrentPage = item.Page;
        NavigationRequested?.Invoke(item.Label);
    }

    [RelayCommand]
    private void NavigateTo(string label)
    {
        Serilog.Log.Information("Navigate to {Label} (NavigateToCommand)", label);
        var item = NavItems.FirstOrDefault(n => string.Equals(n.Label, label, StringComparison.OrdinalIgnoreCase));
        if (item is not null) Navigate(item);
    }
}
