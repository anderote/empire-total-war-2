using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle.Art
{
    [Serializable] public sealed class BayonetWeaponTemplate
    {
        // Source vertices in the same first-triangle-use order as the combined poses,
        // expressed in metres. A rigid fit recovers the real barrel in every pose.
        public Vector3[] Points;
        public int B, C;
        public Vector3 Socket;
    }
    [Serializable] public sealed class BayonetOrderClip
    {
        public string Name;
        public Material Uniform;
        public int Lod;
        public Mesh[] Standing, Kneeling, March, Run, UnfixMarch, UnfixRun;
        public BayonetTransferTrack[] Transfer;
    }
    [Serializable] public sealed class BayonetTransferTrack { public Mesh[] Bodies, Props; }
    public sealed class BayonetPresentationLibrary : ScriptableObject
    {
        public BayonetWeaponTemplate[] Weapons;
        public BayonetOrderClip[] Orders;
        public Material Steel;
        public BayonetOrderClip Resolve(Material uniform,int lod)
        {
            if(Orders!=null)foreach(var clip in Orders)if(clip.Uniform==uniform&&clip.Lod==lod)return clip;
            return null;
        }
        public bool Locate(Mesh pose,out Vector3 socket,out Quaternion rotation,bool next=false)
        {
            socket=default;rotation=default;
            if(!pose||Weapons==null)return false;
            var points=pose.vertices;
            if(next){var morph=new List<Vector3>();pose.GetUVs(2,morph);if(morph.Count==points.Length)points=morph.ToArray();}
            foreach(var weapon in Weapons)
            {
                var source=weapon.Points;if(source==null||source.Length<3)continue;
                Vector3 ab=source[weapon.B]-source[0],ac=source[weapon.C]-source[0];
                float length=ab.sqrMagnitude,area=Vector3.Cross(ab,ac).sqrMagnitude;
                var sourceBasis=Quaternion.LookRotation(ab,Vector3.Cross(ab,ac));
                for(int start=0;start<=points.Length-source.Length;start++)
                {
                    Vector3 targetAB=points[start+weapon.B]-points[start];
                    if(Mathf.Abs(targetAB.sqrMagnitude-length)>Mathf.Max(.000002f,length*.0001f))continue;
                    Vector3 targetAC=points[start+weapon.C]-points[start];
                    if(Mathf.Abs(Vector3.Cross(targetAB,targetAC).sqrMagnitude-area)>Mathf.Max(.000002f,area*.0002f))continue;
                    var turn=Quaternion.LookRotation(targetAB,Vector3.Cross(targetAB,targetAC))*Quaternion.Inverse(sourceBasis);
                    bool match=true;
                    for(int test=1;test<=12;test++)
                    {
                        int index=(source.Length-1)*test/12;
                        if((points[start+index]-points[start]-turn*(source[index]-source[0])).sqrMagnitude>.000001f){match=false;break;}
                    }
                    if(!match)continue;
                    rotation=turn;socket=points[start]+turn*(weapon.Socket-source[0]);return true;
                }
            }
            return false;
        }
        public static Mesh Equipment(Vector3 socket,Quaternion rotation,Vector3 belt,Quaternion beltRotation,bool blade=true)
        {
            var mesh=Blade(socket,rotation,socket,rotation);
            var points=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
            if(blade){points.AddRange(mesh.vertices);triangles.AddRange(mesh.triangles);for(int i=0;i<mesh.vertexCount;i++)uv.Add(new Vector2(.25f,.5f));}
            int first=points.Count;
            // Leather sheath surrounding the stowed blade, with a visible steel socket.
            for(int ring=0;ring<2;ring++)for(int side=0;side<4;side++){
                float angle=side*Mathf.PI*.5f;
                points.Add(belt+beltRotation*new Vector3(Mathf.Cos(angle)*.023f,Mathf.Sin(angle)*.013f,ring==0?.014f:.445f));uv.Add(new Vector2(.75f,.5f));
            }
            for(int side=0;side<4;side++){int a=first+side,b=first+(side+1)%4;triangles.AddRange(new[]{a,b,a+4,b,b+4,a+4});}
            triangles.AddRange(new[]{first+4,first+5,first+6,first+4,first+6,first+7});
            mesh.SetVertices(points);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh.SetUVs(2,points);mesh.SetUVs(3,new List<Vector3>(mesh.normals));return mesh;
        }
        // A slim socket bayonet with a diamond-section blade, separate from the bore.
        // Blade topology remains fixed while its points follow the gun's pose morph.
        public static Mesh Blade(Vector3 socket,Quaternion rotation,Vector3 nextSocket,Quaternion nextRotation)
        {
            var local=new[]{new Vector3(-.008f,-.008f,-.06f),new Vector3(.008f,-.008f,-.06f),new Vector3(.008f,.008f,-.06f),new Vector3(-.008f,.008f,-.06f),
                new Vector3(-.008f,-.008f,0),new Vector3(.008f,-.008f,0),new Vector3(.008f,.008f,0),new Vector3(-.008f,.008f,0),
                new Vector3(-.015f,0,.025f),new Vector3(0,.006f,.025f),new Vector3(.015f,0,.025f),new Vector3(0,-.006f,.025f),new Vector3(0,0,.43f)};
            var triangles=new[]{0,1,5,0,5,4,1,2,6,1,6,5,2,3,7,2,7,6,3,0,4,3,4,7,8,9,12,9,10,12,10,11,12,11,8,12,8,11,10,8,10,9};
            var mesh=new Mesh{name="Instanced socket bayonet"};var points=new Vector3[local.Length];var next=new List<Vector3>();
            for(int i=0;i<local.Length;i++){points[i]=socket+rotation*local[i];next.Add(nextSocket+nextRotation*local[i]);}
            mesh.vertices=points;mesh.triangles=triangles;mesh.uv=new Vector2[local.Length];mesh.RecalculateNormals();mesh.RecalculateBounds();
            mesh.SetUVs(2,next);var nextMesh=new Mesh();nextMesh.SetVertices(next);nextMesh.triangles=triangles;nextMesh.RecalculateNormals();mesh.SetUVs(3,new List<Vector3>(nextMesh.normals));
            var bounds=mesh.bounds;bounds.Encapsulate(nextMesh.bounds);mesh.bounds=bounds;if(Application.isPlaying)UnityEngine.Object.Destroy(nextMesh);else UnityEngine.Object.DestroyImmediate(nextMesh);return mesh;
        }
    }
}
