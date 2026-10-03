using Empire.UI;
using UnityEngine;

namespace EmpireBattle
{
    // A cartographic level of detail, driven by camera height above the focus terrain.
    public sealed partial class BattleView
    {
        bool? atlasViewOverride;
        public float AtlasBlend => atlasViewOverride.HasValue ? (atlasViewOverride.Value ? 1f : 0f)
            : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(180, 500, distance * Mathf.Sin(pitch * Mathf.Deg2Rad)));
        public void ToggleAtlasView()
        {
            // Once chosen manually, keep the presentation independent of camera height,
            // tilt, focus and selection. Toggling never moves or reorients the camera.
            atlasViewOverride=AtlasBlend<.5f;
            status=atlasViewOverride.Value?"White map view · M returns to terrain.":"Terrain view · M opens the white map.";
        }
        [System.NonSerialized] public bool ReviewAtlasOrders;
        public int VisibleAtlasAttackOrders { get; private set; }
        Texture2D atlasArrow;
        GUIStyle atlasLabel;
        readonly Rect[] atlasLabels = new Rect[512];
        int atlasLabelCount;
        static readonly Color AtlasBlue = new Color(.055f, .27f, .57f);
        static readonly Color AtlasRed = new Color(.76f, .12f, .10f);
        static Color ArmyInk(ArmySide side) => side == ArmySide.French ? AtlasBlue : AtlasRed;

        bool AtlasPoint(Vector2 ground, out Vector2 point)
        {
            var screen = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(ground, 2));
            point = new Vector2(screen.x, Screen.height - screen.y);
            return screen.z > 0 && point.x > -30 && point.x < Screen.width + 30 && point.y > 88 && point.y < hud.StatusRect.y - 8;
        }
        static Vector2 AtlasHalfExtents(BattalionState unit) => FormationMath.HalfExtents(unit) + Vector2.one;
        static Vector2 AtlasOrderHalfExtents(BattalionState unit,int files) => FormationMath.CanResizeFrontage(unit)
            ?new Vector2(Mathf.Min(files,unit.AliveCount)*unit.Spacing/2,
                Mathf.CeilToInt(unit.AliveCount/(float)Mathf.Max(1,files))*FormationMath.RankSpacing(unit.Spacing)/2)+Vector2.one
            :AtlasHalfExtents(unit);

        bool AtlasBounds(Vector2 center, float facing, Vector2 half, out Rect rect)
        {
            Vector2 right = FormationMath.Right(facing) * half.x, forward = FormationMath.Forward(facing) * half.y;
            Vector2 minimum = new Vector2(float.MaxValue, float.MaxValue), maximum = -minimum;
            for (int i = 0; i < 4; i++)
            {
                Vector2 ground = center + (i == 0 || i == 3 ? -right : right) + (i < 2 ? -forward : forward);
                Vector3 screen = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(ground, .12f));
                // Labels can be omitted at the near plane; the world outline still draws.
                if (screen.z <= battleCamera.nearClipPlane) { rect = default; return false; }
                Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
                minimum = Vector2.Min(minimum, point); maximum = Vector2.Max(maximum, point);
            }
            rect = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            return rect.Overlaps(new Rect(0, 88, Screen.width, Mathf.Max(0, hud.StatusRect.y - 96)));
        }
        int PickAtlasUnit(Vector2 screen, ArmySide side, bool includeRouting = false)
        {
            if (AtlasBlend < .15f || !GroundPoint(screen, out var ground)) return -1;
            int closest = -1; float best = float.MaxValue;
            float padding = ProjectileVisibility.Width(battleCamera, world.Terrain.SurfacePoint(ground), 8, .1f, 128);
            foreach (var unit in world.Battalions)
            {
                if (unit.Side != side || unit.AliveCount <= 0 || (!includeRouting && unit.Routing)) continue;
                Vector2 half = AtlasHalfExtents(unit), delta = ground - unit.Position;
                if (Mathf.Abs(Vector2.Dot(delta, FormationMath.Right(unit.Facing))) > half.x + padding ||
                    Mathf.Abs(Vector2.Dot(delta, FormationMath.Forward(unit.Facing))) > half.y + padding) continue;
                Vector2 center = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position));
                float d = (screen - center).sqrMagnitude;
                if (d < best) { best = d; closest = unit.Id; }
            }
            return closest;
        }
        void RenderAtlasFormations()
        {
            float blend = AtlasBlend; if (blend < .01f) return;
            bool orders = ReviewAtlasOrders || (EnableInput && Input.GetKey(KeyCode.Space));
            foreach (var unit in world.Battalions)
            {
                if (unit.AliveCount <= 0) continue;
                Color ink = ArmyInk(unit.Side); ink.a = blend * (unit.Routing ? .5f : 1);
                tacticalMarkers.DrawAtlasFormation(unit.Position, unit.Facing, AtlasHalfExtents(unit), ink,
                    selected[unit.Id], false, unit.Kind == UnitKind.Cavalry);
                if (unit.Side != ArmySide.French || unit.Routing) continue;
                if (orders) foreach(var order in world.PreviewOrders(unit))
                    if(!order.IsAttack)tacticalMarkers.DrawAtlasFormation(order.Destination, order.Facing, AtlasOrderHalfExtents(unit,order.Files), ink,
                        false, true, unit.Kind == UnitKind.Cavalry);
                if (ordering && previewActive[unit.Id])
                {
                    Vector2 half = FormationMath.CanResizeFrontage(unit)
                        ? new Vector2(previewFiles[unit.Id] * unit.Spacing / 2,
                            Mathf.CeilToInt(unit.AliveCount / (float)Mathf.Max(1, previewFiles[unit.Id])) * FormationMath.RankSpacing(unit.Spacing) / 2) + Vector2.one
                        : AtlasHalfExtents(unit);
                    tacticalMarkers.DrawAtlasFormation(previewDestinations[unit.Id], previewFacings[unit.Id], half, ink,
                        false, true, unit.Kind == UnitKind.Cavalry);
                }
            }
        }
        void AtlasOrderArrow(Vector2 start, Vector2 end, Color ink, float opacity = .65f, float width = 20)
        {
            if (!AtlasPoint(start, out var a)) return;
            var target = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(end, 2));
            if (target.z <= 0) return;
            Vector2 b = new Vector2(target.x, Screen.height - target.y), delta = b - a;
            float length = delta.magnitude; if (length < 12) return;
            if (!atlasArrow)
            {
                atlasArrow = new Texture2D(128, 32, TextureFormat.RGBA32, false) { name = "Atlas order arrow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                for (int y = 0; y < 32; y++) for (int x = 0; x < 128; x++)
                {
                    float half = x < 96 ? 6 : (127 - x) * .5f;
                    float alpha = Mathf.Clamp01(half - Mathf.Abs(y - 15.5f) + .5f);
                    atlasArrow.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
                atlasArrow.Apply(false, true); owned.Add(atlasArrow);
            }
            var matrix = GUI.matrix; var color = GUI.color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            GUI.color = EmpireTheme.RenderColor(new Color(ink.r, ink.g, ink.b, AtlasBlend * opacity));
            GUI.DrawTexture(new Rect(a.x, a.y - width / 2, length, width), atlasArrow);
            GUI.color = color; GUI.matrix = matrix;
        }
        void AtlasAttackOrder(Vector2 start, Vector2 end)
        {
            var ink = new Color(.78f, .15f, .10f);
            AtlasOrderArrow(start, end, ink, .88f, 14);
            if (!AtlasPoint(end, out var target)) return;
            ink.a = AtlasBlend;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2)
            {
                var corner = target + new Vector2(x * 11, y * 9);
                EmpireTheme.Fill(new Rect(corner.x - (x > 0 ? 5 : 0), corner.y, 5, 1.5f), ink);
                EmpireTheme.Fill(new Rect(corner.x, corner.y - (y > 0 ? 5 : 0), 1.5f, 5), ink);
            }
            VisibleAtlasAttackOrders++;
        }
        void DrawAtlas()
        {
            VisibleAtlasAttackOrders = 0;
            float blend = AtlasBlend; if (blend < .01f) return;
            int depth = GUI.depth; GUI.depth = 15;
            atlasLabel ??= EmpireTheme.Text(11, AtlasBlue, FontStyle.Italic);
            atlasLabel.alignment = TextAnchor.MiddleCenter;
            atlasLabelCount = 0;
            bool orders = ReviewAtlasOrders || (EnableInput && Input.GetKey(KeyCode.Space));
            if (orders || ordering) foreach (var unit in world.Battalions)
            {
                if (unit.Side != ArmySide.French || unit.AliveCount <= 0 || unit.Routing) continue;
                if (orders)
                {
                    Vector2 start=unit.Position;
                    foreach(var order in world.PreviewOrders(unit))
                    {
                        if(order.IsAttack)AtlasAttackOrder(start,order.Destination);
                        else AtlasOrderArrow(start,order.Destination,new Color(.55f,.73f,.94f));
                        start=order.Destination;
                    }
                }
                if (ordering && previewActive[unit.Id])
                {
                    AtlasOrderArrow(PreviewOrigin(unit), previewDestinations[unit.Id], new Color(.55f, .73f, .94f));
                }
            }
            if (commandArtillery != null) foreach (var gun in commandArtillery.Guns)
            {
                if (!AtlasPoint(gun.Position, out var p)) continue;
                if (orders && gun.Side == ArmySide.French && (gun.HasGroundTarget || gun.TargetBattalion >= 0))
                {
                    // One arrow per battery command avoids four identical cannon paths.
                    bool duplicate = false;
                    foreach (var previous in commandArtillery.Guns)
                        if (previous.Id < gun.Id && previous.UnitId == gun.UnitId && previous.TargetBattalion == gun.TargetBattalion &&
                            previous.HasGroundTarget == gun.HasGroundTarget && (!gun.HasGroundTarget || previous.GroundTarget == gun.GroundTarget))
                        { duplicate = true; break; }
                    if (!duplicate) AtlasAttackOrder(gun.Position, gun.HasGroundTarget ? gun.GroundTarget : world.Battalions[gun.TargetBattalion].Position);
                }
                Color ink = ArmyInk(gun.Side); ink.a = blend;
                EmpireTheme.Fill(new Rect(p.x - 1, p.y - 5, 2, 10), ink);
                EmpireTheme.Fill(new Rect(p.x - 3, p.y + 2, 6, 2), ink);
                if (selectedGuns[gun.Id]) EmpireTheme.Fill(new Rect(p.x - 4, p.y + 6, 8, 2), EmpireTheme.Brass);
            }
            Vector2 mouse = Event.current.mousePosition;
            mouse.y = Screen.height - mouse.y;
            int hoveredAtlasUnit = PickAtlasUnit(mouse, ArmySide.French, true);
            if (hoveredAtlasUnit < 0) hoveredAtlasUnit = PickAtlasUnit(mouse, ArmySide.British, true);
            // Selected labels get first choice of space; omit overlaps instead of stacking banners.
            for (int pass = 0; pass < 2; pass++) foreach (var unit in world.Battalions)
            {
                if (unit.AliveCount <= 0 || selected[unit.Id] != (pass == 0)) continue;
                Color ink = ArmyInk(unit.Side); ink.a = blend * (unit.Routing ? .5f : 1);
                if (!AtlasBounds(unit.Position, unit.Facing, AtlasHalfExtents(unit), out var bar)) continue;
                bool hover = hoveredAtlasUnit == unit.Id;
                string text = selected[unit.Id] || hover ? unit.Name + " · " + unit.AliveCount + (unit.Routing ? " · Routing" : "") : unit.Name.Replace("French ", "").Replace("British ", "").Replace(" Battalion", " Bn");
                var size = atlasLabel.CalcSize(new GUIContent(text));
                var label = new Rect(bar.center.x - size.x / 2 - 3, bar.y - 21, size.x + 6, 19);
                bool overlaps = false;
                for (int i = 0; i < atlasLabelCount; i++) if (atlasLabels[i].Overlaps(label)) { overlaps = true; break; }
                if (overlaps || label.y < 122 || atlasLabelCount == atlasLabels.Length) continue;
                atlasLabels[atlasLabelCount++] = label;
                atlasLabel.normal.textColor = EmpireTheme.RenderColor(ink);
                GUI.Label(label, text, atlasLabel);
            }
            DrawAtlasKey(orders);
            GUI.depth = depth;
        }
        void DrawAtlasKey(bool orders)
        {
            float alpha = AtlasBlend;
            Color ink = new Color(.24f, .25f, .19f, alpha);
            atlasLabel.normal.textColor = EmpireTheme.RenderColor(ink);
            atlasLabel.alignment = TextAnchor.MiddleLeft;
            var key = new Rect(22, 124, 188, 119);
            EmpireTheme.Fill(key, new Color(.92f, .89f, .76f, alpha * .88f));
            EmpireTheme.Fill(new Rect(key.x, key.y, key.width, 1), ink);
            GUI.Label(new Rect(32, 130, 175, 18), "BATTLE ATLAS · M", atlasLabel);
            Color blue = AtlasBlue, red = AtlasRed; blue.a = red.a = alpha;
            EmpireTheme.Fill(new Rect(32, 158, 18, 5), blue);
            EmpireTheme.Fill(new Rect(112, 158, 18, 5), red);
            GUI.Label(new Rect(55, 151, 55, 18), "France", atlasLabel);
            GUI.Label(new Rect(135, 151, 65, 18), "Britain", atlasLabel);
            GUI.Label(new Rect(32, 175, 172, 18), orders ? "Full order queue displayed" : "Hold Space · show orders", atlasLabel);
            var moveInk = new Color(.35f, .55f, .78f, alpha);
            var attackInk = new Color(.78f, .15f, .10f, alpha);
            EmpireTheme.Fill(new Rect(32, 201, 18, 2), moveInk);
            EmpireTheme.Fill(new Rect(112, 201, 18, 2), attackInk);
            GUI.Label(new Rect(55, 193, 55, 18), "Move", atlasLabel);
            GUI.Label(new Rect(135, 193, 65, 18), "Attack", atlasLabel);
            var a = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(new Vector2(focus.x, focus.z)));
            var b = battleCamera.WorldToScreenPoint(world.Terrain.SurfacePoint(new Vector2(focus.x, focus.z) + new Vector2(battleCamera.transform.right.x, battleCamera.transform.right.z) * 100));
            float width = Vector2.Distance(a, b);
            float metres = width < 20 ? 500 : width < 45 ? 200 : width < 100 ? 100 : 50;
            width *= metres / 100;
            EmpireTheme.Fill(new Rect(32, 231, width, 1), ink);
            EmpireTheme.Fill(new Rect(32, 227, 1, 5), ink);
            EmpireTheme.Fill(new Rect(32 + width, 227, 1, 5), ink);
            GUI.Label(new Rect(36 + width, 221, 60, 18), metres + " m", atlasLabel);
            atlasLabel.alignment = TextAnchor.MiddleCenter;
        }
    }
}
