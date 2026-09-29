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
    /// itself, and your own lights edited group by group: click a part of the wheel (or its chip) to edit it.
    /// </summary>
    public class UnlockedLightsTab : StackPanel
    {
        private readonly FXProRpmSyncPlugin plugin;
        private UsbSettings S => plugin.Settings.Usb;
        private UsbController Usb => plugin.Usb;

        private readonly WheelView big = new WheelView { Width = 600 };
        private readonly LightEngine bigEngine = new LightEngine();
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

        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly DispatcherTimer frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        private readonly DispatcherTimer slowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        private int frame;

        public UnlockedLightsTab(FXProRpmSyncPlugin plugin)
        {
            this.plugin = plugin;

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
            var seg = Theme.Segmented(new[] { "FX Unleashed", "ATSR-Hub", "SimHub device" }, (int)S.LightsFrom, i =>
            {
                S.LightsFrom = (LightsSource)i;
                Changed(); ShowSource();
                if (i == 1) RefreshAtsrDevices();
            }, out _);
            seg.Margin = new Thickness(0, 4, 0, 14);
            src.Children.Add(seg);
            var opts = new WrapPanel();
            var idle = Theme.Switch("Keep them on between sessions", S.IdleLights, v => { S.IdleLights = v; Changed(); }, "Off: SimPro's lights while no game runs.");
            idle.Margin = new Thickness(0, 0, 40, 6);
            opts.Children.Add(idle);
            src.Children.Add(opts);
            liveNote = new TextBlock { Foreground = Theme.Text3, FontSize = 11.5, Margin = new Thickness(0, 6, 0, 0) };
            src.Children.Add(liveNote);
            Children.Add(Theme.CardBox(src));

            // ----- ATSR-Hub -----
            atsrPanel = new StackPanel();
            atsrPanel.Children.Add(Theme.Eyebrow("ATSR-Hub"));
            atsrPanel.Children.Add(Theme.Note("ATSR-Hub works out every light (shift lights, flags, spotter, TC/ABS, animations); the plugin sends them to the wheel. " +
                "Add the FX Pro in ATSR-Hub as a steering wheel (VID 0483, PID 0529) numbered like the wheel view here: hover an LED to see its number. " +
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
            var map = new TextBox { Width = 520, Text = S.AtsrMap ?? "", ToolTip = "38 ATSR-Hub LED numbers, one per FX Pro LED in FX Pro order, -1 = off. Empty = same numbers." };
            atsrMapError = new TextBlock { Foreground = Theme.Amber, Margin = new Thickness(130, -6, 0, 10), TextWrapping = TextWrapping.Wrap };
            map.LostFocus += (s, e) =>
            {
                AtsrBridge.ParseMap(map.Text, out var err);
                atsrMapError.Text = err == null ? "" : "Map not used: " + err;
                if (err == null) { S.AtsrMap = map.Text.Trim(); Changed(); }
            };
            atsrPanel.Children.Add(Theme.Field("LED map", map, 130));
            atsrPanel.Children.Add(atsrMapError);
            atsrState = new TextBlock { Foreground = Theme.Text2, Margin = new Thickness(130, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
            atsrPanel.Children.Add(atsrState);
            var atsrCard = Theme.CardBox(atsrPanel);
            Children.Add(atsrCard);
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
            big.LedClicked += led => { if (Mine != null) { group = WheelView.GroupOf(led); ShowEditor(); } };
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            side.Children.Add(Theme.Eyebrow("Preview"));
            bigName = Theme.Title("", 24);
            side.Children.Add(bigName);
            bigText = Theme.Note("", new Thickness(0, 6, 0, 14));
            side.Children.Add(bigText);
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
            builtIn.Children.Add(perCar = new LightsForCarPanel(plugin));
            Children.Add(Theme.CardBox(builtIn));

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
            editor.Tag = editorCard;

            // ----- Buttons light while pressed (any lights source) -----
            Children.Add(Theme.CardBox(new ButtonLightsCard(plugin, Changed)));

            frameTimer.Tick += (s, e) => RenderFrame();
            slowTimer.Tick += (s, e) => RefreshState();
            saveTimer.Tick += (s, e) => { saveTimer.Stop(); plugin.SaveSettings(); };
            Loaded += (s, e) =>
            {
                frameTimer.Start(); slowTimer.Start();
                var (wheelDash, id) = plugin.UsbDashFor(plugin.DashCarKey);
                big.Screen.Source = wheelDash ? null : DashPictures.Still(DashCache.Find(id) ?? BuiltInDashes.MustangGt3());
            };
            Unloaded += (s, e) => { frameTimer.Stop(); slowTimer.Stop(); if (saveTimer.IsEnabled) { saveTimer.Stop(); plugin.SaveSettings(); } };

            ShowSource();
            BuildGallery();
            if (S.LightsFrom == LightsSource.AtsrHub) RefreshAtsrDevices();
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

        private void RefreshState()
        {
            var u = Usb;
            liveNote.Text = !S.LightsEnabled ? "The wheel shows SimPro's lights."
                          : u?.Active == true ? "On the wheel now: " + u.LightsState + "."
                          : S.LightsFrom == LightsSource.AtsrHub ? "ATSR-Hub's lights show while the plugin drives the wheel; the preset below fills in while it sends nothing."
                          : S.LightsFrom == LightsSource.SimHubDevice ? "SimHub's LED profile shows while the plugin drives the wheel: add \"FX Pro wheel (USB mode)\" (brand FX Unleashed) in SimHub > Devices " +
                                                                        "and set up its lights there (21 side + rev lights, 12 buttons, 5 encoders, or 38 individual LEDs). The preset below fills in while it sends nothing." : "";
            if (S.LightsFrom == LightsSource.AtsrHub)
                atsrState.Text = u?.Active != true ? "Not sending: the plugin isn't driving the wheel now." : "Lights now: " + u.LightsState + ".";
        }

        // ---------- Presets ----------

        /// <summary>The selected lights when they're the user's own (editable), else null.</summary>
        private LightProfile Mine => S.UserLights.FirstOrDefault(p => p.Id == S.LightPreset);

        private void BuildGallery()
        {
            gallery.Children.Clear();
            presets.Clear();
            foreach (var p in LightPresets.All) AddPreset(p);
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
            var view = new WheelView(glow: false) { Width = 196 };
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
            presets.Add((id, tile, view, new LightEngine()));
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
            S.LightPreset = at > 0 ? S.UserLights[at - 1].Id : LightPresets.All[0].Id;
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
            ((Border)editor.Tag).Visibility = mine != null ? Visibility.Visible : Visibility.Collapsed;
            if (mine != null) ShowEditor(); else big.Highlight(null);
            perCar?.Refresh(force: true);
        }

        private void RenderFrame()
        {
            double t = clock.Elapsed.TotalSeconds;
            var sim = SimLap.Values(t);
            var live = Usb?.Active == true && S.LightsEnabled ? Usb.LastFrame : null;
            big.Show(live ?? bigEngine.Render(S.ActiveLights, sim, null, t, false));
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
                view.Show(p == null ? null : engine.Render(p, sim, null, t, false));
            }
        }

        // ---------- Editor ----------

        private static string GroupName(LedGroup g)
        {
            switch (g)
            {
                case LedGroup.Buttons: return "Buttons";
                case LedGroup.Encoders: return "Encoders";
                case LedGroup.SideLeft: return "Left lights";
                case LedGroup.SideRight: return "Right lights";
                default: return "Rev lights";
            }
        }

        private void ShowEditor()
        {
            var p = Mine;
            if (p == null) return;
            big.Highlight(WheelView.LedsOf(group));
            groupChips.Children.Clear();
            foreach (LedGroup g in Enum.GetValues(typeof(LedGroup)))
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
                .Where(x => (x != LightEffect.Rpm || group == LedGroup.Rev) && (x != LightEffect.Levels || group == LedGroup.Encoders));
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
            foreach (LedGroup g in Enum.GetValues(typeof(LedGroup)))
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
