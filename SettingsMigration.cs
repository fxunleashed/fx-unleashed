using System;
using System.IO;

namespace User.FXProRpmSync
{
    /// <summary>
    /// Settings versions (NEXT.md B): old settings survive updates. Each step is a small, idempotent change keyed by
    /// SettingsVersion; before changing the file's version the old file is copied aside, so a roll back (or a newer
    /// version's file read by an older plugin, which drops fields it doesn't know) never loses anything.
    ///   0/1 -> 2: quick controls, lights per car/game, update settings (all new fields with defaults; nothing moved).
    /// </summary>
    public partial class FXProRpmSyncPlugin
    {
        private static string SettingsFile =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSyncPlugin.GeneralSettings.json");

        private static void MigrateSettings(FXProRpmSyncSettings s)
        {
            if (s.Updates == null) s.Updates = new UpdateSettings();
            if (string.IsNullOrWhiteSpace(s.Updates.Repo)) s.Updates.Repo = Updater.DefaultRepo;
            int from = s.SettingsVersion;
            if (from == CurrentSettingsVersion) return;
            BackupSettings(from);
            if (from > CurrentSettingsVersion)
            {
                SimHub.Logging.Current.Warn($"[FXProRpmSync] settings were written by a newer version (format {from}); kept a copy, reading what this version knows");
                s.SettingsVersion = CurrentSettingsVersion;
                return;
            }
            // 0/1 -> 2: nothing to move; Init's null checks and EnsureQuickSettings fill the new fields.
            s.SettingsVersion = CurrentSettingsVersion;
            SimHub.Logging.Current.Info($"[FXProRpmSync] settings migrated from format {from} to {CurrentSettingsVersion}");
        }

        /// <summary>Undoes the lights' growth from the old load bugs (LightsRepair), keeping a copy of the file first.</summary>
        private void RepairLights()
        {
            var log = new System.Collections.Generic.List<string>();
            if (!LightsRepair.Repair(Settings.Usb, log)) return;
            try
            {
                var copy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync", "settings-backup-before-lights-repair.json");
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                if (File.Exists(SettingsFile) && !File.Exists(copy)) File.Copy(SettingsFile, copy);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] settings backup failed: " + ex.Message); }
            SimHub.Logging.Current.Info("[FXProRpmSync] repaired saved lights that had grown on every start: " + string.Join(", ", log));
            SaveSettings();
        }

        /// <summary>The spotter used to light the three small lights beside the rev bar; saved lights that still have it there move to the six buttons on its side.</summary>
        private void UpgradeSpotterAlerts()
        {
            var log = new System.Collections.Generic.List<string>();
            if (!LightsRepair.UpgradeSpotter(Settings.Usb, log)) return;
            try
            {
                var copy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync", "settings-backup-before-spotter-buttons.json");
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                if (File.Exists(SettingsFile) && !File.Exists(copy)) File.Copy(SettingsFile, copy);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] settings backup failed: " + ex.Message); }
            SimHub.Logging.Current.Info("[FXProRpmSync] the spotter now lights the six buttons on its side in saved lights: " + string.Join(", ", log));
            SaveSettings();
        }

        private static void BackupSettings(int version)
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginsData", "Common", "FXProRpmSync");
                Directory.CreateDirectory(dir);
                var copy = Path.Combine(dir, $"settings-backup-format{version}.json");
                if (!File.Exists(copy)) File.Copy(SettingsFile, copy);
            }
            catch (Exception ex) { SimHub.Logging.Current.Warn("[FXProRpmSync] settings backup failed: " + ex.Message); }
        }
    }
}
