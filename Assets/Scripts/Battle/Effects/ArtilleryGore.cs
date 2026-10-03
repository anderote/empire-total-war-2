using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Lethal artillery cuts the actual posed mesh; strong shells partition the complete body.
    public sealed class ArtilleryGore : IDisposable
    {
        sealed class Cut { public Mesh Body; public int[] Detached, Boundary; }
        sealed class Piece
        {
            public Mesh Mesh; public Material Uniform;
            public Vector3 Position, Velocity, Spin; public Quaternion Rotation;
            public float Age, Radius, TrailTime; public Vector3 Wound; public bool Settled;
        }
        readonly Dictionary<InfantryRagdollSet,Cut[]> cuts=new Dictionary<InfantryRagdollSet,Cut[]>();
        readonly Dictionary<InfantryRagdollSet,Cut[]> fragments=new Dictionary<InfantryRagdollSet,Cut[]>();
        readonly List<Piece> pieces=new List<Piece>();
        readonly Plane[] frustum=new Plane[6];
        readonly BattleWorld world; readonly Camera camera;
        readonly Mesh emptyBody=new Mesh{name="Fragmented infantry body"};
        public int FragmentedBodies { get; private set; }
        public Material WoundMaterial { get; }
        public int SeveredLimbs => pieces.Count;
        public ArtilleryGore(BattleWorld world,Camera camera,InfantryRagdollLibrary rigs)
        {
            this.world=world;this.camera=camera;
            WoundMaterial=new Material(Resources.Load<Material>("CannonLit")) {name="Artillery exposed tissue"};
            WoundMaterial.SetColor("_BaseColor",new Color(.32f,.009f,.015f));
            WoundMaterial.SetFloat("_Cull",0);WoundMaterial.SetTexture("_BaseMap",null);WoundMaterial.SetFloat("_Smoothness",.48f);
            foreach(var set in new[]{rigs.French,rigs.British})
                Prepare(set);
        }
        public void Prepare(InfantryRagdollSet set)
        {
            if(set==null||cuts.ContainsKey(set))return;
            // Equipment removal creates a new mesh with different triangles. Cut
            // that actual body, so fragmentation cannot restore detached weapons.
            var choices=new Cut[5];string[] names={"l_upperarm_twist","r_upperarm_twist","l_thigh","r_thigh","head"};
            for(int i=0;i<choices.Length;i++)choices[i]=BuildCut(set,names[i]);
            cuts.Add(set,choices);
            fragments.Add(set,BuildFragments(set,choices));
        }
        static Cut BuildCut(InfantryRagdollSet set,string name)
        {
            int root=Array.IndexOf(set.Names,name);if(root<0)return null;
            bool[] belongs=new bool[set.Names.Length];
            for(int i=0;i<belongs.Length;i++)belongs[i]=i==root||set.Parents[i]>=0&&belongs[set.Parents[i]];
            var weights=set.Mesh.boneWeights;var triangles=set.Mesh.triangles;
            var detached=new List<int>();var retained=new List<int>();var edges=new Dictionary<(int,int),int>();
            float Weight(int v){var w=weights[v];return (belongs[w.boneIndex0]?w.weight0:0)+(belongs[w.boneIndex1]?w.weight1:0)+(belongs[w.boneIndex2]?w.weight2:0)+(belongs[w.boneIndex3]?w.weight3:0);}
            void Edge(int a,int b){var key=a<b?(a,b):(b,a);edges.TryGetValue(key,out int count);edges[key]=count+1;}
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                bool removed=(Weight(a)+Weight(b)+Weight(c))/3f>.5f;
                var destination=removed?detached:retained;destination.Add(a);destination.Add(b);destination.Add(c);
                if(removed){Edge(a,b);Edge(b,c);Edge(c,a);}
            }
            if(detached.Count==0||retained.Count==0)return null;
            var boundary=new List<int>();foreach(var edge in edges)if(edge.Value==1){boundary.Add(edge.Key.Item1);boundary.Add(edge.Key.Item2);}
            var body=UnityEngine.Object.Instantiate(set.Mesh);body.name="Severed "+name;
            var vertices=new List<Vector3>(body.vertices);var normals=new List<Vector3>(body.normals);var uv=new List<Vector2>(body.uv);var boneWeights=new List<BoneWeight>(weights);
            Vector3 centre=Vector3.zero;foreach(int index in boundary)centre+=vertices[index];centre/=Mathf.Max(1,boundary.Count);
            int capCentre=vertices.Count;vertices.Add(centre);normals.Add(Vector3.up);uv.Add(Vector2.zero);boneWeights.Add(new BoneWeight{boneIndex0=Mathf.Max(0,set.Parents[root]),weight0=1});
            var cap=new List<int>();for(int i=0;i<boundary.Count;i+=2){cap.Add(boundary[i+1]);cap.Add(boundary[i]);cap.Add(capCentre);}
            body.SetVertices(vertices);body.SetNormals(normals);body.SetUVs(0,uv);body.boneWeights=boneWeights.ToArray();
            body.subMeshCount=2;body.SetTriangles(retained,0);body.SetTriangles(cap,1);body.RecalculateBounds();
            return new Cut{Body=body,Detached=detached.ToArray(),Boundary=boundary.ToArray()};
        }
        // Assign every original triangle exactly once, including torso and equipment.
        static Cut[] BuildFragments(InfantryRagdollSet set,Cut[] choices)
        {
            var groups=new List<int>[6];for(int i=0;i<groups.Length;i++)groups[i]=new List<int>();
            var ownership=new Dictionary<(int,int,int),int>();
            for(int i=0;i<choices.Length;i++)if(choices[i]!=null)
                for(int t=0;t<choices[i].Detached.Length;t+=3)
                    ownership.TryAdd((choices[i].Detached[t],choices[i].Detached[t+1],choices[i].Detached[t+2]),i);
            var triangles=set.Mesh.triangles;
            for(int t=0;t<triangles.Length;t+=3)
            {
                int a=triangles[t],b=triangles[t+1],c=triangles[t+2];
                if(!ownership.TryGetValue((a,b,c),out int group))group=5;
                groups[group].Add(a);groups[group].Add(b);groups[group].Add(c);
            }
            var result=new Cut[6];
            for(int i=0;i<groups.Length;i++)
            {
                var edges=new Dictionary<(int,int),int>();
                void Edge(int a,int b){var key=a<b?(a,b):(b,a);edges.TryGetValue(key,out int count);edges[key]=count+1;}
                var g=groups[i];for(int t=0;t<g.Count;t+=3){Edge(g[t],g[t+1]);Edge(g[t+1],g[t+2]);Edge(g[t+2],g[t]);}
                var boundary=new List<int>();foreach(var edge in edges)if(edge.Value==1){boundary.Add(edge.Key.Item1);boundary.Add(edge.Key.Item2);}
                if(g.Count>0)result[i]=new Cut{Detached=g.ToArray(),Boundary=boundary.ToArray()};
            }
            return result;
        }
        public static bool FullFragmentation(SoldierDeath death)=>death.Cause==DeathCause.Shell&&death.Impulse.sqrMagnitude>=49f;
        public bool Sever(SoldierDeath death,SkinnedMeshRenderer skin,InfantryRagdollSet set)
        {
            bool full=FullFragmentation(death);
            if(!death.Cannon||death.Impulse.sqrMagnitude<16f||(!full&&RifleFireTiming.Variation(death.Soldier,137)>(death.Cause==DeathCause.Shell?.72f:.40f)))return false;
            Prepare(set);
            int choice=Mathf.Min(4,(int)(RifleFireTiming.Variation(death.Soldier,139)*5));
            var cut=cuts[set][choice];if(!full&&cut==null)return false;
            var posed=new Mesh();skin.BakeMesh(posed);
            if(full)
            {
                for(int i=0;i<fragments[set].Length;i++)if(fragments[set][i]!=null)Spawn(death,skin,set,posed,fragments[set][i],i);
                skin.sharedMesh=emptyBody;FragmentedBodies++;
            }
            else {Spawn(death,skin,set,posed,cut,choice);skin.sharedMesh=cut.Body;}
            DestroyOwned(posed);
            skin.sharedMaterials=new[]{set.Material,WoundMaterial};return true;
        }
        void Spawn(SoldierDeath death,SkinnedMeshRenderer skin,InfantryRagdollSet set,Mesh posed,Cut cut,int choice)
        {
            var source=posed.vertices;var sourceNormals=posed.normals;var sourceUV=posed.uv;
            var remap=new Dictionary<int,int>();var vertices=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var tris=new List<int>();
            int Map(int index)
            {
                if(remap.TryGetValue(index,out int mapped))return mapped;
                mapped=vertices.Count;remap.Add(index,mapped);vertices.Add(source[index]);normals.Add(sourceNormals[index]);uv.Add(sourceUV[index]);return mapped;
            }
            foreach(int index in cut.Detached)tris.Add(Map(index));
            Vector3 centre=Vector3.zero;foreach(var v in vertices)centre+=v;centre/=vertices.Count;
            var caps=new List<int>();Vector3 wound=Vector3.zero;foreach(int index in cut.Boundary)wound+=source[index];wound=cut.Boundary.Length>0?wound/cut.Boundary.Length:centre;
            // Cap each connected opening separately: the torso has several severed joints.
            var adjacency=new Dictionary<int,List<int>>();
            for(int i=0;i<cut.Boundary.Length;i+=2)
            {
                int a=cut.Boundary[i],b=cut.Boundary[i+1];
                if(!adjacency.ContainsKey(a))adjacency[a]=new List<int>();
                if(!adjacency.ContainsKey(b))adjacency[b]=new List<int>();
                adjacency[a].Add(b);adjacency[b].Add(a);
            }
            var visited=new HashSet<int>();
            foreach(int first in adjacency.Keys)
            {
                if(visited.Contains(first))continue;
                var opening=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(first);visited.Add(first);
                Vector3 capPosition=Vector3.zero;
                while(queue.Count>0)
                {
                    int v=queue.Dequeue();opening.Add(v);capPosition+=source[v];
                    foreach(int next in adjacency[v])if(visited.Add(next))queue.Enqueue(next);
                }
                int capCentre=vertices.Count;vertices.Add(capPosition/opening.Count);normals.Add(Vector3.up);uv.Add(Vector2.zero);
                for(int i=0;i<cut.Boundary.Length;i+=2)if(opening.Contains(cut.Boundary[i]))
                {caps.Add(Map(cut.Boundary[i]));caps.Add(Map(cut.Boundary[i+1]));caps.Add(capCentre);}
            }
            for(int i=0;i<vertices.Count;i++)vertices[i]-=centre;
            var fragment=new Mesh{name="Artillery fragment "+death.Soldier+"/"+choice,indexFormat=IndexFormat.UInt32};
            fragment.SetVertices(vertices);fragment.SetNormals(normals);fragment.SetUVs(0,uv);fragment.subMeshCount=2;fragment.SetTriangles(tris,0);fragment.SetTriangles(caps,1);fragment.RecalculateBounds();
            float angle=(choice*60f+RifleFireTiming.Variation(death.Soldier,143)*360f)*Mathf.Deg2Rad;
            pieces.Add(new Piece{Mesh=fragment,Uniform=set.Material,Position=skin.transform.TransformPoint(centre),Rotation=skin.transform.rotation,Wound=wound-centre,
                Velocity=death.Impulse*1.1f+new Vector3(Mathf.Cos(angle)*3f,2f+choice*.35f,Mathf.Sin(angle)*3f),Spin=new Vector3(190+choice*31,110-choice*47,250+choice*23),Radius=Mathf.Min(.25f,fragment.bounds.extents.magnitude)});
        }
        public void Step(float dt,BattleBlood blood=null,float time=0f)
        {
            if(dt<=0)return;
            foreach(var p in pieces)
            {
                if(p.Settled)continue;
                p.TrailTime+=dt;
                if(p.Age<1.8f&&p.TrailTime>=.06f)
                {
                    p.TrailTime=Mathf.Repeat(p.TrailTime,.06f);
                    blood?.EmitFragmentTrail(p.Position+p.Rotation*p.Wound,p.Velocity,time);
                }
                p.Age+=dt;p.Velocity+=Vector3.down*(9.81f*dt);p.Position+=p.Velocity*dt;
                p.Rotation=Quaternion.Euler(p.Spin*dt)*p.Rotation;
                // Use oriented bounds so the whole fragment rests above terrain rather than sinking at its pivot.
                var ext=p.Mesh.bounds.extents;var r=Matrix4x4.Rotate(p.Rotation);
                float clearance=Mathf.Abs(r.m10)*ext.x+Mathf.Abs(r.m11)*ext.y+Mathf.Abs(r.m12)*ext.z;
                float floor=world.Terrain.HeightAt(new Vector2(p.Position.x,p.Position.z))+clearance-(p.Rotation*p.Mesh.bounds.center).y+.02f;
                if(p.Position.y<=floor){if(p.Velocity.y<-1f)blood?.EmitFragmentLanding(p.Position,time);p.Position.y=floor;p.Velocity=new Vector3(p.Velocity.x*.42f,Mathf.Abs(p.Velocity.y)*.20f,p.Velocity.z*.42f);p.Spin*=.45f;if(p.Velocity.sqrMagnitude<1f||p.Age>3f)p.Settled=true;}
                if(p.Age>5f){p.Position.y=floor;p.Settled=true;}
            }
        }
        public void Render()
        {
            GeometryUtility.CalculateFrustumPlanes(camera,frustum);
            foreach(var p in pieces)
            {
                if((p.Position-camera.transform.position).sqrMagnitude>220f*220f||!GeometryUtility.TestPlanesAABB(frustum,new Bounds(p.Position,Vector3.one*3)))continue;
                var matrix=Matrix4x4.TRS(p.Position,p.Rotation,Vector3.one);
                Graphics.DrawMesh(p.Mesh,matrix,p.Uniform,0,camera,0);
                Graphics.DrawMesh(p.Mesh,matrix,WoundMaterial,0,camera,1);
            }
        }
        static void DestroyOwned(UnityEngine.Object value)
        {
            if(!value)return;
            if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);
        }
        public void Dispose()
        {
            foreach(var choices in cuts.Values)foreach(var cut in choices)if(cut!=null)DestroyOwned(cut.Body);
            foreach(var p in pieces)DestroyOwned(p.Mesh);
            DestroyOwned(emptyBody);
            DestroyOwned(WoundMaterial);
        }
    }
}
