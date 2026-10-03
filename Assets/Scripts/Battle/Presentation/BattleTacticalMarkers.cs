using System;
using Empire.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Screen-space national flags and terrain-following selection bases.</summary>
    public sealed class BattleTacticalMarkers : IDisposable
    {
        public float AtlasBlend;
        readonly BattleWorld world;
        readonly Camera camera;
        readonly bool[] selected;
        readonly Rect[] flags;
        readonly Vector2[] anchors;
        readonly float[] opacity;
        readonly Texture2D frenchFlag, britishFlag;
        readonly Mesh ring, cube;
        readonly Material groundMaterial;
        readonly MaterialPropertyBlock atlasProperties = new MaterialPropertyBlock();
        readonly Matrix4x4[] matrices = new Matrix4x4[1023];
        GUIStyle label;
        readonly Vector3[] rangeLabelPoints = new Vector3[256];
        readonly string[] rangeLabels = new string[256];
        int rangeLabelCount;
        string effectiveLabel, maximumLabel;
        float lastEffectiveRange = -1;

        public BattleTacticalMarkers(BattleWorld world, Camera camera, bool[] selected, Mesh cube)
        {
            this.world = world; this.camera = camera; this.selected = selected; this.cube = cube;
            flags = new Rect[selected.Length]; anchors = new Vector2[selected.Length]; opacity = new float[selected.Length];
            frenchFlag = world.Config.CampaignDeployment?CreateCampaignBanner(world.Config.PlayerCountryColor):CreateFlag(true); britishFlag = world.Config.CampaignDeployment?CreateCampaignBanner(world.Config.EnemyCountryColor):CreateFlag(false); ring = CreateRing();
            groundMaterial = new Material(Resources.Load<Material>("BattleSelectionVariant")) { name = "Selected troop bases", enableInstancing = true };
            groundMaterial.SetColor("_BaseColor", EmpireTheme.Brass);
        }

        static Texture2D CreateCampaignBanner(Color color)
        {
            var t=new Texture2D(32,20,TextureFormat.RGBA32,false){name="Campaign force banner"};
            var pixels=new Color[640];for(int i=0;i<pixels.Length;i++)pixels[i]=EmpireTheme.RenderColor(color);
            t.SetPixels(pixels);t.Apply(false,true);return t;
        }
        static Texture2D CreateFlag(bool french)
        {
            const int width = 96, height = 60;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = french ? "France tricolour" : "Britain Union 1707", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color blue = new Color(.055f,.16f,.37f), red = new Color(.74f,.12f,.16f), white = new Color(.96f,.94f,.89f);
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                Color color;
                if (french) color = x < width / 3 ? blue : x < width * 2 / 3 ? white : red;
                else
                {
                    // The 1800 scenario uses the pre-1801 Union, before the red Irish saltire.
                    // Reference: flaginstitute.org/wp/uk-flags/union-flag-history/
                    float u = (x + .5f) / width - .5f, v = (y + .5f) / height - .5f;
                    color = Mathf.Abs(v - u) < .10f || Mathf.Abs(v + u) < .10f ? white : blue;
                    if (Mathf.Abs(u) < .135f || Mathf.Abs(v) < .18f) color = white;
                    if (Mathf.Abs(u) < .08f || Mathf.Abs(v) < .105f) color = red;
                }
                texture.SetPixel(x, y, EmpireTheme.RenderColor(color));
            }
            texture.Apply(false, true); return texture;
        }

        static Mesh CreateRing()
        {
            const int segments = 24;
            var vertices = new Vector3[segments * 2]; var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = direction * .62f; vertices[i * 2 + 1] = direction * .43f;
                int next = (i + 1) % segments;
                int t = i * 6; triangles[t] = i * 2; triangles[t + 1] = next * 2; triangles[t + 2] = i * 2 + 1;
                triangles[t + 3] = i * 2 + 1; triangles[t + 4] = next * 2; triangles[t + 5] = next * 2 + 1;
            }
            var mesh = new Mesh { name = "Troop selection ring", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds(); return mesh;
        }

        public void UpdateLayout(float bottom)
        {
            rangeLabelCount = 0;
            Array.Clear(opacity, 0, opacity.Length);
            // Keep every flag anchored to its actual formation. Selected units win crowded space.
            for (int pass = 0; pass < 2; pass++) foreach (var unit in world.Battalions)
            {
                if (unit.AliveCount == 0 || selected[unit.Id] != (pass == 0)) continue;
                Vector3 point = camera.WorldToScreenPoint(world.Terrain.SurfacePoint(unit.Position, 5));
                if (point.z <= 0 || point.x < 20 || point.x > Screen.width - 20) continue;
                float alpha = selected[unit.Id] ? 1 : Mathf.SmoothStep(0, 1, Mathf.InverseLerp(170, 330, point.z));
                alpha *= Mathf.Pow(1 - AtlasBlend, 3);
                if (alpha < .05f) continue;
                var anchor = new Vector2(point.x, Screen.height - point.y);
                if (anchor.y < 90 || anchor.y > bottom) continue;
                float width = selected[unit.Id] ? 40 : 32, height = selected[unit.Id] ? 29 : 24;
                var rect = new Rect(anchor.x - width / 2, anchor.y - height - 12, width, height);
                if (rect.y < 88) continue;
                bool overlaps = false;
                for (int i = 0; i < flags.Length; i++)
                    if (opacity[i] > 0 && flags[i].Overlaps(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4)))
                    { overlaps = true; break; }
                // Omit a crowded banner rather than move it away from the regiment.
                // The formation itself remains selectable and atlas bars take over on zoom-out.
                if (overlaps) continue;
                flags[unit.Id] = rect; anchors[unit.Id] = anchor; opacity[unit.Id] = alpha;
            }
        }

        public int Pick(Vector2 screenPoint, ArmySide side, bool includeRouting = false)
        {
            var gui = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            foreach (var unit in world.Battalions)
                if (unit.Side == side && unit.AliveCount > 0 && (includeRouting || !unit.Routing) && opacity[unit.Id] > .25f && flags[unit.Id].Contains(gui)) return unit.Id;
            return -1;
        }

        public void DrawFlags()
        {
            if (label == null) label = EmpireTheme.Text(12, EmpireTheme.Ivory);
            Color old = GUI.color; int oldDepth = GUI.depth; GUI.depth = 10;
            foreach (var unit in world.Battalions)
            {
                float alpha = opacity[unit.Id]; if (alpha <= 0) continue;
                Rect rect = flags[unit.Id]; bool picked = selected[unit.Id];
                Color accent = picked ? EmpireTheme.Brass : unit.Routing ? EmpireTheme.Danger : EmpireTheme.Line;
                accent.a = alpha;
                EmpireTheme.Fill(new Rect(rect.center.x - .5f, rect.yMax, 1, anchors[unit.Id].y - rect.yMax), accent);
                EmpireTheme.Fill(rect, accent);
                GUI.color = new Color(1, 1, 1, alpha);
                GUI.DrawTexture(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 6), unit.Side == ArmySide.French ? frenchFlag : britishFlag);
                GUI.color = old;
                EmpireTheme.Fill(new Rect(rect.x + 2, rect.yMax - 3, (rect.width - 4) * unit.AliveCount / Mathf.Max(1f, unit.SoldierCount), 2), picked ? EmpireTheme.Brass : EmpireTheme.Muted);

            }
            for (int i = 0; i < rangeLabelCount; i++)
            {
                var point = camera.WorldToScreenPoint(rangeLabelPoints[i]);
                if (point.z <= 0) continue;
                float y = Screen.height - point.y;
                if (y < 90 || y > Screen.height - 80 || point.x < 50 || point.x > Screen.width - 50) continue;
                var rect = new Rect(point.x - 90, y - 20, 180, 20);
                label.alignment = TextAnchor.MiddleCenter;
                // Text only: never cover the battlefield with label panels.
                GUI.Label(rect, rangeLabels[i], label);
                label.alignment = TextAnchor.MiddleLeft;
            }
            GUI.color = old; GUI.depth = oldDepth;
        }

        public void DrawSelection(BattalionState unit)
        {
            if (unit.AliveCount == 0) return;
            int count = 0;
            float cameraDistance = Vector3.Distance(camera.transform.position, world.Terrain.SurfacePoint(unit.Position));
            // Individual bases follow actual soldiers, including while the formation is reforming.
            if (cameraDistance < 350)
            {
                for (int i = unit.SoldierStart; i < unit.SoldierStart + unit.SoldierCount; i++)
                {
                    if (!world.Soldiers.Alive[i]) continue;
                    var point = world.Soldiers.Positions[i];
                    float dx = world.Terrain.HeightAt(point + Vector2.right * .5f) - world.Terrain.HeightAt(point - Vector2.right * .5f);
                    float dz = world.Terrain.HeightAt(point + Vector2.up * .5f) - world.Terrain.HeightAt(point - Vector2.up * .5f);
                    var normal = new Vector3(-dx, 1, -dz).normalized;
                    matrices[count++] = Matrix4x4.TRS(world.Terrain.SurfacePoint(point, .10f), Quaternion.FromToRotation(Vector3.up, normal), Vector3.one);
                    if (count == matrices.Length) { Draw(ring, count); count = 0; }
                }
                if (count > 0) Draw(ring, count);
            }
            count = 0;
            var half = FormationMath.HalfExtents(unit) + Vector2.one;
            var right = FormationMath.Right(unit.Facing); var forward = FormationMath.Forward(unit.Facing);
            float thickness = Mathf.Clamp(cameraDistance * .0018f, .25f, 4f);
            // Small terrain-following segments prevent the perimeter cutting through hills.
            for (int edge = 0; edge < 4; edge++)
            {
                Vector2 tangent = edge < 2 ? right : forward;
                Vector2 center = unit.Position + (edge < 2 ? forward * half.y : right * half.x) * (edge % 2 == 0 ? 1 : -1);
                float length = 2 * (edge < 2 ? half.x : half.y);
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / 2));
                float segment = length / steps;
                for (int i = 0; i < steps; i++)
                {
                    var point = center + tangent * (-length / 2 + (i + .5f) * segment);
                    matrices[count++] = Matrix4x4.TRS(world.Terrain.SurfacePoint(point, .12f), Quaternion.Euler(0, FormationMath.FacingOf(tangent) * Mathf.Rad2Deg, 0), new Vector3(thickness, .10f, segment + .05f));
                    if (count == matrices.Length) { Draw(cube, count); count = 0; }
                }
            }
            if (count > 0) Draw(cube, count);
        }

        public void DrawAtlasFormation(Vector2 center, float facing, Vector2 half, Color ink, bool picked, bool preview, bool cavalry)
        {
            // Use the formation's world footprint at every zoom. The GPU clips partial
            // formations normally, even when their center is outside the view.
            float width = ProjectileVisibility.Width(camera, world.Terrain.SurfacePoint(center), 2, .12f, 24);
            if (picked)
            {
                atlasProperties.SetColor("_BaseColor", new Color(.92f, .74f, .25f, ink.a));
                AtlasPerimeter(center, facing, half + Vector2.one * width * 2, width, false);
            }
            atlasProperties.SetColor("_BaseColor", ink);
            AtlasPerimeter(center, facing, half, width, preview);
            // Mark the actual leading edge and cavalry diagonal on the same surface.
            Vector2 right = FormationMath.Right(facing) * half.x;
            Vector2 forward = FormationMath.Forward(facing) * half.y;
            int count = 0;
            AtlasLine(center + forward - right, center + forward + right, width * 1.6f, preview, ref count);
            if (cavalry) AtlasLine(center - forward - right, center + forward + right, width, preview, ref count);
            if (count > 0) DrawAtlasSegments(count);
        }

        void AtlasPerimeter(Vector2 center, float facing, Vector2 half, float width, bool dashed)
        {
            Vector2 right = FormationMath.Right(facing) * half.x;
            Vector2 forward = FormationMath.Forward(facing) * half.y;
            int count = 0;
            AtlasLine(center - right - forward, center + right - forward, width, dashed, ref count);
            AtlasLine(center + right - forward, center + right + forward, width, dashed, ref count);
            AtlasLine(center + right + forward, center - right + forward, width, dashed, ref count);
            AtlasLine(center - right + forward, center - right - forward, width, dashed, ref count);
            if (count > 0) DrawAtlasSegments(count);
        }

        void AtlasLine(Vector2 from, Vector2 to, float width, bool dashed, ref int count)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 2f));
            for (int i = 0; i < steps; i++)
            {
                if (dashed && i % 4 >= 2) continue;
                Vector3 a = world.Terrain.SurfacePoint(Vector2.Lerp(from, to, i / (float)steps), .12f);
                Vector3 b = world.Terrain.SurfacePoint(Vector2.Lerp(from, to, (i + 1f) / steps), .12f);
                Vector3 delta = b - a;
                if (delta.sqrMagnitude < .0001f) continue;
                matrices[count++] = Matrix4x4.TRS((a + b) * .5f, Quaternion.LookRotation(delta), new Vector3(width, .08f, delta.magnitude + .04f));
                if (count == matrices.Length) { DrawAtlasSegments(count); count = 0; }
            }
        }

        void DrawAtlasSegments(int count) => Graphics.DrawMeshInstanced(cube, 0, groundMaterial, matrices, count, atlasProperties, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);

        void AddRangeLabel(Vector2 point, string text)
        {
            if (rangeLabelCount == rangeLabels.Length) return;
            rangeLabelPoints[rangeLabelCount] = world.Terrain.SurfacePoint(point, .35f);
            rangeLabels[rangeLabelCount++] = text;
        }

        public void DrawInfantryRange(BattalionState unit)
        {
            if(unit.Kind==UnitKind.Cavalry)return;
            if (unit.Routing || unit.AliveCount == 0) return;
            float maximum = world.AimedRange(unit);
            float effective = Mathf.Clamp(world.EffectiveRange(unit), 0, maximum);
            if (effective != lastEffectiveRange)
            {
                lastEffectiveRange = effective;
                effectiveLabel = effective.ToString("0") + " m effective";
                maximumLabel = maximum.ToString("0") + " m limit";
            }
            int count = 0;
            Vector2 forward = FormationMath.Forward(unit.Facing);
            Vector2 rangeOrigin = unit.Position;
            if (unit.Formation == FormationKind.Square)
            {
                RangeArc(unit.Position,unit.Facing,Mathf.PI,effective,false,ref count);
                if(maximum>effective+1)RangeArc(unit.Position,unit.Facing,Mathf.PI,maximum,true,ref count);
            }
            else
            {
                Vector2 half = FormationMath.HalfExtents(unit);
                rangeOrigin += forward * half.y;
                Vector2 right = FormationMath.Right(unit.Facing);
                Vector2 leftEdge = rangeOrigin - right * half.x;
                Vector2 rightEdge = rangeOrigin + right * half.x;
                float halfAngle = WeaponBallistics.FireHalfAngle;
                // Envelope of the firing sectors along the front rank: two rounded
                // shoulders joined across the frontage, with rays from the outer edges.
                RangeFan(leftEdge,rightEdge,unit.Facing,halfAngle,effective,false,ref count);
                if(maximum>effective+1)RangeFan(leftEdge,rightEdge,unit.Facing,halfAngle,maximum,true,ref count);
                for(int sign=-1;sign<=1;sign+=2)
                {
                    Vector2 edge=sign<0?leftEdge:rightEdge;
                    Vector2 direction=FormationMath.Forward(unit.Facing+sign*halfAngle);
                    RangeLine(edge,edge+direction*effective,false,ref count);
                    RangeLine(edge+direction*effective,edge+direction*maximum,true,ref count);
                }
            }
            if(count>0)Draw(cube,count);
            AddRangeLabel(rangeOrigin+forward*effective,effectiveLabel);
            if(maximum>effective+1)AddRangeLabel(rangeOrigin+forward*maximum,maximumLabel);
        }

        void RangeFan(Vector2 leftEdge,Vector2 rightEdge,float facing,float halfAngle,float radius,bool dashed,ref int count)
        {
            // Each shoulder sweeps only its own half of the arc. The front segment
            // represents equal forward range from every file, not a central apex.
            RangeArc(leftEdge,facing-halfAngle*.5f,halfAngle*.5f,radius,dashed,ref count);
            Vector2 forward=FormationMath.Forward(facing)*radius;
            RangeLine(leftEdge+forward,rightEdge+forward,dashed,ref count);
            RangeArc(rightEdge,facing+halfAngle*.5f,halfAngle*.5f,radius,dashed,ref count);
        }

        public void DrawArtilleryRange(ArtilleryGun gun)
        {
            float range = ArtillerySystem.EffectiveRange(gun);
            int count = 0;
            // Guns traverse through 360 degrees: do not imply a fixed infantry-style fire cone.
            RangeArc(gun.Position, gun.Facing, Mathf.PI, range, true, ref count);
            RangeArc(gun.Position, gun.Facing, Mathf.PI, 8, false, ref count);
            RangeLine(gun.Position, gun.Position + FormationMath.Forward(gun.Facing) * range, false, ref count);
            if (count > 0) Draw(cube, count);
            AddRangeLabel(gun.Position + FormationMath.Forward(gun.Facing) * range,
                gun.AmmunitionType == ArtilleryAmmunition.Canister ? "220 m · Canister" : "900 m · Gun range");
        }

        void RangeArc(Vector2 origin, float facing, float halfAngle, float radius, bool dashed, ref int count)
        {
            int steps = Mathf.Max(8, Mathf.CeilToInt(2 * halfAngle * radius / 4f));
            for (int i = 0; i < steps; i++)
            {
                if (dashed && i % 2 != 0) continue;
                float a = facing - halfAngle + 2 * halfAngle * i / steps;
                float b = facing - halfAngle + 2 * halfAngle * (i + 1) / steps;
                RangeSegment(origin + FormationMath.Forward(a) * radius, origin + FormationMath.Forward(b) * radius, ref count);
            }
        }
        void RangeLine(Vector2 from, Vector2 to, bool dashed, ref int count)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 4f));
            for (int i = 0; i < steps; i++)
            {
                if (dashed && i % 2 != 0) continue;
                RangeSegment(Vector2.Lerp(from, to, i / (float)steps), Vector2.Lerp(from, to, (i + 1f) / steps), ref count);
            }
        }
        void RangeSegment(Vector2 from, Vector2 to, ref int count)
        {
            float limit = world.Config.FieldHalfSize;
            if (Mathf.Abs(from.x) > limit || Mathf.Abs(from.y) > limit || Mathf.Abs(to.x) > limit || Mathf.Abs(to.y) > limit) return;
            Vector3 a = world.Terrain.SurfacePoint(from, .16f), b = world.Terrain.SurfacePoint(to, .16f);
            Vector3 delta = b - a;
            if (delta.sqrMagnitude < .0001f) return;
            float width = ProjectileVisibility.Width(camera, (a + b) * .5f, 1.25f, .16f, 2.5f);
            matrices[count++] = Matrix4x4.TRS((a + b) * .5f, Quaternion.LookRotation(delta), new Vector3(width, .08f, delta.magnitude + .04f));
            if (count == matrices.Length) { Draw(cube, count); count = 0; }
        }

        void Draw(Mesh mesh, int count) => Graphics.DrawMeshInstanced(mesh, 0, groundMaterial, matrices, count, null, ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
        public void Dispose()
        {
            UnityEngine.Object.Destroy(frenchFlag); UnityEngine.Object.Destroy(britishFlag);
            UnityEngine.Object.Destroy(ring); UnityEngine.Object.Destroy(groundMaterial);
        }
    }
}
