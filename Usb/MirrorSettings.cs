using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace User.FXProRpmSync
{
    /// <summary>The frame round the screen on the stream page. Bezel and Carbon are housings (the lights sit inside them); the rest float.</summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MirrorFrame { None, Line, Bezel, Carbon, Glow }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MirrorLightStyle { Dots, Bars, Line }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MirrorLightsAt { Above, Below }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum MirrorBackground { Transparent, Dark, Green }

    /// <summary>
    /// How the stream page (`/mirror`, Usb/Designer/mirror.html, ScreenMirror) looks. Set in the plugin; the page reads it
    /// from `/api/wheel/mirror` on every poll, so a change shows in OBS at once and the browser source's address stays plain.
    /// </summary>
    public class MirrorSettings
    {
        public MirrorFrame Frame = MirrorFrame.Bezel;
        /// <summary>The frame's accent (Line, Glow and the Carbon piping), "#RRGGBB"; "rev" = the colour of the rev lights' lead LED.</summary>
        public string FrameColor = "#FF1F2D";
        /// <summary>The screen's corner radius in page pixels (the wheel's own screen is square).</summary>
        public int Corners = 10;

        /// <summary>The 15 rev lights.</summary>
        public bool RevLights = true;
        /// <summary>The three lights down each side of the screen (flags, alerts).</summary>
        public bool SideLights = true;
        public MirrorLightStyle LightStyle = MirrorLightStyle.Dots;
        /// <summary>Which edge the rev lights sit on.</summary>
        public MirrorLightsAt LightsAt = MirrorLightsAt.Above;

        public MirrorBackground Background = MirrorBackground.Transparent;
        /// <summary>Show nothing at all while the plugin isn't drawing the screen (instead of a note).</summary>
        public bool HideWhenIdle = false;
        /// <summary>Page updates per second (1-30). The screen itself only changes ~10 times a second.</summary>
        public int Fps = 20;

        public static readonly string[] Accents = { "#FF1F2D", "#EEF0F3", "#00E5FF", "#4DA3FF", "#34D27B", "#F5A524", "#9B6BFF" };

        /// <summary>Settings from an old or hand-edited file made safe.</summary>
        public void Clamp()
        {
            Corners = Math.Max(0, Math.Min(32, Corners));
            Fps = Math.Max(1, Math.Min(30, Fps));
            if (!IsFollowRev && !IsHex(FrameColor)) FrameColor = Accents[0];
        }

        public bool IsFollowRev => string.Equals(FrameColor, "rev", StringComparison.OrdinalIgnoreCase);

        private static bool IsHex(string s)
        {
            if (s == null || s.Length != 7 || s[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(s[i])) return false;
            return true;
        }

        public MirrorSettings Clone() => (MirrorSettings)MemberwiseClone();

        /// <summary>What the page gets (lower-case names, as the page's other data).</summary>
        public object ForPage()
        {
            Clamp();
            return new
            {
                frame = Frame.ToString().ToLowerInvariant(),
                frameColor = IsFollowRev ? "rev" : FrameColor.ToUpperInvariant(),
                corners = Corners,
                rev = RevLights,
                sides = SideLights,
                lightStyle = LightStyle.ToString().ToLowerInvariant(),
                lightsAt = LightsAt.ToString().ToLowerInvariant(),
                background = Background.ToString().ToLowerInvariant(),
                hideWhenIdle = HideWhenIdle,
                fps = Fps,
            };
        }
    }
}
