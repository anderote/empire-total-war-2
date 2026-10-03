using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Bounded ballistic clods with rotation, impact bounce and resting spoil.</summary>
    public sealed class ArtilleryDebris
    {
        struct Clod { public Vector3 Position, Velocity, Scale; public Quaternion Rotation; public Vector3 Spin; public float End, Drag; public int Bounces, Kind; }
        readonly Clod[] clods = new Clod[4096];
        readonly Matrix4x4[][] matrices = { new Matrix4x4[256], new Matrix4x4[256], new Matrix4x4[256] };
        readonly int[] counts = new int[3];
        readonly BattleWorld world;
        readonly Mesh mesh;
        readonly Material[] materials;
        int cursor;
        float previousTime = -1;
        public ArtilleryDebris(BattleWorld world, Mesh mesh, Material soil, Material wood, Material ember) { this.world = world; this.mesh = mesh; materials = new[] { soil, wood, ember }; }
        static float Rand(int seed, int salt) => RifleFireTiming.Variation(seed, salt);
        public void Emit(ArtilleryImpact hit, float emphasis = 0f)
        {
            if (!hit.TerrainStrike || ArtillerySystem.IsCloudShell(hit.AmmunitionType)) return;
            int count = hit.AmmunitionType == ArtilleryAmmunition.Canister ? 3 : hit.AmmunitionType == ArtilleryAmmunition.Carcass ? 24 : hit.ExplosiveShell ? 80 : 20;
            emphasis=Mathf.Clamp01(emphasis);
            count=Mathf.Clamp(Mathf.RoundToInt(count*Mathf.Lerp(1f,1.75f,emphasis)*BattleEffectTuning.Current.DebrisAmount),0,clods.Length);
            var shape = ArtilleryEarthwork.FromImpact(hit);
            for (int i = 0; i < count; i++)
            {
                int seed = hit.Sequence * 97 + i;
                float a = Rand(seed, 31) * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                float speed = hit.ExplosiveShell ? Mathf.Lerp(3, 15, Rand(seed, 5)) : Mathf.Lerp(1, 5, Rand(seed, 5));
                Vector2 p = new Vector2(hit.Position.x, hit.Position.z) + new Vector2(radial.x, radial.z) * shape.Radius * Rand(seed, 6) * .6f;
                float size = (hit.ExplosiveShell?ArtilleryEffectShape.ClodRadius(seed,i,count):Mathf.Lerp(.055f, .27f, Mathf.Pow(Rand(seed, 9), 2f))) * (hit.AmmunitionType == ArtilleryAmmunition.Canister ? .35f : 1f);
                if(hit.ExplosiveShell)speed*=Mathf.Lerp(1.15f,.55f,Mathf.InverseLerp(.035f,.55f,size));
                size*=Mathf.Lerp(1f,1.2f,emphasis); speed*=Mathf.Lerp(1f,1.2f,emphasis);
                clods[cursor] = new Clod { Position = world.Terrain.SurfacePoint(p, .08f),
                    Velocity = radial * speed + Vector3.up * speed * Mathf.Lerp(hit.Detonates?1f:.7f, hit.Detonates?1.85f:1.5f, Rand(seed, 10))
                        + (hit.ExplosiveShell ? Vector3.zero : new Vector3(hit.Direction.x, 0, hit.Direction.z) * Mathf.Min(hit.Speed * .075f, 13f)),
                    Drag=hit.ExplosiveShell?Mathf.Lerp(.15f,1.2f,Mathf.Clamp01(.035f/size)):0f,
                    Scale = new Vector3(size, size * Mathf.Lerp(.5f, 1.1f, Rand(seed, 7)), size * Mathf.Lerp(.65f,1.2f,Rand(seed,13))), Rotation = Quaternion.Euler(seed, seed * 7, seed * 3),
                    Spin = new Vector3(90 + seed % 200, seed % 130, 160)*Mathf.Clamp(.09f/size,.35f,2f), End = hit.Time + 12f + Rand(seed, 11) * 8f };
                cursor = (cursor + 1) % clods.Length;
            }
        }
        public void EmitSparks(Vector3 origin, Vector3 velocity, float time, int seed, int count, bool wood = false)
        {
            for (int i = 0; i < count; i++)
            {
                int key = seed * 131 + i; float size = wood ? .08f + Rand(key,3) * .1f : .025f + Rand(key,3) * .035f;
                clods[cursor] = new Clod { Position = origin,
                    Velocity = velocity * Mathf.Lerp(.3f,1f,Rand(key,4)) + new Vector3(Rand(key,5)-.5f,Rand(key,6),Rand(key,7)-.5f) * (wood ? 8f : 3f),
                    Rotation = Quaternion.Euler(key,key*3,key*7), Scale = new Vector3(size,size * (wood ? 3f : 1f),size),
                    Spin = Vector3.one * 240f, End = time + (wood ? 8f : .5f + Rand(key,8) * 1.2f), Kind = wood ? 1 : 2 };
                cursor = (cursor + 1) % clods.Length;
            }
        }
        public void Draw(float time, Camera camera)
        {
            float dt = previousTime < 0 ? 0 : Mathf.Max(0, time - previousTime); previousTime = time;
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / .025f), 1, 64); float step = Mathf.Min(dt, 1.6f) / steps;
            for (int i = 0; i < counts.Length; i++) counts[i] = 0;
            for (int i = 0; i < clods.Length; i++)
            {
                var clod = clods[i]; if (clod.End <= time || clod.End <= 0) continue;
                for (int j = 0; j < steps && clod.Bounces < 3; j++)
                {
                    clod.Position += clod.Velocity * step + Vector3.down * (4.905f * step * step);
                    clod.Velocity += Vector3.down * (9.81f * step);
                    clod.Velocity /= 1f+clod.Drag*step;
                    clod.Rotation = (clod.Rotation * Quaternion.Euler(clod.Spin * step)).normalized;
                    float h = world.Terrain.HeightAt(new Vector2(clod.Position.x, clod.Position.z)) + clod.Scale.y * .5f;
                    if (clod.Position.y < h)
                    {
                        clod.Position.y = h; clod.Velocity = new Vector3(clod.Velocity.x * .45f, Mathf.Abs(clod.Velocity.y) * .25f, clod.Velocity.z * .45f);
                        clod.Spin *= .4f; clod.Bounces++; if (clod.Velocity.sqrMagnitude < .5f) clod.Bounces = 3;
                    }
                }
                clods[i] = clod;
                if ((camera.transform.position - clod.Position).sqrMagnitude > 350f * 350f) continue;
                int kind = clod.Kind;
                matrices[kind][counts[kind]++] = Matrix4x4.TRS(clod.Position, clod.Rotation, clod.Scale);
                if (counts[kind] == matrices[kind].Length) { Render(kind, counts[kind], camera); counts[kind] = 0; }
            }
            for (int kind = 0; kind < counts.Length; kind++) if (counts[kind] > 0) Render(kind, counts[kind], camera);
        }
        void Render(int kind, int count, Camera camera) => Graphics.DrawMeshInstanced(mesh, 0, materials[kind], matrices[kind], count, null, kind == 2 ? ShadowCastingMode.Off : ShadowCastingMode.On, kind != 2, 0, camera);
    }
}
