using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Collections;
using Unity.Jobs;

namespace EmpireBattle
{
    // Static broadphase tiles: a crater only recooks its small tile and fine damage cells,
    // never the entire 131,072-triangle battlefield collider.
    public sealed class RagdollGround : IDisposable
    {
        const int Cells=16, Side=(BattlefieldTerrain.Resolution-1)/Cells;
        struct Tile { public Mesh Mesh; public MeshCollider Collider; }
        readonly BattlefieldTerrain terrain;
        readonly Scene scene;
        readonly Tile[] tiles=new Tile[Side*Side];
        readonly bool[] dirty=new bool[Side*Side];
        readonly List<int> indices=new List<int>(Cells*Cells*6);
        struct Cut { public MeshCollider Collider; public int Revision; }
        readonly Dictionary<Vector2Int,Cut> cuts=new Dictionary<Vector2Int,Cut>();
        readonly HashSet<Vector2Int> desired=new HashSet<Vector2Int>();
        readonly List<Vector2Int> removed=new List<Vector2Int>();
        // Preserve Unity's default cleaning/welding and simulation-quality options.
        const MeshColliderCookingOptions Cooking=MeshColliderCookingOptions.CookForFasterSimulation |
            MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices |
            MeshColliderCookingOptions.UseFastMidphase;
        readonly List<Tile> pending=new List<Tile>(64);
        NativeArray<EntityId> bakeIds=new NativeArray<EntityId>((BattlefieldTerrain.Resolution-1)*(BattlefieldTerrain.Resolution-1)+Side*Side,Allocator.Persistent);
        struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<EntityId> Ids;
            public void Execute(int index) { Physics.BakeMesh(Ids[index],false,Cooking); }
        }
        int revision;
        bool localized;
        public int LastRebuiltTiles {get;private set;}
        public int FineColliderCount=>cuts.Count;
        public RagdollGround(BattlefieldTerrain value,Scene target)
        {
            terrain=value;scene=target;
            const int n=Cells+1;
            for(int z=0;z<Side;z++)for(int x=0;x<Side;x++)
            {
                var vertices=new Vector3[n*n];
                for(int vz=0;vz<n;vz++)for(int vx=0;vx<n;vx++)
                {
                    var p=new Vector2(-terrain.HalfSize+(x*Cells+vx)*terrain.CellSize,-terrain.HalfSize+(z*Cells+vz)*terrain.CellSize);
                    vertices[vz*n+vx]=new Vector3(p.x,terrain.BaseHeightAt(p),p.y);
                }
                var tile=new Tile {Mesh=new Mesh{name="Casualty ground tile"},Collider=CreateCollider("Casualty ground tile")};
                tile.Mesh.vertices=vertices;tiles[z*Side+x]=tile;RebuildTile(x,z);
            }
            Update();
        }
        MeshCollider CreateCollider(string name)
        {
            var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);
            var collider=go.AddComponent<MeshCollider>();collider.cookingOptions=Cooking;return collider;
        }
        void RebuildTile(int x,int z,bool immediate=true)
        {
            indices.Clear();const int n=Cells+1;
            for(int vz=0;vz<Cells;vz++)for(int vx=0;vx<Cells;vx++)
            {
                if(cuts.ContainsKey(new Vector2Int(x*Cells+vx,z*Cells+vz)))continue;
                int i=vz*n+vx;indices.Add(i);indices.Add(i+n);indices.Add(i+1);indices.Add(i+1);indices.Add(i+n);indices.Add(i+n+1);
            }
            var tile=tiles[z*Side+x];tile.Collider.sharedMesh=null;
            tile.Mesh.SetTriangles(indices,0);
            if(indices.Count>0)
            {
                if(immediate)tile.Collider.sharedMesh=tile.Mesh;
                else pending.Add(tile);
            }
        }
        public void Update() => Update(null,0);
        // Fine collision geometry is needed only where active bodies can contact it.
        // Coarse tiles remain solid elsewhere; entering a crater installs its exact
        // current geometry before simulation, even if its revision is old.
        public void Update(Bounds[] regions,int count)
        {
            LastRebuiltTiles=0;
            if(regions==null&&!localized&&revision==terrain.DamageRevision)return;
            localized=regions!=null;
            desired.Clear();
            if(regions==null)
            {
                foreach(var cell in terrain.DamageCells.Keys)if(terrain.HasDamage(cell))desired.Add(cell);
            }
            else for(int region=0;region<count;region++)
            {
                var bounds=regions[region];
                int minX=Mathf.Max(0,Mathf.FloorToInt((bounds.min.x+terrain.HalfSize)/terrain.CellSize));
                int minZ=Mathf.Max(0,Mathf.FloorToInt((bounds.min.z+terrain.HalfSize)/terrain.CellSize));
                int maxX=Mathf.Min(BattlefieldTerrain.Resolution-2,Mathf.FloorToInt((bounds.max.x+terrain.HalfSize)/terrain.CellSize));
                int maxZ=Mathf.Min(BattlefieldTerrain.Resolution-2,Mathf.FloorToInt((bounds.max.z+terrain.HalfSize)/terrain.CellSize));
                for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)
                {
                    var cell=new Vector2Int(x,z);if(terrain.HasDamage(cell))desired.Add(cell);
                }
            }
            pending.Clear();removed.Clear();Array.Clear(dirty,0,dirty.Length);
            foreach(var entry in cuts)if(!desired.Contains(entry.Key))removed.Add(entry.Key);
            foreach(var cell in removed)
            {
                var cut=cuts[cell];cut.Collider.sharedMesh=null;
                terrain.ReleaseDamageMesh(cell);Destroy(cut.Collider.gameObject);cuts.Remove(cell);
                dirty[(cell.y/Cells)*Side+cell.x/Cells]=true;
            }
            foreach(var cell in desired)
            {
                int changed=terrain.DamageCells[cell];
                bool existed=cuts.TryGetValue(cell,out var cut);
                if(existed&&cut.Revision==changed)continue;
                if(!existed){cut.Collider=CreateCollider("Excavated casualty ground");dirty[(cell.y/Cells)*Side+cell.x/Cells]=true;}
                cut.Collider.sharedMesh=null;
                var mesh=existed?terrain.RefreshDamageMesh(cell):terrain.AcquireDamageMesh(cell);
                cut.Revision=changed;cuts[cell]=cut;pending.Add(new Tile{Mesh=mesh,Collider=cut.Collider});
            }
            for(int i=0;i<dirty.Length;i++)if(dirty[i]){RebuildTile(i%Side,i/Side,false);LastRebuiltTiles++;}
            if(pending.Count>0)
            {
                using var scope=BattleMetrics.Measure(BattleMetrics.Stage.GroundBake);
                if(BattleOptimizationSettings.HitchOptimizations)
                {
                    for(int i=0;i<pending.Count;i++)bakeIds[i]=pending[i].Mesh.GetEntityId();
                    new BakeJob{Ids=bakeIds}.Schedule(pending.Count,1).Complete();
                }
                foreach(var tile in pending)tile.Collider.sharedMesh=tile.Mesh;
            }
            revision=terrain.DamageRevision;
        }
        static void Destroy(UnityEngine.Object item)
        {
            if(!item)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(item);else UnityEngine.Object.DestroyImmediate(item);
        }
        public void Dispose()
        {
            foreach(var tile in tiles){if(tile.Collider)tile.Collider.sharedMesh=null;Destroy(tile.Mesh);if(tile.Collider)Destroy(tile.Collider.gameObject);}
            foreach(var entry in cuts){var collider=entry.Value.Collider;if(collider){collider.sharedMesh=null;Destroy(collider.gameObject);}terrain.ReleaseDamageMesh(entry.Key);}
            cuts.Clear();
            if(bakeIds.IsCreated)bakeIds.Dispose();
        }
    }
}
