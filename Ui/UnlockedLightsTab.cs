using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Unlocked mode, lights: where they come from (the plugin's effects or ATSR-Hub), the presets shown on the wheel
    /// itself, and your own lights edited group by group: click a part of the wheel (or its chip) to edit it. Built for
    /// the active wheel (FX Pro or GT Neo): its drawing, groups and presets; the settings page rebuilds it on a switch.
    /// </summary>
    public class UnlockedLightsTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly WheelModel model;
        private readonly WheelView big;
        private readonly LightEngine bigEngine;
        private readonly Border simHubCard;
        private readonly TextBlock simHubText;
        private readonly Button simHubOff, simHubUse, simHubOn;
        private Button copyTo;
        private readonly TextBlock bigName, bigText, liveNote;
        private readonly StackPanel builtIn, atsrPanel, editor, groupEditor;
        private readonly WrapPanel gallery, groupChips;
        private readonly List<(string Id, Border Tile, WheelView View, LightEngine Engine)> presets = new List<(string, Border, WheelView, LightEngine)>();
        private readonly ComboBox atsrDevice;
        private readonly TextBlock atsrState, atsrMapError;
        private LedGroup group = LedGroup.Buttons;
        private readonly List<(LedStrip Strip, PatternKind Kind, RevLighting Rev)> patternStrips = new List<(LedStrip, PatternKind, RevLighting)>();
        private Button duplicate, delete;
        private TextBox nameBox;
        private StackPanel nameRow;
        private bool loading;
        private readonly LightsForCarPanel perCar;

        // the state the previews play (docs/light-states-plan.md); Driving shows the wheel's own frame while it's driven
        private CarState previewState = CarState.Driving;
        private WrapPanel stateChips;
        private static readonly CarState[] PreviewStates =
            { CarState.Driving, CarState.PitLimiter, CarState.Starting, CarState.Idle, CarState.Menu, CarState.EngineOff, CarState.Stopping };
        // the per-car pit limiter card: what's being set up, shown on the previews while it's edited
        private LimiterLook carLimiterDraft;
        private StackPanel carLimiterPanel, carLaunchPanel;
        private TextBlock scenarioStatus;
        private string launchCar = "";
        private CarState lookState = CarState.Idle;

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private int frame;

        public UnlockedLightsTab(FXProRpmSyncPlugin plugin)
        {
            UiProfiler.Lap(null);
            this.plugin = plugin;
            model = plugin.ActiveModel;
            big = new WheelView(model) { Width = 600 };
            bigEngine = new LightEngine(model);
            bool neo = model == WheelModel.GtNeo;

            // ----- Source -----
            var src = new StackPanel();
            var srcHead = new DockPanel();
            var master = Theme.Switch("Drive the wheel's lights", S.LightsEnabled, v => { S.LightsEnabled = v; Changed(); ShowSource(); });
            master.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(master, Dock.Right);
            srcHead.Children.Add(master);
            var srcText = new StackPanel();
            srcText.Children.Add(Theme.Eyebrow("Lights come from"));
            srcHead.Children.Add(srcText);
            src.Children.Add(srcHead);
            var seg = Theme.Segmented(new[] { "FX Unleashed", "ATSR-Hub", neo ? "SimHub's GT Neo device" : "SimHub device" }, (int)S.LightsFrom, i =>
            {
                S.LightsFrom = (LightsSource)i;
                Changed(); ShowSource();
                if (i == 1) RefreshAtsrDevices();
            }, out _);
            seg.Margin = new Thickness(0, 4, 0, 14);
            src.Children.Add(seg);
            var opts = new WrapPanel();
            var idle = Theme.Switch("Keep them on between sessions", S.IdleLights, v => { S.IdleLights = v; Changed(); },
                neo ? "Off: the wheel's own lights while no game runs." : "Off: SimPro's lights while no game runs.");
            idle.Margin = new Thickness(0, 0, 40, 6);
            opts.Children.Add(idle);
            var spotter = Theme.Switch("Spotter over ATSR-Hub / SimHub lights", S.SpotterOverExternal, v => { S.SpotterOverExternal = v; Changed(); },
                "On: a car alongside lights this preset's spotter alerts (the side lights) over those lights. Nothing else of the preset's alerts is drawn over them.");
            spotter.Margin = new Thickness(0, 0, 40, 6);
            opts.Children.Add(spotter);
            src.Children.Add(opts);
            liveNote = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
            src.Children.Add(liveNote);
            Children.Add(Theme.CardBox(src));
            UiProfiler.Lap("lights source card");

            // ----- SimHub's own GT Neo device (it fights our lights while both drive the wheel) -----
            var sh = new StackPanel();
            sh.Children.Add(Theme.Eyebrow("SimHub's GT Neo device", Theme.Amber));
            simHubText = Theme.Note("");
            sh.Children.Add(simHubText);
            var shButtons = new WrapPanel();
            simHubOff = Theme.Btn("Turn it off", () => SetSimHubDevice(false), primary: true, icon: "");
            simHubUse = Theme.Btn("Use SimHub's device instead", () => { S.LightsFrom = LightsSource.SimHubDevice; Changed(); Rebuild(); }, icon: "");
            simHubOn = Theme.Btn("Turn SimHub's device back on", () => SetSimHubDevice(true), icon: "");
            shButtons.Children.Add(simHubOff); shButtons.Children.Add(simHubUse); shButtons.Children.Add(simHubOn);
            sh.Children.Add(shButtons);
            simHubCard = Theme.CardBox(sh);
            simHubCard.BorderBrush = Theme.Amber;
            simHubCard.Visibility = Visibility.Collapsed;
            Children.Add(simHubCard);

            // ----- ATSR-Hub -----
            atsrPanel = new StackPanel();
            atsrPanel.Children.Add(Theme.Eyebrow("ATSR-Hub"));
            atsrPanel.Children.Add(Theme.Note("ATSR-Hub works out every light (shift lights, flags, spotter, TC/ABS, animations); the plugin sends them to the wheel. " +
                (neo ? "Pick ATSR-Hub's GT Neo setup: its LEDs are numbered like the wheel view here (hover an LED to see its number). "
                     : "Add the FX Pro in ATSR-Hub as a steering wheel (VID 0483, PID 0529) numbered like the wheel view here: hover an LED to see its number. ") +
                "While ATSR-Hub sends nothing, the plugin's own lights stay on."));
            atsrDevice = new ComboBox { Width = 300, IsEditable = true, Text = S.AtsrDevice ?? "" };
            atsrDevice.LostFocus += (s, e) => SetAtsrDevice(atsrDevice.Text);
            atsrDevice.SelectionChanged += (s, e) => { if (atsrDevice.SelectedItem is string d) SetAtsrDevice(d); };
            var deviceRow = new StackPanel { Orientation = Orientation.Horizontal };
            deviceRow.Children.Add(atsrDevice);
            var refresh = Theme.Btn("Refresh", RefreshAtsrDevices, icon: "");
            refresh.Margin = new Thickness(8, 0, 0, 0);
            deviceRow.Children.Add(refresh);
            atsrPanel.Children.Add(Theme.Field("Device", deviceRow, 130));
            var nm = Theme.Switch("Follow ATSR-Hub's brightness (night mode)", S.AtsrBrightness, v => { S.AtsrBrightness = v; Changed(); });
            nm.Margin = new Thickness(130, 0, 0, 12);
            atsrPanel.Children.Add(nm);
            var map = new TextBox { Width = 520, Text = S.AtsrMap ?? "", ToolTip = $"{model.LedCount} ATSR-Hub LED numbers, one per {model.Name} LED in its order, -1 = off. Empty = same numbers." };
            atsrMapError = new TextBlock { Foreground = Theme.Amber, Margin = new Thickness(130, -6, 0, 10), TextWrapping = TextWrapping.Wrap };
            map.LostFocus += (s, e) =>
            {
                AtsrBridge.ParseMap(map.Text, model.LedCount, out var err);
                atsrMapError.Text = err == null ? "" : "Map not used: " + err;
                if (err == null) { S.AtsrMap = map.Text.Trim(); Changed(); }
            };
            atsrPanel.Children.Add(Theme.Field("LED map", map, 130));
            atsrPanel.Children.Add(atsrMapError);
            atsrState = new TextBlock { Foreground = Theme.Text2, Margin = new Thickness(130, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            atsrPanel.Children.Add(atsrState);
            var atsrCard = Theme.CardBox(atsrPanel);
            Children.Add(atsrCard);
            UiProfiler.Lap("lights simhub+atsr cards");
            atsrPanel.Tag = atsrCard;

            // ----- Built-in: the big wheel + presets + editor -----
            builtIn = new StackPanel();
            var stageGrid = new Grid();
            stageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            stageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            stageGrid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 20, 18, 16), Margin = new Thickness(0, 0, 20, 0),
                Background = new RadialGradientBrush(Color.FromRgb(0x17, 0x0A, 0x0D), Color.FromRgb(0x07, 0x08, 0x0A)) { RadiusX = 0.7, RadiusY = 0.8 },
                BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Child = big,
            });
            big.LedClicked += led => { if (Mine != null) { group = big.GroupOf(led); ShowEditor(); } };
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            side.Children.Add(Theme.Eyebrow("Preview"));
            bigName = Theme.Title("", 24);
            side.Children.Add(bigName);
            bigText = Theme.Note("", new Thickness(0, 6, 0, 14));
            side.Children.Add(bigText);
            side.Children.Add(Theme.Eyebrow("Preview as"));
            stateChips = new WrapPanel { Margin = new Thickness(0, 2, 0, 12) };
            side.Children.Add(stateChips);
            BuildStateChips();
            nameRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            nameBox = new TextBox { Width = 240 };
            nameBox.LostFocus += (s, e) => Rename();
            nameBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) Rename(); };
            nameRow.Children.Add(new TextBlock { Text = "Name", Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            nameRow.Children.Add(nameBox);
            side.Children.Add(nameRow);
            var presetButtons = new WrapPanel();
            duplicate = Theme.Btn("Duplicate", Duplicate, primary: true, icon: "\uE8C8");
            delete = Theme.Btn("Delete", Delete, icon: "\uE74D");
            presetButtons.Children.Add(duplicate);
            presetButtons.Children.Add(delete);
            var other = neo ? WheelModel.FxPro : WheelModel.GtNeo;
            copyTo = Theme.Btn("Copy to the " + other.Name, () => CopyTo(other), icon: "\uE8C8");
            copyTo.ToolTip = $"Adds a copy of these lights to the {other.Name}'s own lights (they show there the next time it's the wheel in use)";
            presetButtons.Children.Add(copyTo);
            side.Children.Add(presetButtons);
            side.Children.Add(new TextBlock { Text = "The preview revs up and down and triggers ABS and TC now and then. While the plugin drives the wheel, it shows the wheel's lights.", TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 8, 0, 0) });
            Grid.SetColumn(side, 1);
            stageGrid.Children.Add(side);
            builtIn.Children.Add(stageGrid);

            builtIn.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 18, 0, 16) });
            builtIn.Children.Add(Theme.Eyebrow("Presets"));
            gallery = new WrapPanel();
            builtIn.Children.Add(new ScrollViewer { Content = gallery, MaxHeight = 410, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            builtIn.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 14, 0, 16) });
            UiProfiler.Lap("lights preview + gallery shell");
            builtIn.Children.Add(perCar = new LightsForCarPanel(plugin));
            UiProfiler.Lap("lights per car panel");
            Children.Add(Theme.CardBox(builtIn));

            // ----- Pit limiter lights for this car (no sim publishes them, so they're set up here once) -----
            carLimiterPanel = new StackPanel();
            Children.Add(Theme.CardBox(carLimiterPanel));
            UiProfiler.Lap("lights car limiter");
            BuildCarLimiter();

            // ----- Launch aid for this car -----
            carLaunchPanel = new StackPanel();
            Children.Add(Theme.CardBox(carLaunchPanel));
            BuildCarLaunch();

            // ----- Try the lights: scripted situations on the wheel -----
            Children.Add(Theme.CardBox(BuildScenarioCard()));

            // ----- Editor (your own) -----
            editor = new StackPanel();
            editor.Children.Add(Theme.Eyebrow("Your lights", Theme.Red));
            editor.Children.Add(Theme.Note("Pick a part of the wheel: click it on the wheel above, or here."));
            groupChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            editor.Children.Add(groupChips);
            groupEditor = new StackPanel();
            editor.Children.Add(groupEditor);
            var editorCard = Theme.CardBox(editor);
            Children.Add(editorCard);
            UiProfiler.Lap("lights editor");
            editor.Tag = editorCard;

            // ----- Buttons light while pressed (any lights source) -----
            Children.Add(Theme.CardBox(new ButtonLightsCard(plugin, Changed)));
            UiProfiler.Lap("lights button card");

            frameTimer.Tick += (s, e) => RenderFrame();
            slowTimer.Tick += (s, e) => RefreshState();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); plugin.SaveSettings(); };
            Loaded += (s, e) =>
            {
                frameTimer.Start(); slowTimer.Start();
                if (!model.HasScreen) return;
                var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
                big.Screen.Source = wheelDash ? null : DashPictures.Still(DashCache.Find(id) ?? BuiltInDashes.MustangGt3(), S.RamFor(id ?? BuiltInDashes.MustangId));
            };
            Unloaded += (s, e) => { frameTimer.Stop(); slowTimer.Stop(); if (saveTimer.IsEnabled) { saveTimer.Stop(); plugin.SaveSettings(); } };

            ShowSource();
            UiProfiler.Lap("lights timers + ShowSource");
            BuildGallery();
            UiProfiler.Lap("lights BuildGallery");
            if (S.LightsFrom == LightsSource.AtsrHub) RefreshAtsrDevices();
            UiProfiler.Lap("lights ATSR devices");
        }

        /// <summary>Everything shown depends on the source (the SimHub card, the lights state): redraw what's cheap.</summary>
        private void Rebuild() { ShowSource(); RefreshPresets(); }

        private void CopyTo(WheelModel other)
        {
            var from = S.ActiveLights;
            S.CopyLightsTo(other, from);
            plugin.SaveSettings();
            copyTo.Content = "Copied to the " + other.Name + " ✓";
        }

        private void SetSimHubDevice(bool on)
        {
            if (!SimHubGtNeoDevice.SetEnabled(plugin.PluginManager, on))
            {
                MessageBox.Show(Window.GetWindow(this), "SimHub didn't let the plugin switch its GT Neo device " + (on ? "on" : "off") + ". Do it in SimHub > Devices > Simagic GT Neo.",
                    "FX Unleashed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            S.TurnedOffSimHubDevice = !on;
            if (on && S.LightsFrom != LightsSource.SimHubDevice) { S.LightsFrom = LightsSource.SimHubDevice; Usb?.SettingsChanged(); }
            plugin.SaveSettings();
            RefreshState();
        }

        private void Changed()
        {
            if (loading) return;
            Usb?.SettingsChanged();
            plugin.Reapply(); // the current car's rev lights come from the preset when it has no data
            saveTimer.Stop();
            saveTimer.Start();
        }

        private void ShowSource()
        {
            bool atsr = S.LightsFrom == LightsSource.AtsrHub;
            ((Border)atsrPanel.Tag).Visibility = atsr && S.LightsEnabled ? Visibility.Visible : Visibility.Collapsed;
            builtIn.Opacity = S.LightsEnabled ? 1 : 0.5;
            RefreshState();
        }

        private string limiterCar = "";

        private void RefreshState()
        {
            // the per-car limiter card follows the car being driven (not while something is being edited)
            if (carLimiterDraft == null && (plugin.DashCarKey ?? "") != limiterCar) { limiterCar = plugin.DashCarKey ?? ""; BuildCarLimiter(); }
            if ((plugin.DashCarKey ?? "") != launchCar) { launchCar = plugin.DashCarKey ?? ""; BuildCarLaunch(); }
            if (scenarioStatus != null)
            {
                var playing = Usb?.ScenarioNow;
                scenarioStatus.Text = playing != null ? $"Playing \"{playing.Title}\"  ·  {Usb.ScenarioElapsed:0} of {playing.Seconds:0} s  ·  {Usb.ScenarioNote}"
                                    : Usb?.Active == true ? "Pick one to play on the wheel." : "The wheel isn't connected in USB mode right now (the Wheel tab says why).";
            }
            var u = Usb;
            liveNote.Text = !S.LightsEnabled ? "The wheel shows SimPro's lights."
                          : u?.Active == true ? "On the wheel now: " + u.LightsState + "."
                          : S.LightsFrom == LightsSource.AtsrHub ? "ATSR-Hub's lights show while the plugin drives the wheel; the preset below fills in while it sends nothing."
                          : S.LightsFrom == LightsSource.SimHubDevice && model == WheelModel.GtNeo ? "SimHub's own GT Neo device drives the lights (SimHub > Devices > Simagic GT Neo); the plugin leaves them alone. " +
                                                                        "Sleep and the brightness limit don't apply to it."
                          : S.LightsFrom == LightsSource.SimHubDevice ? "SimHub's LED profile shows while the plugin drives the wheel: add \"FX Pro wheel (USB mode)\" (brand FX Unleashed) in SimHub > Devices " +
                                                                        "and set up its lights there (21 side + rev lights, 12 buttons, 5 encoders, or 38 individual LEDs). The preset below fills in while it sends nothing." : "";
            RefreshSimHubCard();
            if (S.LightsFrom == LightsSource.AtsrHub)
                atsrState.Text = u?.Active != true ? "Not sending: the plugin isn't driving the wheel now." : "Lights now: " + u.LightsState + ".";
        }

        /// <summary>GT Neo: offer to turn SimHub's own device off while it would fight our lights, and back on after.</summary>
        private void RefreshSimHubCard()
        {
            if (model != WheelModel.GtNeo) return;
            bool? on = SimHubGtNeoDevice.Enabled(plugin.PluginManager);
            bool ours = S.LightsEnabled && S.LightsFrom != LightsSource.SimHubDevice;
            bool fight = ours && on == true;
            bool offerOn = !ours && S.TurnedOffSimHubDevice && on == false;
            simHubCard.Visibility = fight || offerOn ? Visibility.Visible : Visibility.Collapsed;
            simHubText.Text = fight ? "SimHub's own GT Neo device is on as well, so the lights flicker between its colours and these. Turn it off, or let it drive the lights instead."
                                    : "The plugin turned SimHub's own GT Neo device off. Turn it back on to let SimHub drive the lights.";
            simHubOff.Visibility = simHubUse.Visibility = fight ? Visibility.Visible : Visibility.Collapsed;
            simHubOn.Visibility = offerOn ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---------- Presets ----------

        /// <summary>The selected lights when they're the user's own (editable), else null.</summary>
        private LightProfile Mine => S.UserLights.FirstOrDefault(p => p.Id == S.LightPreset);

        private void BuildGallery()
        {
            gallery.Children.Clear();
            presets.Clear();
            foreach (var p in LightPresets.For(model)) AddPreset(p);
            foreach (var p in S.UserLights) AddPreset(p);
            // "+": a new one, from the selected lights
            var plus = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            plus.Children.Add(new TextBlock { Text = "\uE710", FontFamily = Theme.Icons, FontSize = 30, Foreground = Theme.Red, HorizontalAlignment = HorizontalAlignment.Center });
            plus.Children.Add(new TextBlock { Text = "New lights", FontFamily = Theme.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
            plus.Children.Add(new TextBlock { Text = "from the selected ones", Foreground = Theme.Text3, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var tile = Theme.Tile(new Border { Height = 146, Child = plus }, 222, Duplicate, "A copy of the selected lights to edit");
            tile.BorderBrush = Theme.Line2;
            gallery.Children.Add(tile);
            RefreshPresets();
        }

        private void AddPreset(LightProfile p)
        {
            var view = new WheelView(model, glow: false, lite: true) { Width = 196 };
            var body = new StackPanel();
            body.Children.Add(new Border { Background = Theme.B("#07080A"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 10, 8, 8), Child = view });
            var name = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            if (!LightPresets.IsBuiltIn(p.Id))
            {
                var tag = new TextBlock { Text = "YOURS", FontFamily = Theme.Display, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Theme.Red, VerticalAlignment = VerticalAlignment.Center };
                DockPanel.SetDock(tag, Dock.Right);
                name.Children.Add(tag);
            }
            name.Children.Add(new TextBlock { Text = p.Name, FontFamily = Theme.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            body.Children.Add(name);
            var id = p.Id;
            var tile = Theme.Tile(body, 222, () => { S.LightPreset = id; Changed(); RefreshPresets(); }, p.Description);
            presets.Add((id, tile, view, new LightEngine(model)));
            gallery.Children.Add(tile);
        }

        /// <summary>A copy of the selected lights, selected, to edit.</summary>
        private void Duplicate()
        {
            var from = S.ActiveLights;
            var copy = from.Clone();
            copy.Id = LightPresets.NewUserId();
            string baseName = from.Name + " copy";
            copy.Name = baseName;
            for (int n = 2; S.UserLights.Any(x => x.Name == copy.Name); n++) copy.Name = baseName + " " + n;
            copy.Description = "Based on " + from.Name;
            S.UserLights.Add(copy);
            S.LightPreset = copy.Id;
            Changed();
            BuildGallery();
        }

        private void Delete()
        {
            var mine = Mine;
            if (mine == null) return;
            if (MessageBox.Show(Window.GetWindow(this), $"Delete \"{mine.Name}\"?", "FX Unleashed", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            int at = S.UserLights.IndexOf(mine);
            S.UserLights.Remove(mine);
            S.LightPreset = at > 0 ? S.UserLights[at - 1].Id : LightPresets.For(model)[0].Id;
            Changed();
            BuildGallery();
        }

        private void Rename()
        {
            var mine = Mine;
            var name = nameBox.Text.Trim();
            if (mine == null || name.Length == 0 || name == mine.Name) return;
            mine.Name = name;
            Changed();
            BuildGallery();
        }

        private void RefreshPresets()
        {
            foreach (var (id, tile, _, _) in presets) Theme.Select(tile, id == S.LightPreset);
            var p = S.ActiveLights;
            var mine = Mine;
            bigName.Text = p.Name;
            bigText.Text = p.Description ?? "";
            nameRow.Visibility = mine != null ? Visibility.Visible : Visibility.Collapsed;
            nameBox.Text = mine?.Name ?? "";
            delete.Visibility = mine != null ? Visibility.Visible : Visibility.Collapsed;
            duplicate.Content = mine != null ? "Duplicate" : "Duplicate to edit";
            copyTo.Content = "Copy to the " + (model == WheelModel.GtNeo ? WheelModel.FxPro : WheelModel.GtNeo).Name;
            ((Border)editor.Tag).Visibility = mine != null ? Visibility.Visible : Visibility.Collapsed;
            if (mine != null) ShowEditor(); else big.Highlight(null);
            perCar?.Refresh(force: true);
        }

        private void RenderFrame()
        {
            double t = clock.Elapsed.TotalSeconds;
            var sim = SimLap.Values(t);
            var live = previewState == CarState.Driving && carLimiterDraft == null && Usb?.Active == true && S.LightsEnabled ? Usb.LastFrame : null;
            var (pv, moment) = PreviewMoment(t, sim);
            big.Show(live ?? bigEngine.Render(S.ActiveLights, pv, null, t, false, moment));
            // the pattern previews: a sweep up to the shift point in the preset's colours
            bool blinkOn = (int)(t * 8) % 2 == 0;
            foreach (var (strip, kind, rev) in patternStrips)
            {
                var r = rev.Clone(); r.Pattern = kind;
                strip.Layout = r.Layout();
                strip.Render(LedStrip.SweepRpm(t, r.StartPercent / 100), blinkOn);
            }
            // the small ones at half the rate
            if (++frame % 2 != 0) return;
            foreach (var (id, _, view, engine) in presets)
            {
                var p = S.UserLights.FirstOrDefault(x => x.Id == id) ?? LightPresets.Find(id);
                view.Show(p == null ? null : engine.Render(p, pv, null, t, false, moment));
            }
        }

        /// <summary>The simulated values and moment for the state being previewed (the start-up and shutdown loop).</summary>
        private (DashValues, LightMoment) PreviewMoment(double t, DashValues sim)
        {
            var state = carLimiterDraft != null ? CarState.PitLimiter : previewState;
            double progress = 0;
            if (state == CarState.Starting || state == CarState.Stopping)
            {
                // play it, hold the end for a moment, again
                double len = state == CarState.Starting ? CarStateTracker.StartSeconds : CarStateTracker.StopSeconds;
                progress = Math.Min(1, t % (len + 1.2) / len);
            }
            DashValues v = sim;
            if (state == CarState.Idle) v = new DashValues();
            else if (state == CarState.Menu || state == CarState.EngineOff) { v = SimLap.Values(0); v.Rpm = 0; v.AbsActive = v.TcActive = v.SpotterLeft = v.SpotterRight = false; v.InMenu = state == CarState.Menu; }
            else if (state == CarState.PitLimiter) { v.Rpm = 3400; v.PitLimiter = true; v.AbsActive = v.TcActive = false; }
            else if (state == CarState.Starting) v.Rpm = 900;
            return (v, LightMoment.Of(state, progress, carLimiterDraft ?? plugin.CarLimiterFor(plugin.DashCarKey)));
        }

        private void BuildStateChips()
        {
            stateChips.Children.Clear();
            foreach (var st in PreviewStates)
            {
                var ss = st;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(0, 0, 6, 6), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1), BorderBrush = st == previewState ? Theme.Red : Theme.Line2, Background = st == previewState ? Theme.RedWash : Theme.Raised,
                    Child = new TextBlock { Text = CarStateTracker.Name(st), FontSize = 11.5, Foreground = st == previewState ? Theme.Text : Theme.Text2 },
                };
                chip.MouseLeftButtonUp += (s2, e2) =>
                {
                    previewState = ss;
                    if (ss == CarState.Idle || ss == CarState.Menu || ss == CarState.EngineOff) lookState = ss;
                    BuildStateChips();
                    if (Mine != null) ShowEditor();
                };
                stateChips.Children.Add(chip);
            }
        }

        // ---------- Pit limiter lights per car ----------

        private void BuildCarLimiter()
        {
            var panel = carLimiterPanel;
            panel.Children.Clear();
            panel.Children.Add(Theme.Eyebrow("Pit limiter lights for this car"));
            string key = plugin.DashCarKey, name = plugin.CurrentCarNameForDash;
            panel.Children.Add(Theme.Note("Cars whose dash has pit limiter lights in the game's own data (AMS2 so far) show those. For any other car, " +
                "set it up once: pick the pattern and colours, check it on the wheel above (and on the real wheel in the pit lane), then save it " +
                "for the car. Cars without either use the light preset's. The same on both wheels."));
            var saved = plugin.LimiterFor(key);
            var fromGame = saved == null ? plugin.GameLimiterFor(key) : null;
            if (key == null)
                panel.Children.Add(new TextBlock { Text = "Start a game to set up the car you're driving.", Foreground = Theme.Text2, Margin = new Thickness(0, 4, 0, 10) });
            else
            {
                var look = carLimiterDraft ?? (saved ?? fromGame ?? S.ActiveLights.Limiter ?? new LimiterLook()).Clone();
                panel.Children.Add(Theme.Title((name ?? key) + (saved != null ? "  ·  saved" : fromGame != null ? "  ·  from the game" : ""), 16));
                if (fromGame != null && carLimiterDraft == null)
                    panel.Children.Add(Theme.Note($"This car's own pit limiter lights, read from {plugin.CurrentGameLights?.Label ?? "the game"}'s dash. " +
                        "Change them below and save if you'd rather have something else."));
                panel.Children.Add(new Border { Height = 8 });
                panel.Children.Add(LimiterEditor(look, () => { carLimiterDraft = look; }));
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
                row.Children.Add(Theme.Btn("Save for this car", () => { look.FromGame = false; plugin.SetCarLimiter(key, look); carLimiterDraft = null; BuildCarLimiter(); }, primary: true));
                if (carLimiterDraft != null) row.Children.Add(Theme.Btn("Undo changes", () => { carLimiterDraft = null; BuildCarLimiter(); }));
                if (saved != null) row.Children.Add(Theme.Btn("Remove this car's", () => { plugin.SetCarLimiter(key, null); carLimiterDraft = null; BuildCarLimiter(); }));
                panel.Children.Add(row);
                panel.Children.Add(Theme.Note(carLimiterDraft != null ? "The previews above show it while you edit." : "Change anything above to preview it."));
            }
            var all = S.CarLimiters ?? new Dictionary<string, LimiterLook>();
            if (all.Count > 0)
            {
                var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var kv in all.OrderBy(k => k.Key))
                {
                    var carKey = kv.Key;
                    var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var remove = Theme.Btn("Remove", () => { plugin.SetCarLimiter(carKey, null); BuildCarLimiter(); });
                    DockPanel.SetDock(remove, Dock.Right);
                    line.Children.Add(remove);
                    line.Children.Add(new TextBlock { Text = carKey + "  ·  " + LimiterLook.StyleName(kv.Value.Style), Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center });
                    list.Children.Add(line);
                }
                panel.Children.Add(new Expander { Header = $"Every car with its own ({all.Count})", Content = list, Margin = new Thickness(0, 6, 0, 0) });
            }
        }

        /// <summary>One button per scripted situation (LightScenarios): plays it on the wheel and says what to look for.</summary>
        private FrameworkElement BuildScenarioCard()
        {
            var panel = new StackPanel();
            panel.Children.Add(Theme.Eyebrow("Try the lights on the wheel"));
            panel.Children.Add(Theme.Note("Plays a scripted situation on the wheel without a game: pit lane, a standing start, a car alongside, every alert, the engine states, " +
                "cars with mirrored or blinking-out lights. It goes through the same code as a real session, with your selected preset (an alert or extra a scenario shows is " +
                "switched on in a copy for the test). Each one says what you should see. It ends by itself."));
            scenarioStatus = new TextBlock { Foreground = Theme.Text2, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap };
            panel.Children.Add(scenarioStatus);
            var stop = Theme.Btn("Stop", () => { Usb?.StopScenario(); RefreshState(); });
            stop.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(stop);
            foreach (var sc in LightScenarios.All)
            {
                var id = sc.Id;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                var play = Theme.Btn($"Play ({sc.Seconds:0} s)", () => { if (Usb != null) Usb.PlayScenario(id); RefreshState(); }, primary: true);
                DockPanel.SetDock(play, Dock.Right);
                play.VerticalAlignment = VerticalAlignment.Top;
                play.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(play);
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = sc.Title, FontWeight = FontWeights.SemiBold, Foreground = Theme.Text });
                text.Children.Add(new TextBlock { Text = sc.Expect, Foreground = Theme.Text2, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                row.Children.Add(text);
                panel.Children.Add(row);
            }
            return panel;
        }

        /// <summary>What to hold at a standing start in the car being driven (the launch aid, UsbSettings.CarLaunch).</summary>
        private void BuildCarLaunch()
        {
            var panel = carLaunchPanel;
            panel.Children.Clear();
            panel.Children.Add(Theme.Eyebrow("Launch aid for this car"));
            panel.Children.Add(Theme.Note("At a standing start in first gear (under 30 km/h, not in the pit lane) the rev bar shows a pointer against the target: amber below it, " +
                "green on it, red above. Switch it on in your lights (Rev bar extras), then set what to hold here. With no target saved it aims at 70% of the car's max revs."));
            string key = plugin.DashCarKey, name = plugin.CurrentCarNameForDash;
            if (key == null)
            {
                panel.Children.Add(new TextBlock { Text = "Start a game to set up the car you're driving.", Foreground = Theme.Text2, Margin = new Thickness(0, 4, 0, 10) });
                return;
            }
            var saved = plugin.LaunchFor(key);
            var t = (saved ?? new LaunchTarget()).Clone();
            panel.Children.Add(Theme.Title((name ?? key) + (saved != null ? "  ·  saved" : ""), 16));
            var mode = new ComboBox { Width = 240 };
            foreach (var (m, label) in new[] { (LaunchMode.Rpm, "Revs"), (LaunchMode.Throttle, "Throttle"), (LaunchMode.Clutch, "Clutch bite point") })
                mode.Items.Add(new ComboBoxItem { Content = label, Tag = m });
            mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => (LaunchMode)i.Tag == t.Mode);
            var value = new TextBox { Width = 90, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var unit = new TextBlock { Foreground = Theme.Text2, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            bool loadingBox = false;
            void Show()
            {
                loadingBox = true;
                value.Text = t.Mode == LaunchMode.Rpm ? (t.Rpm > 0 ? t.Rpm.ToString() : "") : (t.Mode == LaunchMode.Throttle ? t.ThrottlePercent : t.BitePercent).ToString();
                unit.Text = t.Mode == LaunchMode.Rpm ? "rpm" + (t.Rpm > 0 ? "" : "  (70% of max)") : "%";
                loadingBox = false;
            }
            mode.SelectionChanged += (s, e) => { if (mode.SelectedItem is ComboBoxItem i) { t.Mode = (LaunchMode)i.Tag; Show(); } };
            value.TextChanged += (s, e) =>
            {
                if (loadingBox) return;
                int.TryParse(value.Text, out var v);
                if (t.Mode == LaunchMode.Rpm) t.Rpm = Math.Max(0, Math.Min(30000, v));
                else if (t.Mode == LaunchMode.Throttle) t.ThrottlePercent = Math.Max(0, Math.Min(100, v));
                else t.BitePercent = Math.Max(0, Math.Min(100, v));
            };
            Show();
            panel.Children.Add(Theme.Field("Watch", mode));
            var valueRow = new StackPanel { Orientation = Orientation.Horizontal };
            valueRow.Children.Add(value);
            valueRow.Children.Add(unit);
            panel.Children.Add(Theme.Field("Target", valueRow));
            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 6) };
            row.Children.Add(Theme.Btn("Use the revs now", () =>
            {
                var rpm = plugin.PluginManager?.LastData?.NewData?.Rpms ?? 0;
                if (rpm > 300) { t.Mode = LaunchMode.Rpm; t.Rpm = (int)(Math.Round(rpm / 50) * 50); mode.SelectedItem = mode.Items.Cast<ComboBoxItem>().First(i => (LaunchMode)i.Tag == LaunchMode.Rpm); Show(); }
            }));
            row.Children.Add(Theme.Btn("Save for this car", () => { plugin.SetCarLaunch(key, t); BuildCarLaunch(); }, primary: true));
            if (saved != null) row.Children.Add(Theme.Btn("Remove this car's", () => { plugin.SetCarLaunch(key, null); BuildCarLaunch(); }));
            panel.Children.Add(row);
            var all = S.CarLaunch ?? new Dictionary<string, LaunchTarget>();
            if (all.Count > 0)
            {
                var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (var kv in all.OrderBy(k => k.Key))
                {
                    var carKey = kv.Key;
                    var line = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var remove = Theme.Btn("Remove", () => { plugin.SetCarLaunch(carKey, null); BuildCarLaunch(); });
                    DockPanel.SetDock(remove, Dock.Right);
                    line.Children.Add(remove);
                    string what = kv.Value.Mode == LaunchMode.Rpm ? (kv.Value.Rpm > 0 ? kv.Value.Rpm + " rpm" : "70% of max revs")
                                : kv.Value.Mode + " " + (kv.Value.Mode == LaunchMode.Throttle ? kv.Value.ThrottlePercent : kv.Value.BitePercent) + "%";
                    line.Children.Add(new TextBlock { Text = carKey + "  ·  " + what, Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center });
                    list.Children.Add(line);
                }
                panel.Children.Add(new Expander { Header = $"Every car with a target ({all.Count})", Content = list, Margin = new Thickness(0, 6, 0, 0) });
            }
        }

        /// <summary>Style, colours, speed and "whole wheel" for a limiter look; `changed` after each edit.</summary>
        private FrameworkElement LimiterEditor(LimiterLook look, Action changed)
        {
            var box = new StackPanel();
            var style = new ComboBox { Width = 240 };
            foreach (LimiterStyle st in Enum.GetValues(typeof(LimiterStyle)))
                if (st != LimiterStyle.CarPattern || look.Pattern != null) // the car's own needs its pattern from the game
                    style.Items.Add(new ComboBoxItem { Content = LimiterLook.StyleName(st), Tag = st });
            style.SelectedItem = style.Items.Cast<ComboBoxItem>().First(i => (LimiterStyle)i.Tag == look.Style);
            style.SelectionChanged += (s2, e2) => { if (style.SelectedItem is ComboBoxItem i) { look.Style = (LimiterStyle)i.Tag; changed(); } };
            box.Children.Add(Theme.Field("Pattern", style));
            if (look.Colors == null || look.Colors.Count == 0) look.Colors = new List<string> { "#0040FF", "#000000" };
            if (look.Colors.Count < 2) look.Colors.Add("#000000");
            var cols = new WrapPanel();
            cols.Children.Add(new ColourField(look.Colors[0], hex => { look.Colors[0] = hex; changed(); }) { Margin = new Thickness(0, 0, 10, 4) });
            cols.Children.Add(new ColourField(look.Colors[1], hex => { look.Colors[1] = hex; changed(); }) { Margin = new Thickness(0, 0, 10, 4), ToolTip = "The second colour (halves / chequered). Black = dark." });
            box.Children.Add(Theme.Field("Colours", cols));
            box.Children.Add(Theme.Field("Speed", Theme.SliderField(0.5, 8, look.Hz, 0.1, v => $"{v:0.0} per second", v => { look.Hz = v; changed(); })));
            var whole = Theme.Switch("Over the whole wheel, not just the rev bar", look.WholeWheel, v => { look.WholeWheel = v; changed(); });
            whole.Margin = new Thickness(170, 0, 0, 8);
            box.Children.Add(whole);
            return box;
        }

        // ---------- Editor ----------

        private string GroupName(LedGroup g)
        {
            if (model != WheelModel.FxPro) return model.GroupName(g);
            switch (g)
            {
                case LedGroup.Buttons: return "Buttons";
                case LedGroup.Encoders: return "Encoders";
                case LedGroup.SideLeft: return "Left lights";
                case LedGroup.SideRight: return "Right lights";
                case LedGroup.ButtonsLeft: return "Left buttons";
                case LedGroup.ButtonsRight: return "Right buttons";
                default: return "Rev lights";
            }
        }

        private void ShowEditor()
        {
            var p = Mine;
            if (p == null) return;
            if (!model.Has(group)) group = model.Groups[0];
            big.Highlight(big.LedsOf(group));
            groupChips.Children.Clear();
            foreach (LedGroup g in model.Groups)
            {
                var gg = g;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1), BorderBrush = g == group ? Theme.Red : Theme.Line2, Background = g == group ? Theme.RedWash : Theme.Raised,
                    Child = new TextBlock { Text = GroupName(g), FontFamily = Theme.Display, FontSize = 13, Foreground = g == group ? Theme.Text : Theme.Text2 },
                };
                chip.MouseLeftButtonUp += (s, e) => { group = gg; ShowEditor(); };
                groupChips.Children.Add(chip);
            }

            loading = true;
            groupEditor.Children.Clear();
            patternStrips.Clear();
            var l = p.Group(group);
            var effect = new ComboBox { Width = 240 };
            // shift lights only on the rev bar, setting levels only on the encoders
            var effects = Enum.GetValues(typeof(LightEffect)).Cast<LightEffect>()
                .Where(x => (x != LightEffect.Rpm || group == LedGroup.Rev) && (x != LightEffect.Levels || (group == LedGroup.Encoders && (model.HasLevels || model.GaugeRings))));
            foreach (var x in effects) effect.Items.Add(new ComboBoxItem { Content = EffectName(x), Tag = x });
            effect.SelectedItem = effect.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (LightEffect)i.Tag == l.Effect) ?? effect.Items[0];
            var details = new StackPanel();
            Action fill = () =>
            {
                details.Children.Clear();
                if (l.Effect == LightEffect.Rpm) { BuildRevEditor(details, p.Rev); return; }
                if (l.Effect == LightEffect.Levels) { BuildLevelsEditor(details, l); return; }
                if (l.Effect == LightEffect.Off) return;
                if (l.Effect != LightEffect.Rainbow && l.Effect != LightEffect.RainbowBreathe)
                    details.Children.Add(Theme.Field("Colours", ColourList(l.Colors, 1, 4)));
                if (l.Effect != LightEffect.Solid)
                    details.Children.Add(Theme.Field("Speed", Theme.SliderField(0.5, 15, l.Period, 0.1, v => $"{v:0.0} s per cycle", v => { if (!loading) { l.Period = v; Changed(); } })));
                details.Children.Add(Theme.Field("Brightness", Theme.SliderField(5, 100, l.Brightness, 1, v => $"{v:0}%", v => { if (!loading) { l.Brightness = (int)v; Changed(); } })));
            };
            effect.SelectionChanged += (s, e) => { if (effect.SelectedItem is ComboBoxItem i) { l.Effect = (LightEffect)i.Tag; Changed(); fill(); } };
            groupEditor.Children.Add(Theme.Title(GroupName(group), 18));
            groupEditor.Children.Add(new Border { Height = 12 });
            groupEditor.Children.Add(Theme.Field("Effect", effect));
            groupEditor.Children.Add(details);
            fill();

            // Global brightness + alerts
            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 16) });
            groupEditor.Children.Add(Theme.Field("All lights", Theme.SliderField(1, 90, p.MaxBrightness, 1, v => $"{v:0} / 90", v => { if (!loading) { p.MaxBrightness = (int)v; Changed(); } })));
            BuildStateEditor(p);
            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 16) });
            groupEditor.Children.Add(Theme.Eyebrow("Alerts"));
            groupEditor.Children.Add(Theme.Note("While one is on, its lights show it over everything else. Higher in the list wins a light: use the arrows to reorder."));
            if (p.AddMissingAlerts()) { saveTimer.Stop(); saveTimer.Start(); } // alerts added since these lights were saved
            alertList = new StackPanel();
            groupEditor.Children.Add(alertList);
            BuildAlerts(p);
            var add = Theme.Btn("Add a custom alert", () =>
            {
                p.Alerts.Add(new AlertRule { Trigger = AlertTrigger.Custom, Name = "Custom alert", Color = "#FF00C0", BlinkHz = 2, Groups = { LedGroup.Encoders } });
                Changed(); BuildAlerts(p);
            }, icon: "");
            add.Margin = new Thickness(0, 6, 0, 0);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            groupEditor.Children.Add(add);
            loading = false;
        }

        private StackPanel alertList;

        /// <summary>
        /// Your own lights per car state: how they look with no game, in a menu, with the engine off; the engine start and
        /// stop animations; the pit limiter lights; the rev bar tint while driving.
        /// </summary>
        private void BuildStateEditor(LightProfile p)
        {
            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 10, 0, 16) });
            groupEditor.Children.Add(Theme.Eyebrow("When the car isn't driving"));
            groupEditor.Children.Add(Theme.Note("Pick the moment; the preview above plays it."));
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var st in new[] { CarState.Idle, CarState.Menu, CarState.EngineOff })
            {
                var ss = st;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 5, 14, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1), BorderBrush = st == lookState ? Theme.Red : Theme.Line2, Background = st == lookState ? Theme.RedWash : Theme.Raised,
                    Child = new TextBlock { Text = CarStateTracker.Name(st), FontFamily = Theme.Display, FontSize = 13, Foreground = st == lookState ? Theme.Text : Theme.Text2 },
                };
                chip.MouseLeftButtonUp += (s2, e2) => { lookState = ss; previewState = ss; BuildStateChips(); ShowEditor(); };
                chips.Children.Add(chip);
            }
            groupEditor.Children.Add(chips);

            // edits go into the profile's own look for the state (created from the default on the first change)
            StateLook Look() { if (p.Looks == null) p.Looks = new Dictionary<CarState, StateLook>(); if (!p.Looks.TryGetValue(lookState, out var l) || l == null) p.Looks[lookState] = l = p.LookFor(lookState).Clone(); return l; }
            var look = p.LookFor(lookState);
            groupEditor.Children.Add(Theme.Field("Brightness", Theme.SliderField(0, 100, look.Brightness, 1, v => v < 0.5 ? "dark" : $"{v:0}% of the lights", v => { if (!loading) { Look().Brightness = (int)v; Changed(); } })));
            var effect = new ComboBox { Width = 240 };
            effect.Items.Add(new ComboBoxItem { Content = "As they are while driving", Tag = null });
            foreach (var x in Enum.GetValues(typeof(LightEffect)).Cast<LightEffect>().Where(x => x != LightEffect.Rpm && x != LightEffect.Levels && x != LightEffect.Off))
                effect.Items.Add(new ComboBoxItem { Content = EffectName(x), Tag = x });
            effect.SelectedItem = effect.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (LightEffect?)i.Tag == look.Effect) ?? effect.Items[0];
            var lookDetails = new StackPanel();
            Action fillLook = () =>
            {
                lookDetails.Children.Clear();
                var l = p.LookFor(lookState);
                if (l.Effect == null) return;
                if (l.Colors == null || l.Colors.Count == 0) Look().Colors = (p.Groups.Values.FirstOrDefault(g => g.Effect != LightEffect.Off && g.Effect != LightEffect.Rpm)?.Colors ?? new List<string> { "#FFFFFF" }).ToList();
                if (l.Effect != LightEffect.Rainbow && l.Effect != LightEffect.RainbowBreathe)
                    lookDetails.Children.Add(Theme.Field("Colours", ColourList(Look().Colors, 1, 4)));
                lookDetails.Children.Add(Theme.Field("Speed", Theme.SliderField(0.5, 20, l.Period, 0.1, v => $"{v:0.0} s per cycle", v => { if (!loading) { Look().Period = v; Changed(); } })));
            };
            effect.SelectionChanged += (s2, e2) => { if (effect.SelectedItem is ComboBoxItem i) { Look().Effect = (LightEffect?)i.Tag; Changed(); fillLook(); } };
            groupEditor.Children.Add(Theme.Field("Effect", effect));
            groupEditor.Children.Add(lookDetails);
            fillLook();
            var rev = Theme.Switch("The rev bar joins in", look.RevBar, v => { Look().RevBar = v; Changed(); }, "Off: the rev bar stays dark until you drive.");
            rev.Margin = new Thickness(170, 0, 0, 10);
            groupEditor.Children.Add(rev);

            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 16) });
            groupEditor.Children.Add(Theme.Eyebrow("Engine start and stop"));
            var start = new ComboBox { Width = 240 };
            foreach (var (st, label) in new[] { (StartupStyle.None, "Nothing"), (StartupStyle.Sweep, "A wave from the middle"), (StartupStyle.SelfTest, "Rev bar self-test"), (StartupStyle.Ignite, "Double flash") })
                start.Items.Add(new ComboBoxItem { Content = label, Tag = st });
            start.SelectedItem = start.Items.Cast<ComboBoxItem>().First(i => (StartupStyle)i.Tag == p.Startup);
            start.SelectionChanged += (s2, e2) => { if (start.SelectedItem is ComboBoxItem i) { p.Startup = (StartupStyle)i.Tag; Changed(); previewState = CarState.Starting; BuildStateChips(); } };
            groupEditor.Children.Add(Theme.Field("Engine start", start));
            var stop = new ComboBox { Width = 240 };
            foreach (var (st, label) in new[] { (ShutdownStyle.None, "Nothing"), (ShutdownStyle.Fade, "Fade out"), (ShutdownStyle.Collapse, "Close in to the middle") })
                stop.Items.Add(new ComboBoxItem { Content = label, Tag = st });
            stop.SelectedItem = stop.Items.Cast<ComboBoxItem>().First(i => (ShutdownStyle)i.Tag == p.Shutdown);
            stop.SelectionChanged += (s2, e2) => { if (stop.SelectedItem is ComboBoxItem i) { p.Shutdown = (ShutdownStyle)i.Tag; Changed(); previewState = CarState.Stopping; BuildStateChips(); } };
            groupEditor.Children.Add(Theme.Field("Engine stop", stop));

            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 16) });
            groupEditor.Children.Add(Theme.Eyebrow("Pit limiter"));
            groupEditor.Children.Add(Theme.Note("For cars without their own (set in the card for this car above)."));
            if (p.Limiter == null) p.Limiter = new LimiterLook();
            groupEditor.Children.Add(LimiterEditor(p.Limiter, () => { if (!loading) { Changed(); previewState = CarState.PitLimiter; BuildStateChips(); } }));

            groupEditor.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 6, 0, 16) });
            groupEditor.Children.Add(Theme.Field("Rev bar tint", Theme.SliderField(0, 30, p.RevTint, 1, v => v < 0.5 ? "off" : $"{v:0}% glow in the theme colour",
                v => { if (!loading) { p.RevTint = (int)v; Changed(); } })));

            groupEditor.Children.Add(Theme.Eyebrow("While driving"));
            var still = Theme.Switch("Hold still", p.StillWhileDriving, v => { if (!loading) { p.StillWhileDriving = v; Changed(); previewState = CarState.Driving; BuildStateChips(); } },
                "On: the buttons and encoders show one still frame of their effect instead of animating. Off: they keep moving.");
            still.Margin = new Thickness(170, 0, 0, 10);
            groupEditor.Children.Add(still);
            var sides = Theme.Switch("Side lights dark", p.SidesDarkWhileDriving, v => { if (!loading) { p.SidesDarkWhileDriving = v; Changed(); previewState = CarState.Driving; BuildStateChips(); } },
                "On: the lights beside the rev bar stay off while you drive and only light for alerts (spotter, flags).");
            sides.Margin = new Thickness(170, 0, 0, 10);
            groupEditor.Children.Add(sides);

            groupEditor.Children.Add(Theme.Eyebrow("Rev bar extras"));
            groupEditor.Children.Add(Theme.Note("The rev bar shows these instead of the shift lights while they last; the shift lights win again near the shift point."));
            if (p.Extras == null) p.Extras = new RevExtrasOptions();
            void Extra(string label, string hint, bool value, Action<bool> set)
            {
                var sw = Theme.Switch(label, value, v => { if (!loading) { set(v); Changed(); } }, hint);
                sw.Margin = new Thickness(170, 0, 0, 10);
                groupEditor.Children.Add(sw);
            }
            Extra("Pit speed bar", "In the pit lane, moving: a pointer for your speed against the limit (the middle is the limit, red over it). The limit comes from the game, or is learned from the pit limiter.",
                p.Extras.PitSpeed, v => p.Extras.PitSpeed = v);
            Extra("Lift and coast (LMU)", "The bar fills from both ends with the lift-and-coast progress.", p.Extras.LiftCoast, v => p.Extras.LiftCoast = v);
            Extra("Fuel while refuelling", "Stopped with fuel going in, the bar shows the tank filling.", p.Extras.Refuel, v => p.Extras.Refuel = v);
            Extra("Brake bias change", "After you change the bias, a pointer shows how far it moved from where it started.", p.Extras.BrakeBias, v => p.Extras.BrakeBias = v);
            Extra("Launch aid", "Standing start in first gear: a pointer for revs (or throttle, or clutch) against this car's target, set in the launch card above. Amber below it, green on it, red above.",
                p.Extras.Launch, v => p.Extras.Launch = v);
        }

        private void BuildAlerts(LightProfile p)
        {
            bool was = loading;
            loading = true;
            alertList.Children.Clear();
            for (int i = 0; i < p.Alerts.Count; i++) alertList.Children.Add(AlertRow(p, i));
            loading = was;
        }

        private FrameworkElement AlertRow(LightProfile p, int index)
        {
            var a = p.Alerts[index];
            var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            var row = new WrapPanel();
            outer.Children.Add(row);

            // priority: up / down
            Action<int> move = d =>
            {
                int to = index + d;
                if (to < 0 || to >= p.Alerts.Count) return;
                p.Alerts.RemoveAt(index);
                p.Alerts.Insert(to, a);
                Changed(); BuildAlerts(p);
            };
            var up = Small("", () => move(-1), "Higher priority");
            var down = Small("", () => move(+1), "Lower priority");
            up.IsEnabled = index > 0; down.IsEnabled = index < p.Alerts.Count - 1;
            row.Children.Add(up); row.Children.Add(down);

            var on = Theme.Switch(LightPresets.AlertName(a), a.Enabled, v => { a.Enabled = v; Changed(); });
            on.Width = 190; on.Margin = new Thickness(4, 4, 0, 0);
            row.Children.Add(on);
            row.Children.Add(new ColourField(a.Color, hex => { a.Color = hex; Changed(); }) { Margin = new Thickness(0, 0, 14, 0) });
            foreach (LedGroup g in model.AlertGroups)
            {
                var gg = g;
                var chip = new ToggleButton
                {
                    Content = GroupName(g).Replace(" lights", ""), IsChecked = a.Groups.Contains(g), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(9, 3, 9, 4),
                    Foreground = Theme.Text2, Background = Theme.Raised, BorderBrush = Theme.Line2, FontSize = 11.5, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center,
                };
                Action paint = () => { chip.Background = chip.IsChecked == true ? Theme.RedWash : Theme.Raised; chip.BorderBrush = chip.IsChecked == true ? Theme.Red : Theme.Line2; chip.Foreground = chip.IsChecked == true ? Theme.Text : Theme.Text3; };
                chip.Template = ChipTemplate();
                paint();
                chip.Checked += (s, e) => { if (!a.Groups.Contains(gg)) a.Groups.Add(gg); paint(); Changed(); };
                chip.Unchecked += (s, e) => { a.Groups.Remove(gg); paint(); Changed(); };
                row.Children.Add(chip);
            }
            var hz = new ComboBox { Width = 120, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var (label, value) in new[] { ("Steady", 0.0), ("Pulse 1/s", 1.0), ("Blink 2/s", 2.0), ("Blink 4/s", 4.0), ("Flicker 8/s", 8.0), ("Flicker 12/s", 12.0) })
                hz.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            hz.SelectedItem = hz.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((double)i.Tag - a.BlinkHz)).First();
            hz.SelectionChanged += (s, e) => { if (hz.SelectedItem is ComboBoxItem i) { a.BlinkHz = (double)i.Tag; Changed(); } };
            row.Children.Add(hz);
            var style = new ComboBox { Width = 110, Margin = new Thickness(6, 0, 0, 0), ToolTip = "How it shows: all lights blinking, fading in and out, a band running along them, or every other light (chequered)" };
            foreach (AlertStyle st in Enum.GetValues(typeof(AlertStyle))) style.Items.Add(new ComboBoxItem { Content = LightPresets.StyleName(st), Tag = st });
            style.SelectedItem = style.Items.Cast<ComboBoxItem>().First(i => (AlertStyle)i.Tag == a.Style);
            style.SelectionChanged += (s, e) => { if (style.SelectedItem is ComboBoxItem i) { a.Style = (AlertStyle)i.Tag; Changed(); } };
            row.Children.Add(style);

            if (a.Trigger == AlertTrigger.Custom)
            {
                var del = Small("", () => { p.Alerts.Remove(a); Changed(); BuildAlerts(p); }, "Delete this alert");
                del.Margin = new Thickness(6, 0, 0, 0);
                row.Children.Add(del);

                var custom = new WrapPanel { Margin = new Thickness(62, 6, 0, 0) };
                var name = new TextBox { Width = 180, Text = a.Name ?? "", ToolTip = "Its name in this list" };
                name.LostFocus += (s, e) => { var n = name.Text.Trim(); if (n != (a.Name ?? "")) { a.Name = n; Changed(); on.Content = LightPresets.AlertName(a); } };
                custom.Children.Add(new TextBlock { Text = "Name", Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                custom.Children.Add(name);
                var cond = new TextBox { Width = 380, Text = a.Condition ?? "", Margin = new Thickness(0, 0, 0, 0),
                    ToolTip = "On while this is true (or a number other than 0).\nA SimHub property: DataCorePlugin.GameData.NewData.CarDamagesMax\nA formula: [CarDamagesMax] > 5\nJavaScript: js:return $prop('...') > 5\nOr a dash value: absActive, spotterLeft, lapInvalid" };
                var hint = new TextBlock { Foreground = Theme.Text3, FontSize = 11, Margin = new Thickness(62, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
                Action showHint = () =>
                {
                    var b = a.ConditionBind;
                    hint.Text = b == null ? "Type a SimHub property or formula; the alert is on while it's true."
                              : b.StartsWith("prop:", StringComparison.OrdinalIgnoreCase) ? "Read as a SimHub property."
                              : b.StartsWith("ncalc:", StringComparison.OrdinalIgnoreCase) ? "Read as an NCalc formula (as in SimHub's dash studio)."
                              : b.StartsWith("js:", StringComparison.OrdinalIgnoreCase) ? "Read as JavaScript (as in SimHub's dash studio)."
                              : "Read as the dash value \"" + b + "\".";
                    hint.Text += " Only live with SimHub's data: the preview and the demo don't show it.";
                };
                cond.LostFocus += (s, e) => { var c = cond.Text.Trim(); if (c != (a.Condition ?? "")) { a.Condition = c; Changed(); showHint(); } };
                custom.Children.Add(new TextBlock { Text = "On while", Foreground = Theme.Text2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0) });
                custom.Children.Add(cond);
                outer.Children.Add(custom);
                showHint();
                outer.Children.Add(hint);
            }
            return outer;
        }

        private static Button Small(string glyph, Action click, string tip)
        {
            var b = Theme.Btn("", click);
            b.Content = new TextBlock { Text = glyph, FontFamily = Theme.Icons, FontSize = 11 };
            b.Width = 26; b.Height = 26; b.Padding = new Thickness(0); b.Margin = new Thickness(0, 0, 2, 0);
            b.ToolTip = tip;
            b.VerticalAlignment = VerticalAlignment.Center;
            return b;
        }

        private void BuildLevelsEditor(StackPanel details, GroupLighting l)
        {
            if (model.GaugeRings) { BuildGaugesEditor(details, l); return; }
            details.Children.Add(Theme.Note("Each encoder's light shows its own setting: ABS, TC, brake bias, DIFF and engine map, " +
                "coloured from the first colour (low) to the last (high). A setting the game doesn't give stays dim."));
            if (l.Colors == null || l.Colors.Count < 2) l.Colors = new List<string> { "#00FF40", "#FFB000", "#FF0020" };
            details.Children.Add(Theme.Field("Colours, low to high", ColourList(l.Colors, 1, 4)));
            details.Children.Add(Theme.Field("Brightness", Theme.SliderField(5, 100, l.Brightness, 1, v => $"{v:0}%", v => { if (!loading) { l.Brightness = (int)v; Changed(); } })));
            var flash = Theme.Switch("Blink for a second when a setting changes", l.FlashOnChange, v => { l.FlashOnChange = v; Changed(); });
            flash.Margin = new Thickness(170, 0, 0, 10);
            details.Children.Add(flash);
            var diff = new TextBox { Width = 380, Text = l.DiffSource ?? "",
                ToolTip = "No game gives a standard DIFF value. A SimHub property (e.g. an iRacing dc... value) or a formula; empty = the DIFF light stays dim." };
            diff.LostFocus += (s, e) => { var d = diff.Text.Trim(); if (d != (l.DiffSource ?? "")) { l.DiffSource = d; Changed(); } };
            details.Children.Add(Theme.Field("DIFF shows", diff));
            var ranges = string.Join(" · ", LightEngine.EncoderLevels.Where(x => x.Name != "DIFF").Select(x => $"{x.Name} {x.Low:0}-{x.High:0}")) + " · DIFF 1-10";
            details.Children.Add(Theme.Note("Colour ranges: " + ranges + " (brake bias in %).", new Thickness(170, 0, 0, 8)));
        }

        /// <summary>
        /// GT Neo: each encoder ring as a gauge. Per ring: what it shows (a built-in value or any SimHub property /
        /// formula), its range for custom values, and fill or pointer.
        /// </summary>
        private void BuildGaugesEditor(StackPanel details, GroupLighting l)
        {
            details.Children.Add(Theme.Note("Each ring is a gauge of a value: it fills clockwise from 12 o'clock (or shows a pointer) in the colours from " +
                "low to high, and flashes when the value changes. A value the game doesn't give leaves the ring dim."));
            if (l.Colors == null || l.Colors.Count < 2) l.Colors = new List<string> { "#00FF40", "#FFB000", "#FF0020" };
            details.Children.Add(Theme.Field("Colours, low to high", ColourList(l.Colors, 1, 4)));
            details.Children.Add(Theme.Field("Brightness", Theme.SliderField(5, 100, l.Brightness, 1, v => $"{v:0}%", v => { if (!loading) { l.Brightness = (int)v; Changed(); } })));
            var flash = Theme.Switch("Flash for a second when a value changes", l.FlashOnChange, v => { l.FlashOnChange = v; Changed(); });
            flash.Margin = new Thickness(170, 0, 0, 10);
            details.Children.Add(flash);
            string[] where = { "Upper left ring", "Upper right ring", "Lower left ring", "Lower right ring" };
            if (l.Gauges == null) l.Gauges = new List<RingGauge>();
            for (int k = 0; k < 4; k++)
            {
                while (l.Gauges.Count <= k) l.Gauges.Add(l.GaugeFor(l.Gauges.Count).Clone());
                int kk = k;
                var g = l.Gauges[k];
                var row = new WrapPanel();
                var source = new ComboBox { Width = 170 };
                foreach (var s in RingGauge.Sources) source.Items.Add(new ComboBoxItem { Content = s.Name, Tag = s.Key });
                source.Items.Add(new ComboBoxItem { Content = "A SimHub value…", Tag = RingGauge.Custom });
                source.SelectedItem = source.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (g.IsCustom ? RingGauge.Custom : g.Source));
                var custom = new TextBox { Width = 260, Text = g.IsCustom ? g.Source : "", Margin = new Thickness(8, 0, 0, 0), Visibility = g.IsCustom ? Visibility.Visible : Visibility.Collapsed,
                    ToolTip = "A SimHub property (DataCorePlugin.GameData.NewData.TyreWearFrontLeft) or a formula ([Fuel] / [MaxFuel] * 100)" };
                var low = new TextBox { Width = 50, Text = g.Low.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(8, 0, 0, 0), ToolTip = "The value for an empty ring" };
                var high = new TextBox { Width = 50, Text = g.High.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Margin = new Thickness(4, 0, 0, 0), ToolTip = "The value for a full ring" };
                var style = new ComboBox { Width = 100, Margin = new Thickness(8, 0, 0, 0) };
                style.Items.Add(new ComboBoxItem { Content = "Fill", Tag = GaugeStyle.Fill });
                style.Items.Add(new ComboBoxItem { Content = "Pointer", Tag = GaugeStyle.Pointer });
                style.SelectedItem = style.Items.Cast<ComboBoxItem>().First(i => (GaugeStyle)i.Tag == g.Style);
                source.SelectionChanged += (s2, e2) =>
                {
                    if (loading || !(source.SelectedItem is ComboBoxItem i)) return;
                    var key = (string)i.Tag;
                    var fresh = key == RingGauge.Custom ? new RingGauge { Source = custom.Text.Trim().Length > 0 ? custom.Text.Trim() : "", Low = 0, High = 100 } : RingGauge.Of(key);
                    l.Gauges[kk] = fresh;
                    custom.Visibility = key == RingGauge.Custom ? Visibility.Visible : Visibility.Collapsed;
                    low.Text = fresh.Low.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    high.Text = fresh.High.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                    style.SelectedItem = style.Items.Cast<ComboBoxItem>().First(x => (GaugeStyle)x.Tag == fresh.Style);
                    Changed();
                };
                custom.LostFocus += (s2, e2) => { var t = custom.Text.Trim(); if (l.Gauges[kk].IsCustom && t != l.Gauges[kk].Source) { l.Gauges[kk].Source = t; Changed(); } };
                Action<TextBox, Action<double>> number = (box, set) => box.LostFocus += (s2, e2) =>
                {
                    if (double.TryParse(box.Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) { set(d); Changed(); }
                };
                number(low, d => l.Gauges[kk].Low = d);
                number(high, d => l.Gauges[kk].High = d);
                style.SelectionChanged += (s2, e2) => { if (!loading && style.SelectedItem is ComboBoxItem i) { l.Gauges[kk].Style = (GaugeStyle)i.Tag; Changed(); } };
                row.Children.Add(source); row.Children.Add(custom);
                row.Children.Add(new TextBlock { Text = "from", Foreground = Theme.Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
                row.Children.Add(low);
                row.Children.Add(new TextBlock { Text = "to", Foreground = Theme.Text3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                row.Children.Add(high); row.Children.Add(style);
                details.Children.Add(Theme.Field(where[k], row));
            }
        }

        private static ControlTemplate chipTemplate;

        private static ControlTemplate ChipTemplate()
        {
            if (chipTemplate != null) return chipTemplate;
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(content);
            return chipTemplate = new ControlTemplate(typeof(ToggleButton)) { VisualTree = border };
        }

        private void BuildRevEditor(StackPanel details, RevLighting rev)
        {
            details.Children.Add(Theme.Switch("Use the car's real rev lights when known", rev.UseCarData, v => { rev.UseCarData = v; Changed(); },
                "From the rev light database and your car tuning. Cars without data, and every car when this is off, use the pattern below."));

            // the pattern, each tile a live preview in this preset's colours
            patternStrips.Clear();
            var tiles = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            var tileList = new List<(PatternKind Kind, Border Tile)>();
            foreach (var entry in LedPatterns.Catalog.Where(c => c.Kind != PatternKind.SimProPreset))
            {
                var kind = entry.Kind;
                var strip = new LedStrip(7);
                var body = new StackPanel();
                body.Children.Add(new TextBlock { Text = entry.Title, FontFamily = Theme.Display, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
                body.Children.Add(strip);
                Border tile = null;
                tile = Theme.Tile(body, 190, () =>
                {
                    rev.Pattern = kind; Changed();
                    foreach (var (k, t) in tileList) Theme.Select(t, k == kind);
                }, entry.Description);
                tile.Padding = new Thickness(10, 8, 10, 10);
                tile.Margin = new Thickness(0, 0, 8, 8);
                Theme.Select(tile, rev.Pattern == kind || (rev.Pattern == PatternKind.SimProPreset && kind == PatternKind.LeftToRight));
                tileList.Add((kind, tile));
                patternStrips.Add((strip, kind, rev));
                tiles.Children.Add(tile);
            }
            details.Children.Add(Theme.Eyebrow("Pattern"));
            details.Children.Add(tiles);
            details.Children.Add(Theme.Field("Colours, low to high", ColourList(rev.Colors, 1, 4)));
            details.Children.Add(Theme.Field("First LED at", Theme.SliderField(40, 98, rev.StartPercent, 1, v => $"{v:0}% of the shift point", v => { if (!loading) { rev.StartPercent = v; Changed(); } })));
            details.Children.Add(Theme.Field("Shift flash", new ColourField(rev.FlashColor, hex => { rev.FlashColor = hex; Changed(); })));
            var hz = new ComboBox { Width = 160 };
            foreach (var (label, value) in new[] { ("Steady", 0.0), ("Blink 4/s", 4.0), ("Blink 8/s", 8.0), ("Blink 12/s", 12.0) })
                hz.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            hz.SelectedItem = hz.Items.Cast<ComboBoxItem>().OrderBy(i => Math.Abs((double)i.Tag - rev.FlashHz)).First();
            hz.SelectionChanged += (s, e) => { if (hz.SelectedItem is ComboBoxItem i) { rev.FlashHz = (double)i.Tag; Changed(); } };
            details.Children.Add(Theme.Field("Flash", hz));
        }

        /// <summary>1-4 colours with add/remove.</summary>
        private FrameworkElement ColourList(List<string> colours, int min, int max)
        {
            var panel = new WrapPanel();
            Action build = null;
            build = () =>
            {
                panel.Children.Clear();
                for (int i = 0; i < colours.Count; i++)
                {
                    int k = i;
                    panel.Children.Add(new ColourField(colours[k], hex => { colours[k] = hex; Changed(); }) { Margin = new Thickness(0, 0, 10, 4) });
                }
                if (colours.Count < max) panel.Children.Add(Round("+", () => { colours.Add(colours.LastOrDefault() ?? "#FFFFFF"); Changed(); build(); }));
                if (colours.Count > min) panel.Children.Add(Round("−", () => { colours.RemoveAt(colours.Count - 1); Changed(); build(); }));
            };
            build();
            return panel;
        }

        private static Button Round(string text, Action click)
        {
            var b = Theme.Btn(text, click);
            b.Width = 30; b.Height = 30; b.Padding = new Thickness(0); b.Margin = new Thickness(0, 0, 6, 4);
            return b;
        }

        private static string EffectName(LightEffect e)
        {
            switch (e)
            {
                case LightEffect.Off: return "Off";
                case LightEffect.Solid: return "Solid";
                case LightEffect.Breathe: return "Breathing";
                case LightEffect.Wave: return "Colour wave";
                case LightEffect.Rainbow: return "Rainbow flow";
                case LightEffect.RainbowBreathe: return "Breathing rainbow";
                case LightEffect.Scanner: return "Scanner";
                case LightEffect.Sparkle: return "Sparkle";
                case LightEffect.Levels: return "Setting levels";
                case LightEffect.Comet: return "Comet";
                case LightEffect.Heartbeat: return "Heartbeat";
                case LightEffect.Fire: return "Fire";
                case LightEffect.Twinkle: return "Twinkling stars";
                case LightEffect.Ripple: return "Ripples";
                case LightEffect.Plasma: return "Plasma";
                case LightEffect.Strobe: return "Strobes";
                default: return "Shift lights (RPM)";
            }
        }

        // ---------- ATSR-Hub ----------

        private void SetAtsrDevice(string name)
        {
            name = (name ?? "").Trim();
            if (name == (S.AtsrDevice ?? "")) return;
            S.AtsrDevice = name;
            Changed();
        }

        private void RefreshAtsrDevices()
        {
            var pm = plugin.PluginManager;
            var devices = pm == null ? new List<string>() : AtsrBridge.Devices(pm);
            string current = S.AtsrDevice ?? "";
            atsrDevice.ItemsSource = devices;
            atsrDevice.Text = current;
            if (string.IsNullOrEmpty(current) && devices.Count == 1) SetAtsrDevice(atsrDevice.Text = devices[0]);
        }
    }
}
