using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Bounded soil overlays and low smoke anchored to the excavated surface.</summary>
    public sealed class ArtilleryGroundMarks : MonoBehaviour
    {
        public const int Capacity = 512;
        sealed class Mark
        {
            public Mesh Mesh;
            public ArtilleryImpact Impact;
            public Vector3[] Vertices;
            public Bounds Bounds;
            public int TerrainRevision;
            public Vector3 Position;
            public float Born, NextSmoke, Radius, Length;
            public bool Shell;
            public int Sequence;
        }
        readonly Mark[] marks = new Mark[Capacity];
        readonly Plane[] frustum = new Plane[6];
        BattleWorld world;
        ArtillerySystem artillery;
        MusketSmoke smoke;
        Camera cameraView;
        Material material;
        MaterialPropertyBlock properties;
        int observed, cursor, terrainRevision;
        public Func<float> PresentationTime;
        public int MarkCount { get; private set; }
        public int CraterCount { get; private set; }
        public int FurrowCount { get; private set; }
        public int SmokeEmissions { get; private set; }

        public void Initialize(BattleWorld value, ArtillerySystem guns, Camera camera, MusketSmoke clouds)
        {
            world = value; artillery = guns; cameraView = camera; smoke = clouds;
            var shader = Resources.Load<Shader>("ArtilleryScar");
            if (!shader) { Debug.LogError("Artillery scar shader missing."); enabled = false; return; }
            material = new Material(shader) { name = "Artillery earth scars" };
            properties = new MaterialPropertyBlock();
        }
        public void ConsumeImpacts(float time)
        {
            int first = Mathf.Max(observed + 1, artillery.ImpactSequence - artillery.Impacts.Length + 1);
            for (int sequence = first; sequence <= artillery.ImpactSequence; sequence++)
            {
                var impact = artillery.Impacts[(sequence - 1) % artillery.Impacts.Length];
                if (impact.Sequence != sequence || !impact.TerrainStrike || impact.AmmunitionType == ArtilleryAmmunition.Canister || ArtillerySystem.IsCloudShell(impact.AmmunitionType)) continue;
                var mark = BattleOptimizationSettings.FastForwardOptimizations ? LazyMark(impact) : BuildMark(world, impact);
                mark.NextSmoke = time + .35f;
                if (marks[cursor] != null)
                {
                    if (marks[cursor].Shell) CraterCount--; else FurrowCount--;
                    Release(marks[cursor].Mesh);
                }
                else MarkCount++;
                marks[cursor] = mark; cursor = (cursor + 1) % Capacity;
                if (mark.Shell) CraterCount++; else FurrowCount++;
            }
            observed = artillery.ImpactSequence;
        }
        static Mark LazyMark(ArtilleryImpact impact)
        {
            var shape=ArtilleryEarthwork.FromImpact(impact);
            var center=new Vector3(shape.Center.x,impact.Position.y,shape.Center.y);
            float extent=(shape.Radius+shape.HalfLength)*1.25f;
            var snapshot=new ArtilleryImpact {Position=impact.Position,Direction=impact.Direction,Speed=impact.Speed,
                Sequence=impact.Sequence,Time=impact.Time,ExplosiveShell=impact.ExplosiveShell,
                TerrainStrike=impact.TerrainStrike,AmmunitionType=impact.AmmunitionType};
            return new Mark {Impact=snapshot,Position=center,Born=impact.Time,Shell=impact.ExplosiveShell,
                Sequence=impact.Sequence,Bounds=new Bounds(center,new Vector3(extent*2,64,extent*2))};
        }
        static Mark BuildMark(BattleWorld world, ArtilleryImpact impact)
        {
            bool shell = impact.ExplosiveShell;
            var shape = ArtilleryEarthwork.FromImpact(impact);
            float radius = shape.Radius * 1.25f, length = shape.HalfLength * 1.25f;
            Vector2 direction = shape.Forward, right = shape.Right;
            Vector3 centre = new Vector3(shape.Center.x, impact.Position.y, shape.Center.y);
            int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(radius, length) * 2f / .2f) + 1, 17, 65);
            var vertices = new Vector3[n * n]; var uv = new Vector2[n * n]; var triangles = new int[(n - 1) * (n - 1) * 6]; int k = 0;
            for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                float u = x / (float)(n - 1) * 2 - 1, v = z / (float)(n - 1) * 2 - 1;
                Vector2 offset = right * (u * radius) + direction * (v * length);
                Vector2 point = new Vector2(centre.x, centre.z) + offset;
                vertices[i] = new Vector3(offset.x, world.Terrain.HeightAt(point) - centre.y + .018f, offset.y);
                uv[i] = new Vector2(u, v);
                if (x == n - 1 || z == n - 1) continue;
                triangles[k++] = i; triangles[k++] = i + n; triangles[k++] = i + 1;
                triangles[k++] = i + 1; triangles[k++] = i + n; triangles[k++] = i + n + 1;
            }
            var mesh = new Mesh { name = shell ? "Shell crater spoil and basin" : "Roundshot furrow" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var bounds = mesh.bounds; bounds.center += centre;
            return new Mark { Mesh = mesh, Vertices = vertices, Bounds = bounds, TerrainRevision = world.Terrain.DamageRevision, Position = centre, Born = impact.Time, Shell = shell, Radius = radius, Length = length, Sequence = impact.Sequence };
        }
        void LateUpdate()
        {
            using var measurement = BattleMetrics.Measure(BattleMetrics.Stage.GroundMarks);
            if (!material) return;
            float time = PresentationTime != null ? PresentationTime() : world.Time;
            ConsumeImpacts(time);
            GeometryUtility.CalculateFrustumPlanes(cameraView,frustum);
            terrainRevision = world.Terrain.DamageRevision;
            foreach (var mark in marks)
            {
                if (mark == null) continue;
                bool visible=(cameraView.transform.position-mark.Position).sqrMagnitude<=1200*1200 &&
                    (!BattleOptimizationSettings.FastForwardOptimizations || GeometryUtility.TestPlanesAABB(frustum,mark.Bounds));
                if(visible && mark.Mesh==null)
                {
                    var geometry=BuildMark(world,mark.Impact);
                    mark.Mesh=geometry.Mesh;mark.Vertices=geometry.Vertices;mark.Bounds=geometry.Bounds;mark.TerrainRevision=geometry.TerrainRevision;
                }
                if (mark.Mesh && (visible || !BattleOptimizationSettings.FastForwardOptimizations) &&
                    mark.TerrainRevision!=world.Terrain.DamageRevision && world.Terrain.DamageChangedIn(mark.Bounds, mark.TerrainRevision))
                {
                    var vertices = mark.Vertices;
                    for (int i = 0; i < vertices.Length; i++)
                        vertices[i].y = world.Terrain.HeightAt(new Vector2(vertices[i].x + mark.Position.x, vertices[i].z + mark.Position.z)) - mark.Position.y + .018f;
                    mark.Mesh.vertices = vertices; mark.Mesh.RecalculateNormals(); mark.Mesh.RecalculateBounds();
                    mark.Bounds=mark.Mesh.bounds;mark.Bounds.center+=mark.Position;
                }
                if(visible || !BattleOptimizationSettings.FastForwardOptimizations)mark.TerrainRevision = terrainRevision;
                float age = time - mark.Born;
                if (mark.Shell && age < 90 && time >= mark.NextSmoke)
                {
                    // A low, intermittent dirty plume after the explosive burst. No catch-up burst after a hitch.
                    mark.NextSmoke = time + .55f + (mark.Sequence % 3) * .12f;
                    float strength = 1f - Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(35f,90f,age));
                    if (smoke && (cameraView.transform.position - mark.Position).sqrMagnitude < 225 * 225)
                    {
                        Vector3 position = mark.Position;
                        position.y = world.Terrain.HeightAt(new Vector2(position.x, position.z)) + .06f;
                        smoke.EmitFireSmoke(position+Vector3.up*.25f,time,1.65f,strength,true);
                        SmokeEmissions++;
                    }
                }
                if (!visible) continue;
                properties.SetVector("_ScarData", new Vector4(mark.Shell ? 1 : 0, mark.Sequence * 1.371f, age, 0));
                Graphics.DrawMesh(mark.Mesh, Matrix4x4.Translate(mark.Position), material, 0, cameraView, 0, properties, ShadowCastingMode.Off, false);
            }
        }
        public static Mesh BuildVerificationMesh(BattleWorld world, ArtilleryImpact impact)
        {
            var mark = BuildMark(world, impact); var vertices = mark.Mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] += mark.Position - impact.Position;
            mark.Mesh.vertices = vertices; mark.Mesh.RecalculateBounds(); return mark.Mesh;
        }
        static void Release(UnityEngine.Object value)
        { if(Application.isPlaying)Destroy(value);else DestroyImmediate(value); }
        void OnDestroy()
        {
            foreach (var mark in marks) if (mark != null && mark.Mesh) Release(mark.Mesh);
            if (material) Release(material);
        }
    }
}
