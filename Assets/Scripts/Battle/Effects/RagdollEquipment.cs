using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    // Split rigid equipment once per outfit. Body topology/skin and original assets stay intact.
    public sealed class RagdollEquipment : IDisposable
    {
        readonly Dictionary<InfantryRagdollSet,InfantryRagdollSet> cache=new Dictionary<InfantryRagdollSet,InfantryRagdollSet>();
        public InfantryRagdollSet Resolve(InfantryRagdollSet source)
        {
            if(source==null)return null;
            if(cache.TryGetValue(source,out var ready))return ready;
            var mesh=source.Mesh;var vertices=mesh.vertices;var triangles=mesh.triangles;
            var parents=new int[vertices.Length];for(int i=0;i<parents.Length;i++)parents[i]=i;
            int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            void Join(int a,int b){parents[Find(a)]=Find(b);}
            var seams=new Dictionary<Vector3Int,int>();
            for(int i=0;i<vertices.Length;i++)
            {
                var key=Vector3Int.RoundToInt(vertices[i]*10000);
                if(seams.TryGetValue(key,out int other))Join(i,other);else seams.Add(key,i);
            }
            for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
            var groups=new Dictionary<int,List<int>>();
            for(int i=0;i<vertices.Length;i++){int key=Find(i);if(!groups.TryGetValue(key,out var group))groups.Add(key,group=new List<int>());group.Add(i);}
            int head=Array.IndexOf(source.Names,"head"),left=Array.IndexOf(source.Names,"l_hand"),right=Array.IndexOf(source.Names,"r_hand");
            var labels=new int[vertices.Length];
            var gunBounds=new List<Bounds>();
            var standardBounds=new List<Bounds>();
            var skin=mesh.boneWeights;
            foreach(var group in groups.Values)
            {
                var bounds=new Bounds(vertices[group[0]],Vector3.zero);foreach(int i in group)bounds.Encapsulate(vertices[i]);
                // Weapons can be held diagonally: measure slenderness along the main
                // geometric axis rather than the soldier's world-aligned bounds.
                Vector3 direction=Vector3.one.normalized;
                for(int iteration=0;iteration<12;iteration++)
                {
                    Vector3 next=Vector3.zero;
                    foreach(int i in group){Vector3 p=vertices[i]-bounds.center;next+=p*Vector3.Dot(p,direction);}
                    if(next.sqrMagnitude>.000001f)direction=next.normalized;
                }
                var turn=Quaternion.FromToRotation(direction,Vector3.forward);
                var aligned=new Bounds(turn*vertices[group[0]],Vector3.zero);foreach(int i in group)aligned.Encapsulate(turn*vertices[i]);
                bool gun=aligned.size.z>.7f&&aligned.size.x<.25f&&aligned.size.y<.25f;
                bool standard=gun&&bounds.max.y>2.7f;
                bool hat=head>=0&&bounds.min.y>source.Positions[head].y+.08f&&bounds.max.y<2.5f;
                bool backpack=bounds.center.z<-.14f&&bounds.max.z<.02f&&bounds.min.y>.9f&&bounds.max.y<1.75f;
                int label=standard?4:hat?2:gun?1:backpack?3:0;
                foreach(int i in group)labels[i]=label;
                if(gun&&!standard){bounds.Expand(.06f);gunBounds.Add(bounds);}
                if(standard){bounds.Expand(.22f);standardBounds.Add(bounds);}
            }
            // Include the receiver, trigger, sights and other disconnected rigid
            // components authored on the same hand as the long weapon body.
            foreach(var group in groups.Values)
            {
                if(labels[group[0]]!=0)continue;
                bool high=true;foreach(int i in group)if(vertices[i].y<2.1f){high=false;break;}
                if(high)
                {
                    foreach(var bound in standardBounds)
                    {
                        bool inside=true;foreach(int i in group)if(!bound.Contains(vertices[i])){inside=false;break;}
                        if(inside){foreach(int i in group)labels[i]=4;break;}
                    }
                    if(labels[group[0]]==4)continue;
                }
                bool rigid=skin.Length==vertices.Length;
                foreach(int i in group)
                {
                    var w=skin[i];if(w.weight0<.999f||(w.boneIndex0!=left&&w.boneIndex0!=right)){rigid=false;break;}
                }
                if(!rigid)continue;
                foreach(var bound in gunBounds)
                {
                    bool inside=true;foreach(int i in group)if(!bound.Contains(vertices[i])){inside=false;break;}
                    if(inside){foreach(int i in group)labels[i]=1;break;}
                }
            }
            var body=new List<int>();var gunTriangles=new List<int>();var hatTriangles=new List<int>();
            var packTriangles=new List<int>();var standardTriangles=new List<int>();
            for(int i=0;i<triangles.Length;i+=3)
            {
                var target=labels[triangles[i]]==1?gunTriangles:labels[triangles[i]]==2?hatTriangles:
                    labels[triangles[i]]==3?packTriangles:labels[triangles[i]]==4?standardTriangles:body;
                target.Add(triangles[i]);target.Add(triangles[i+1]);target.Add(triangles[i+2]);
            }
            var bodyMesh=UnityEngine.Object.Instantiate(mesh);bodyMesh.name=mesh.name+" without loose equipment";
            bodyMesh.SetTriangles(body,0);bodyMesh.RecalculateBounds();
            var parts=new List<DetachedRagdollPart>();
            void Part(List<int> indices,int bone,DetachedEquipmentKind kind)
            {
                if(indices.Count==0||bone<0)return;
                var map=new Dictionary<int,int>();var points=new List<Vector3>();var normals=new List<Vector3>();var uv=new List<Vector2>();var faces=new List<int>();
                var sourceNormals=mesh.normals;var sourceUv=mesh.uv;
                var inverse=Matrix4x4.TRS(source.Positions[bone],source.Rotations[bone],Vector3.one).inverse;
                foreach(int index in indices)
                {
                    if(!map.TryGetValue(index,out int mapped))
                    {
                        mapped=points.Count;map.Add(index,mapped);points.Add(inverse.MultiplyPoint3x4(vertices[index]));
                        normals.Add(inverse.MultiplyVector(sourceNormals[index]).normalized);uv.Add(sourceUv[index]);
                    }
                    faces.Add(mapped);
                }
                var partMesh=new Mesh{name=mesh.name+" loose "+kind,indexFormat=IndexFormat.UInt32};
                partMesh.SetVertices(points);partMesh.SetNormals(normals);partMesh.SetUVs(0,uv);partMesh.SetTriangles(faces,0);partMesh.RecalculateBounds();
                parts.Add(new DetachedRagdollPart{Mesh=partMesh,Bone=bone,Kind=kind});
            }
            // A weapon may have been authored on either hand; choose its nearest hand.
            int hand=right;
            if(gunTriangles.Count>0&&left>=0&&right>=0)
            {
                float l=float.PositiveInfinity,r=float.PositiveInfinity;
                foreach(int index in gunTriangles){l=Mathf.Min(l,(vertices[index]-source.Positions[left]).sqrMagnitude);r=Mathf.Min(r,(vertices[index]-source.Positions[right]).sqrMagnitude);}
                hand=l<r?left:right;
                int leftVotes=0,rightVotes=0;
                foreach(int index in gunTriangles)if(skin[index].weight0>.999f)
                {if(skin[index].boneIndex0==left)leftVotes++;if(skin[index].boneIndex0==right)rightVotes++;}
                if(leftVotes+rightVotes>3)hand=leftVotes>rightVotes?left:right;
            }
            Part(gunTriangles,hand,DetachedEquipmentKind.Weapon);Part(hatTriangles,head,DetachedEquipmentKind.Hat);
            Part(packTriangles,Array.IndexOf(source.Names,"spine2"),DetachedEquipmentKind.Backpack);
            Part(standardTriangles,right,DetachedEquipmentKind.Standard);
            ready=new InfantryRagdollSet{Mesh=bodyMesh,Material=source.Material,Names=source.Names,Parents=source.Parents,
                Positions=source.Positions,Rotations=source.Rotations,Idle=source.Idle,Fire=source.Fire,Reload=source.Reload,
                March=source.March,Run=source.Run,Charge=source.Charge,Mounted=source.Mounted,DetachedParts=parts.ToArray()};
            cache.Add(source,ready);return ready;
        }
        public void Dispose()
        {
            foreach(var set in cache.Values){Release(set.Mesh);foreach(var part in set.DetachedParts)Release(part.Mesh);}cache.Clear();
        }
        static void Release(UnityEngine.Object item)
        {if(Application.isPlaying)UnityEngine.Object.Destroy(item);else UnityEngine.Object.DestroyImmediate(item);}
    }
}
