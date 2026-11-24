using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace G321Studio;

public partial class MainWindow : Window
{
    private readonly Settings preferences = Settings.Load();
    private readonly Slider[] gains = new Slider[EqParameters.Frequencies.Length];
    private readonly TextBlock[] gainLabels = new TextBlock[EqParameters.Frequencies.Length];
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private readonly DispatcherTimer directEqTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly CancellationTokenSource lifetime = new();
    private EqParameters parameters = new(new float[EqParameters.Frequencies.Length], 0);
    private EqParameters custom = new(new float[EqParameters.Frequencies.Length], 0);
    private float monitorGain;
    private DirectEqualizer? directEq;
    private AudioRoute? monitorRoute;
    private bool syncing = true;
    private bool syncingSwitches;
    private bool refreshing;
    private bool batteryReading;
    private int? displayedLightspeedPercent;
    private int? pendingLightspeedPercent;
    private bool checkingLightspeed;
    private Endpoint? defaultPlayback;
    private string? eqError;
    private string? monitorError;

    public MainWindow()
    {
        InitializeComponent();
        BuildBands();
        EqGraph.GainChanged += (band, value) => gains[band].Value = value;
        directEqTimer.Tick += (_, _) => UpdateDirectFilters();
        PresetChoice.ItemsSource = EqParameters.Presets;
        var preset = EqParameters.Presets.FirstOrDefault(p => p.Name == preferences.PresetName)
            ?? EqParameters.Presets[0];
        if (preferences.CustomGains is { } customGains && customGains.Length == gains.Length)
            custom = new(customGains, preferences.CustomPreamp);

        PresetChoice.SelectedItem = preset;
        ApplyPreset(preset);
        MonitorSlider.Value = Math.Clamp(preferences.MonitorVolume, MonitorSlider.Minimum, MonitorSlider.Maximum);
        UpdateMonitorGain();
        syncing = false;
        UpdateAudioStatus();
        Closed += OnClosed;
        refreshTimer.Tick += async (_, _) => await RefreshAsync();
    }

    private void BuildBands()
    {
        for (int i = 0; i < gains.Length; i++)
        {
            int frequency = EqParameters.Frequencies[i];
            string label = frequency < 1000 ? $"{frequency} Hz" : $"{frequency / 1000} kHz";
            var slider = new EqualizerDial
            {
                Minimum = -12,
                Maximum = 12,
                TickFrequency = 1,
                SmallChange = 1,
                LargeChange = 3,
                IsSnapToTickEnabled = true,
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 2, 0, 2),
                ToolTip = StudioText.Get("RtBandTooltip"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AutomationProperties.SetName(slider, StudioText.Format("RtBandAutomationName", frequency));
            AutomationProperties.SetHelpText(slider, StudioText.Get("RtBandHelp"));
            slider.ValueChanged += EqualizerChanged;

            var value = new TextBlock
            {
                TextAlignment = TextAlignment.Center,
                FontSize = 12,
                FontWeight = FontWeights.Medium
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                TextAlignment = TextAlignment.Center,
                Foreground = (Brush)FindResource("Quiet"),
                FontSize = 12
            });
            stack.Children.Add(slider);
            stack.Children.Add(value);
            Bands.Children.Add(stack);
            gains[i] = slider;
            gainLabels[i] = value;
        }
    }

    internal Task StartAudioMonitoringAsync()
    {
        refreshTimer.Start();
        return RefreshAsync();
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        refreshTimer.Stop();
        lifetime.Cancel();
        if (!StopEq())
            Trace.TraceWarning(eqError);
        StopMonitor();
    }

    private void SavePreferences()
    {
        preferences.PresetName = (PresetChoice.SelectedItem as Preset)?.Name ?? "Plano";
        preferences.CustomGains = custom.Gains;
        preferences.CustomPreamp = custom.Preamp;
        preferences.MonitorVolume = (float)MonitorSlider.Value;
        try
        {
            preferences.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(ex.Message);
        }
    }

    private async void RefreshClicked(object sender, RoutedEventArgs args)
    {
        eqError = monitorError = null;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (refreshing || lifetime.IsCancellationRequested)
            return;

        try
        {
            refreshing = true;
            syncing = true;
            var headphones = AudioDevices.List(DataFlow.Render, BluetoothBattery.IsG321);
            var mics = AudioDevices.List(DataFlow.Capture, BluetoothBattery.IsG321);

            if (OutputChoice.SelectedItem is Endpoint oldOutput && !headphones.Any(e => e.Id == oldOutput.Id))
            {
                StopEq();
                StopMonitor();
            }
            if (MicChoice.SelectedItem is Endpoint oldMic && !mics.Any(e => e.Id == oldMic.Id))
                StopMonitor();

            UpdateEndpoints(OutputChoice, headphones, preferences.OutputId, preferLightspeed: true);
            UpdateEndpoints(MicChoice, mics, preferences.MicId);
            defaultPlayback = AudioDevices.GetDefaultPlayback();
        }
        catch (Exception ex)
        {
            eqError = monitorError = StudioText.Format("RtAudioListFailed", ex.Message);
        }
        finally
        {
            syncing = false;
            refreshing = false;
        }

        UpdateAudioStatus();
        await RestoreAudioAsync();
        await RefreshBatteryAsync();
    }

    private async Task RestoreAudioAsync()
    {
        if (lifetime.IsCancellationRequested)
            return;
        if (preferences.MonitorEnabled && monitorRoute is null)
            TryStartMonitor();
        if (!preferences.EqEnabled && directEq is not null)
            StopEq();
        UpdateAudioStatus();

        if (!preferences.EqEnabled || checkingLightspeed || directEq is not null ||
            OutputChoice.SelectedItem is not Endpoint output)
            return;
        checkingLightspeed = true;
        try
        {
            bool connected = await Task.Run(() => LightspeedConnection.HeadsetResponds(output.Id));
            if (!connected || lifetime.IsCancellationRequested || !preferences.EqEnabled ||
                directEq is not null || !Equals(OutputChoice.SelectedItem, output))
                return;

            eqError = null;
            StartEq(output);
            UpdateAudioStatus();
        }
        finally
        {
            checkingLightspeed = false;
        }
    }

    private async Task RefreshBatteryAsync()
    {
        if (batteryReading)
            return;

        batteryReading = true;
        string receiverUnavailable = StudioText.Get("RtLightspeedBatteryUnavailable");
        bool receiverPresent = false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            receiverPresent = OutputChoice.ItemsSource is Endpoint[] outputs &&
                outputs.Any(output => LightspeedConnection.IsReceiverEndpoint(output.Id));
            int? lightspeedPercent = await Task.Run(
                () => LightspeedConnection.ReadBatteryPercentAsync(timeout.Token), timeout.Token);
            int? level = ConfirmLightspeedPercent(lightspeedPercent);
            var bluetooth = level is null ? await BluetoothBattery.ReadAsync(timeout.Token) : null;
            if (!lifetime.IsCancellationRequested)
            {
                int? percent = level ?? bluetooth?.Percent;
                ShowBattery(percent, receiverPresent ? receiverUnavailable : bluetooth?.Detail);
            }
        }
        catch (Exception)
        {
            ConfirmLightspeedPercent(null);
            if (!lifetime.IsCancellationRequested)
            {
                ShowBattery(null, receiverPresent
                    ? receiverUnavailable
                    : StudioText.Get("RtBatteryReadFailed"));
            }
        }
        finally
        {
            batteryReading = false;
        }
    }

    private void ShowBattery(int? percent, string? unavailableDetail)
    {
        BatteryValue.Text = percent is { } value ? $"{value}%" : "—";
        BatteryProgress.Value = percent.GetValueOrDefault();
        BatteryProgress.Visibility = percent.HasValue ? Visibility.Visible : Visibility.Hidden;
        BatteryDetail.Text = percent.HasValue ? "" : unavailableDetail ?? "";
        BatteryDetail.Visibility = BatteryDetail.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private int? ConfirmLightspeedPercent(int? percent)
    {
        // The gauge can alternate between 99 and 100 percent near full charge.
        if ((displayedLightspeedPercent, percent) is ((99, 100) or (100, 99)) &&
            pendingLightspeedPercent != percent)
        {
            pendingLightspeedPercent = percent;
            return displayedLightspeedPercent;
        }

        pendingLightspeedPercent = null;
        displayedLightspeedPercent = percent;
        return percent;
    }

    private static void UpdateEndpoints(
        ComboBox combo, Endpoint[] endpoints, string? preferredId, bool preferLightspeed = false)
    {
        if (combo.ItemsSource is Endpoint[] existing && existing.SequenceEqual(endpoints))
            return;

        var previous = (combo.SelectedItem as Endpoint)?.Id;
        combo.ItemsSource = endpoints;
        combo.SelectedItem = endpoints.FirstOrDefault(e => e.Id == (preferredId ?? previous));
        if (combo.SelectedItem is null && preferredId is null)
            combo.SelectedItem = (preferLightspeed
                ? endpoints.FirstOrDefault(e => LightspeedConnection.IsReceiverEndpoint(e.Id))
                : null) ?? endpoints.FirstOrDefault();
    }

    private async void DeviceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (syncing)
            return;

        if (ReferenceEquals(sender, OutputChoice))
            preferences.OutputId = (OutputChoice.SelectedItem as Endpoint)?.Id;
        else
            preferences.MicId = (MicChoice.SelectedItem as Endpoint)?.Id;
        SavePreferences();
        eqError = monitorError = null;
        await RestoreAudioAsync();
    }

    private void PresetChanged(object sender, SelectionChangedEventArgs args)
    {
        if (syncing || PresetChoice.SelectedItem is not Preset preset)
            return;

        ApplyPreset(preset);
        SavePreferences();
    }

    private void ApplyPreset(Preset preset)
    {
        var (presetGains, preamp) = preset.Name == "Personalizado"
            ? (custom.Gains, custom.Preamp)
            : (preset.Gains, preset.Preamp);
        bool previous = syncing;
        syncing = true;
        for (int i = 0; i < gains.Length; i++)
            gains[i].Value = Math.Clamp(presetGains[i], gains[i].Minimum, gains[i].Maximum);
        PreampSlider.Value = Math.Clamp(preamp, PreampSlider.Minimum, PreampSlider.Maximum);
        syncing = previous;
        UpdateEqParameters();
    }

    private void EqualizerChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (syncing)
            return;

        UpdateEqParameters();
        custom = Volatile.Read(ref parameters);
        syncing = true;
        PresetChoice.SelectedItem = EqParameters.Presets[^1];
        syncing = false;
        SavePreferences();
    }

    private void UpdateEqParameters()
    {
        var values = new float[gains.Length];
        for (int i = 0; i < gains.Length; i++)
        {
            values[i] = (float)gains[i].Value;
            gainLabels[i].Text = $"{values[i]:+0;-0;0} dB";
        }
        EqGraph.SetGains(values);
        PreampValue.Text = $"{PreampSlider.Value:+0;-0;0} dB";
        Volatile.Write(ref parameters, new EqParameters(values, (float)PreampSlider.Value));
        if (directEq is not null)
        {
            directEqTimer.Stop();
            directEqTimer.Start();
        }
    }

    private void MonitorVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (!syncing)
        {
            UpdateMonitorGain();
            SavePreferences();
        }
    }

    private void UpdateMonitorGain()
    {
        Volatile.Write(ref monitorGain, (float)(MonitorSlider.Value / 100));
        MonitorValue.Text = $"{MonitorSlider.Value:0}%";
    }

    private async void ToggleEq(object sender, RoutedEventArgs args)
    {
        if (syncing || syncingSwitches)
            return;

        preferences.EqEnabled = EqSwitch.IsChecked == true;
        SavePreferences();
        eqError = null;
        await RestoreAudioAsync();
    }

    private void StartEq(Endpoint output)
    {
        try
        {
            var effect = LightspeedConnection.IsReceiverEndpoint(output.Id) ? DirectEqualizer.For(output) : null;
            if (effect is null)
            {
                eqError = StudioText.Get("RtEnableEqualizerApo");
                return;
            }

            effect.Apply(Volatile.Read(ref parameters));
            directEq = effect;
        }
        catch (Exception ex)
        {
            eqError = StudioText.Format("RtDirectEqStartFailed", ex.Message);
        }
    }

    private void UpdateDirectFilters()
    {
        directEqTimer.Stop();
        if (directEq is null)
            return;

        try
        {
            directEq.Apply(Volatile.Read(ref parameters));
            eqError = null;
        }
        catch (Exception ex)
        {
            eqError = StudioText.Format("RtDirectEqUpdateFailed", ex.Message);
        }
        UpdateAudioStatus();
    }

    private void ToggleMonitor(object sender, RoutedEventArgs args)
    {
        if (syncing || syncingSwitches || lifetime.IsCancellationRequested)
            return;

        monitorError = null;
        preferences.MonitorEnabled = MonitorSwitch.IsChecked == true;
        SavePreferences();
        if (preferences.MonitorEnabled)
            TryStartMonitor();
        else
            StopMonitor();
        UpdateAudioStatus();
    }

    private void TryStartMonitor()
    {
        if (MicChoice.SelectedItem is Endpoint mic && OutputChoice.SelectedItem is Endpoint output)
        {
            try
            {
                if (LightspeedConnection.ShareReceiver(mic.Id, output.Id) ||
                    LightspeedConnection.ShareBluetoothAudioDevice(mic.Id, output.Id))
                {
                    StartMonitor(mic, output);
                    monitorError = null;
                    if (preferences.MicId is null || preferences.OutputId is null)
                    {
                        preferences.MicId = mic.Id;
                        preferences.OutputId = output.Id;
                        SavePreferences();
                    }
                }
                else
                    monitorError = StudioText.Get("RtSidetoneDeviceMismatch");
            }
            catch (Exception ex)
            {
                monitorError = StudioText.Format("RtSidetoneStartFailed", ex.Message);
            }
        }
    }

    private void StartMonitor(Endpoint mic, Endpoint output)
    {
        AudioRoute? route = null;
        route = new AudioRoute(mic.Id, output.Id, () => Volatile.Read(ref monitorGain), error =>
            Dispatcher.BeginInvoke(() =>
            {
                if (route is null || lifetime.IsCancellationRequested || !ReferenceEquals(route, monitorRoute))
                    return;
                StopMonitor();
                monitorError = StudioText.Format("RtSidetoneInterrupted", error.Message);
                UpdateAudioStatus();
            }));
        monitorRoute = route;
    }

    private bool StopEq()
    {
        directEqTimer.Stop();
        try
        {
            directEq?.Clear();
            directEq = null;
        }
        catch (Exception ex)
        {
            eqError = StudioText.Format("RtDirectEqStopFailed", ex.Message);
            return false;
        }

        return true;
    }

    private void StopMonitor()
    {
        var route = monitorRoute;
        monitorRoute = null;
        route?.Dispose();
    }

    private void UpdateAudioStatus()
    {
        bool hasOutput = OutputChoice.SelectedItem is Endpoint;
        bool hasMic = MicChoice.SelectedItem is Endpoint;
        bool eqActive = directEq is not null;
        bool monitorActive = monitorRoute is not null;

        syncingSwitches = true;
        EqSwitch.IsChecked = preferences.EqEnabled;
        MonitorSwitch.IsChecked = preferences.MonitorEnabled;
        syncingSwitches = false;
        EqSwitchState.Text = StudioText.Get(preferences.EqEnabled ? "RtOn" : "RtOff");
        MonitorSwitchState.Text = StudioText.Get(preferences.MonitorEnabled ? "RtOn" : "RtOff");
        OutputChoice.IsEnabled = !eqActive && !monitorActive;
        MicChoice.IsEnabled = !monitorActive && hasMic;

        string? eqStatus = eqError ?? (eqActive ? GetActiveEqNotice()
            : preferences.EqEnabled ? StudioText.Get("RtAutoEqAwaitingLightspeed") : null);
        EqStatus.Text = eqStatus ?? "";
        EqStatus.Visibility = eqStatus is null ? Visibility.Collapsed : Visibility.Visible;
        MonitorStatus.Text = monitorError ?? (preferences.MonitorEnabled && (!hasMic || !hasOutput)
            ? StudioText.Get("RtAwaitingMicrophone")
            : "");
        MonitorStatus.Visibility = string.IsNullOrEmpty(MonitorStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private string? GetActiveEqNotice() =>
        defaultPlayback is { } current && (OutputChoice.SelectedItem as Endpoint)?.Id != current.Id
            ? StudioText.Format("RtDefaultOutputNotice", current.Name)
            : null;

    private void OpenSoundSettings(object sender, RoutedEventArgs args)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            eqError = StudioText.Format("RtSoundSettingsOpenFailed", ex.Message);
            UpdateAudioStatus();
        }
    }
}
