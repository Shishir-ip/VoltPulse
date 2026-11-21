import re

# ---------- AppSettings: PreferDiscreteGpu ----------
p = r'src/VoltPulse.Core/Models/AppSettings.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''    // Advanced
    public string? PreferredProvider { get; set; }''', '''    // Advanced
    /// <summary>Ask Windows to run VoltPulse on the discrete GPU (high performance) on hybrid-GPU laptops.</summary>
    public bool PreferDiscreteGpu { get; set; } = true;
    public string? PreferredProvider { get; set; }''')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- SettingsViewModel: wire the property ----------
p = r'src/VoltPulse.App/ViewModels/SettingsViewModel.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''        _debugLogging = s.DebugLogging;
        _demoMode = s.DemoMode;''', '''        _debugLogging = s.DebugLogging;
        _demoMode = s.DemoMode;
        _preferDiscreteGpu = s.PreferDiscreteGpu;''')
s = s.replace('''    [ObservableProperty]
    private bool _demoMode;

    partial void OnSelectedThemeNameChanged''', '''    [ObservableProperty]
    private bool _demoMode;

    [ObservableProperty]
    private bool _preferDiscreteGpu;

    partial void OnPreferDiscreteGpuChanged(bool value) => Set(s => s.PreferDiscreteGpu = value);

    partial void OnSelectedThemeNameChanged''')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- SettingsView: Advanced toggle ----------
p = r'src/VoltPulse.App/Views/SettingsView.xaml'
s = open(p, encoding='utf-8').read()
s = s.replace('''                    <Grid Style="{StaticResource Row}">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="240"/><ColumnDefinition/></Grid.ColumnDefinitions>
                        <TextBlock Text="Debug logging" VerticalAlignment="Center" />
                        <CheckBox Grid.Column="1" IsChecked="{Binding DebugLogging}" VerticalAlignment="Center" />
                    </Grid>''', '''                    <Grid Style="{StaticResource Row}">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="240"/><ColumnDefinition/></Grid.ColumnDefinitions>
                        <TextBlock Text="Run on the discrete GPU (RTX)" VerticalAlignment="Center"
                                   ToolTip="Ask Windows to render VoltPulse on the high-performance GPU instead of the integrated one. Turning this off saves battery." />
                        <CheckBox Grid.Column="1" IsChecked="{Binding PreferDiscreteGpu}" VerticalAlignment="Center" />
                    </Grid>
                    <Grid Style="{StaticResource Row}">
                        <Grid.ColumnDefinitions><ColumnDefinition Width="240"/><ColumnDefinition/></Grid.ColumnDefinitions>
                        <TextBlock Text="Debug logging" VerticalAlignment="Center" />
                        <CheckBox Grid.Column="1" IsChecked="{Binding DebugLogging}" VerticalAlignment="Center" />
                    </Grid>''')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- WmiBatteryProvider: cached status searcher + temperature backoff ----------
p = r'src/VoltPulse.Services/WmiBatteryProvider.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''    private ManagementScope? _scope;
    private DateTime _scopeFailedUntilUtc = DateTime.MinValue;''', '''    private ManagementScope? _scope;
    private ManagementObjectSearcher? _statusSearcher;
    private DateTime _scopeFailedUntilUtc = DateTime.MinValue;
    private DateTime _temperatureSkipUntilUtc = DateTime.MinValue;
    private int _temperatureEmptyStreak;''')
s = s.replace('''                var status = QueryInstances("SELECT * FROM BatteryStatus");
                var temperatures = QueryInstances("SELECT InstanceName, Temperature FROM BatteryTemperature")
                    .ToDictionary(kv => kv.Key, kv => kv.Value);''', '''                var status = GetStatusInstances();
                var temperatures = DateTime.UtcNow >= _temperatureSkipUntilUtc
                    ? QueryInstances("SELECT InstanceName, Temperature FROM BatteryTemperature")
                        .ToDictionary(kv => kv.Key, kv => kv.Value)
                    : new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);''')
s = s.replace('''                finally
                {
                    foreach (var row in status.Values) row.Dispose();
                    foreach (var row in temperatures.Values) row.Dispose();
                }''', '''                finally
                {
                    foreach (var row in status.Values) row.Dispose();
                    foreach (var row in temperatures.Values) row.Dispose();
                }

                // Machines without a temperature sensor answered with zero instances; stop
                // hammering WMI for it every second and retry once a minute instead.
                if (temperatures.Count == 0)
                {
                    _temperatureEmptyStreak++;
                    if (_temperatureEmptyStreak >= 3)
                    {
                        _temperatureSkipUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(60);
                    }
                }
                else
                {
                    _temperatureEmptyStreak = 0;
                    _temperatureSkipUntilUtc = DateTime.MinValue;
                }''')
s = s.replace('''    private Dictionary<string, ManagementBaseObject> QueryInstances(string query)
    {
        var result = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope!, new ObjectQuery(query));''', '''    /// <summary>
    /// The status query runs every tick; caching the searcher avoids re-building the WMI
    /// query object each time. Recreated whenever the scope is reconnected.
    /// </summary>
    private Dictionary<string, ManagementBaseObject> GetStatusInstances()
    {
        var result = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_statusSearcher is null)
            {
                _statusSearcher = new ManagementObjectSearcher(_scope!, new ObjectQuery("SELECT * FROM BatteryStatus"));
            }
            foreach (var o in _statusSearcher.Get())
            {
                string? instanceName = null;
                try
                {
                    instanceName = o["InstanceName"]?.ToString();
                }
                catch
                {
                    instanceName = null;
                }
                if (instanceName is null)
                {
                    using (o)
                    {
                    }
                    continue;
                }
                if (result.TryGetValue(instanceName, out var existing))
                {
                    using (existing)
                    using (o)
                    {
                    }
                    continue;
                }
                result[instanceName] = o;
            }
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.InvalidQuery)
        {
            // class not implemented on this machine - fine
        }
        return result;
    }

    private Dictionary<string, ManagementBaseObject> QueryInstances(string query)
    {
        var result = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(_scope!, new ObjectQuery(query));''')
s = s.replace('''    public void Invalidate()
    {
        lock (_gate)
        {
            _scope = null;''', '''    public void Invalidate()
    {
        lock (_gate)
        {
            _statusSearcher?.Dispose();
            _statusSearcher = null;
            _scope = null;''')
s = s.replace('''        public void Dispose()
        {
            lock (_gate)
            {
                _scope = null;
                _statics = null;
                _fullCharge = null;
                _cim = null;
            }
        }''', '''        public void Dispose()
        {
            lock (_gate)
            {
                _statusSearcher?.Dispose();
                _statusSearcher = null;
                _scope = null;
                _statics = null;
                _fullCharge = null;
                _cim = null;
            }
        }''')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- DashboardView: timer only ticks while visible ----------
p = r'src/VoltPulse.App/Views/DashboardView.xaml.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''        DataContextChanged += (_, _) => OnViewModelChanged();
        IsVisibleChanged += (_, e) =>
        {
            if (ViewModel is { } vm) vm.Active = e.NewValue is true;
        };
        Unloaded += (_, _) => _renderTimer.Stop();
        Loaded += (_, _) => _renderTimer.Start();''', '''        DataContextChanged += (_, _) => OnViewModelChanged();
        IsVisibleChanged += (_, e) =>
        {
            bool visible = e.NewValue is true;
            if (ViewModel is { } vm) vm.Active = visible;
            // No timer ticks while the page is hidden (WPF perf practice: avoid idle timers).
            if (visible) _renderTimer.Start();
            else _renderTimer.Stop();
        };
        Unloaded += (_, _) => _renderTimer.Stop();''')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- Theme brushes + icons: freeze Freezables ----------
for path, tag in [(r'src/VoltPulse.App/Theme/Dark.xaml', 'Dark'),
                  (r'src/VoltPulse.App/Theme/Light.xaml', 'Light'),
                  (r'src/VoltPulse.App/Controls/Icons.xaml', 'Icons')]:
    s = open(path, encoding='utf-8').read()
    if 'presentation/options' not in s:
        s = s.replace('xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
                      'xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"\n                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"', 1)
    s = s.replace('<SolidColorBrush ', '<SolidColorBrush po:Freeze="True" ')
    if tag == 'Icons':
        s = s.replace('<Geometry ', '<Geometry po:Freeze="True" ')
    open(path, 'w', encoding='utf-8', newline='\n').write(s)

# ---------- CI + build.ps1: ReadyToRun ----------
p = r'.github/workflows/build.yml'
s = open(p, encoding='utf-8').read()
s = s.replace('-p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true',
              '-p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishReadyToRun=true')
open(p, 'w', encoding='utf-8', newline='\n').write(s)

p = r'tools/build.ps1'
s = open(p, encoding='utf-8').read()
s = s.replace('-p:IncludeNativeLibrariesForSelfExtract=true `',
              '-p:IncludeNativeLibrariesForSelfExtract=true -p:PublishReadyToRun=true `')
open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
