using System;
using System.Collections.Generic;
using UnityEngine;

namespace Empire.UI
{
    /// <summary>Shared military-atlas palette and controls for every game screen.
    /// Create styles inside OnGUI; use Scope to restore the caller's GUI state.</summary>
    public static class EmpireTheme
    {
        public static readonly Color Ink = Hex(0x111B1A, .98f);
        public static readonly Color Surface = Hex(0x202925, .99f);
        public static readonly Color Raised = Hex(0x2B342E);
        public static readonly Color Hover = Hex(0x41483A);
        public static readonly Color Selected = Hex(0x51452F);
        public static readonly Color Ivory = Hex(0xF2E7CF);
        public static readonly Color Muted = Hex(0xB8B5A5);
        public static readonly Color Brass = Hex(0xCBA76A);
        public static readonly Color Line = Hex(0x575B49);
        public static Color Border => Line;
        public static readonly Color Danger = Hex(0xDF847B);
        public static readonly Color FrenchBlue = Hex(0x719AC8);
        public static readonly Color BritishRed = Hex(0xBC7671);
        public const int CaptionSize = 12, BodySize = 14, HeadingSize = 24;
        static GUISkin skin;
        static Font bodyFont, boldFont, displayFont, italicFont;
        public static Font BodyFont => bodyFont ? bodyFont : bodyFont = Resources.Load<Font>("UI/Fonts/Lato-Regular");
        public static Font BoldFont => boldFont ? boldFont : boldFont = Resources.Load<Font>("UI/Fonts/Lato-Bold");
        public static Font DisplayFont => displayFont ? displayFont : displayFont = Resources.Load<Font>("UI/Fonts/IMFellEnglish");
        public static Font ItalicFont => italicFont ? italicFont : italicFont = Resources.Load<Font>("UI/Fonts/IMFellEnglish-Italic");
        static readonly List<Texture2D> textures = new List<Texture2D>();
        static readonly Dictionary<GUIStyle, GUIStyle> selectedButtons = new Dictionary<GUIStyle, GUIStyle>();

        static Color Hex(uint rgb, float alpha = 1) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, alpha);

        public static Color RenderColor(Color color) => QualitySettings.activeColorSpace == ColorSpace.Linear ? color.linear : color;

        public static GUIStyle Text(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight,
                wordWrap = false, padding = new RectOffset(), margin = new RectOffset(), alignment = TextAnchor.MiddleLeft };
            var font = weight == FontStyle.Bold ? BoldFont : BodyFont;
            if (font) { style.font = font; if (weight == FontStyle.Bold) style.fontStyle = FontStyle.Normal; }
            style.normal.textColor = color;
            return style;
        }

        public static GUIStyle Display(int size, Color color, bool italic = false)
        {
            var style = Text(size, color);
            var font = italic ? ItalicFont : DisplayFont;
            if (font) style.font = font;
            return style;
        }

        static Texture2D Tile(Color fill, Color edge)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Empire UI surface", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                pixels[y * size + x] = RenderColor(x == 0 || y == 0 || x == size - 1 || y == size - 1 ? edge :
                    y == 1 ? Color.Lerp(fill, Ivory, .06f) : y == size - 2 ? Color.Lerp(fill, Color.black, .18f) : fill);
            texture.SetPixels(pixels); texture.Apply(false, true); textures.Add(texture); return texture;
        }

        static void State(GUIStyleState state, Texture2D texture, Color text) { state.background = texture; state.textColor = text; }
        static void States(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Texture2D focus)
        {
            State(style.normal, normal, Ivory); State(style.hover, hover, Ivory);
            State(style.active, active, Ivory); State(style.focused, focus, Ivory);
            State(style.onNormal, active, Ivory); State(style.onHover, focus, Ivory);
            State(style.onActive, active, Ivory); State(style.onFocused, focus, Ivory);
        }

        static GUISkin Skin
        {
            get
            {
                if (skin) return skin;
                skin = UnityEngine.Object.Instantiate(GUI.skin); skin.name = "Empire / Engraved Atlas"; skin.hideFlags = HideFlags.HideAndDontSave;
                if (BodyFont) skin.font = BodyFont;
                var normal = Tile(Raised, Line); var hover = Tile(Hover, Muted);
                var active = Tile(Selected, Brass); var focus = Tile(Raised, Brass);
                skin.label = Text(BodySize, Ivory);
                skin.button = Text(BodySize, Ivory);
                skin.button.alignment = TextAnchor.MiddleCenter;
                skin.button.padding = new RectOffset(12, 12, 8, 8); skin.button.margin = new RectOffset(0, 6, 3, 3);
                skin.button.border = new RectOffset(2, 2, 2, 2); States(skin.button, normal, hover, active, focus);
                skin.textField = new GUIStyle(skin.button) { alignment = TextAnchor.MiddleLeft };
                States(skin.textField, Tile(Ink, Line), normal, focus, focus);
                skin.toggle = new GUIStyle(skin.button) { alignment = TextAnchor.MiddleLeft };
                skin.box = new GUIStyle(skin.button); States(skin.box, Tile(Surface, Line), normal, active, focus);
                var track = Tile(Ink, Ink); var thumb = Tile(Line, Line); var thumbHover = Tile(Muted, Muted);
                foreach (var bar in new[] { skin.verticalScrollbar, skin.horizontalScrollbar }) States(bar, track, track, track, track);
                foreach (var bar in new[] { skin.verticalScrollbarThumb, skin.horizontalScrollbarThumb }) States(bar, thumb, thumbHover, focus, focus);
                skin.verticalScrollbar.fixedWidth = 8; skin.horizontalScrollbar.fixedHeight = 8;
                skin.verticalScrollbar.margin = new RectOffset(6, 0, 0, 0);
                foreach (var arrow in new[] { skin.verticalScrollbarUpButton, skin.verticalScrollbarDownButton, skin.horizontalScrollbarLeftButton, skin.horizontalScrollbarRightButton })
                { arrow.fixedWidth = 0; arrow.fixedHeight = 0; States(arrow, track, track, track, track); }
                skin.settings.cursorColor = RenderColor(Ivory); skin.settings.selectionColor = RenderColor(Selected);
                return skin;
            }
        }

        public static GUIStyle ButtonStyle(int size = BodySize, TextAnchor alignment = TextAnchor.MiddleCenter)
            => new GUIStyle(Skin.button) { fontSize = size, alignment = alignment };

        public static bool Button(Rect rect,string text,GUIStyle style,bool selected=false)=>Button(rect,new GUIContent(text),style,selected);
        public static bool Button(Rect rect,GUIContent content,GUIStyle style,bool selected=false)
        {
            if(!selected)return GUI.Button(rect,content,style);
            // Do not retain a style per draw: callers may construct fresh styles every frame.
            var chosen=new GUIStyle(style){normal=style.onNormal,hover=style.onHover,active=style.onActive,focused=style.onFocused};
            return GUI.Button(rect,content,chosen);
        }

        public static bool Toggle(bool value, string text)
        {
            return GUILayout.Toggle(value, (value ? "✓  " : "–  ") + text, Skin.toggle);
        }

        public static void Fill(Rect rect, Color color)
        { var old = GUI.color; GUI.color = RenderColor(color); GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }
        public static void Panel(Rect rect, Color color)
        {
            Fill(rect, color);
            Fill(new Rect(rect.x, rect.y, rect.width, 1), Line);
            Fill(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Line);
            Fill(new Rect(rect.x, rect.y, 1, rect.height), Line);
            Fill(new Rect(rect.xMax - 1, rect.y, 1, rect.height), Line);
        }

        // Fine double rules and corner tooling, reserved for the outer command surfaces.
        public static void Frame(Rect rect, Color color)
        {
            Panel(rect, color);
            Fill(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, 1), Brass);
            Fill(new Rect(rect.x + 1, rect.yMax - 2, rect.width - 2, 1), Brass);
            const float length = 16;
            for (int side = 0; side < 2; side++) for (int end = 0; end < 2; end++)
            {
                float x = side == 0 ? rect.x + 4 : rect.xMax - 4;
                float y = end == 0 ? rect.y + 4 : rect.yMax - 5;
                Fill(new Rect(side == 0 ? x : x - length, y, length, 1), Brass);
                Fill(new Rect(x, end == 0 ? y : y - 8, 1, 9), Brass);
            }
        }

        public static void Ornament(float x, float y, float width)
        {
            float center = x + width / 2;
            Fill(new Rect(x, y, width / 2 - 12, 1), Line);
            Fill(new Rect(center + 12, y, width / 2 - 12, 1), Line);
            for (int row = -3; row <= 3; row++)
                Fill(new Rect(center - (3 - Mathf.Abs(row)), y + row, (3 - Mathf.Abs(row)) * 2 + 1, 1), Brass);
        }

        public readonly struct Scope : IDisposable
        {
            readonly GUISkin previous;readonly Matrix4x4 matrix;readonly bool enabled;readonly int depth;readonly Color color,content,background;
            public Scope(bool apply=true){previous=GUI.skin;matrix=GUI.matrix;enabled=GUI.enabled;depth=GUI.depth;color=GUI.color;content=GUI.contentColor;background=GUI.backgroundColor;if(apply)GUI.skin=Skin;}
            public void Dispose(){GUI.skin=previous;GUI.matrix=matrix;GUI.enabled=enabled;GUI.depth=depth;GUI.color=color;GUI.contentColor=content;GUI.backgroundColor=background;}
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            if (skin) UnityEngine.Object.Destroy(skin); skin = null;
            foreach (var texture in textures) if (texture) UnityEngine.Object.Destroy(texture);
            textures.Clear(); selectedButtons.Clear();
            bodyFont = boldFont = displayFont = italicFont = null;
        }
    }
}
