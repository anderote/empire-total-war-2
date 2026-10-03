using System;
using System.Collections.Generic;
using EmpireBattle.Art;
using UnityEngine;

namespace EmpireBattle
{
    // Roster meshes share the common human proportions, but their crowd bake has no skin.
    // Transfer the common skin's weights in the idle pose, retaining the actual outfit/atlas.
    // Cache once per outfit/command mesh, never once per casualty.
    public sealed class InfantryRagdollAppearance : IDisposable
    {
        readonly Dictionary<Mesh, InfantryRagdollSet> sets = new Dictionary<Mesh, InfantryRagdollSet>();
        readonly Dictionary<InfantryRagdollSet, WeightSource> mountedSources = new Dictionary<InfantryRagdollSet, WeightSource>();
        readonly Dictionary<InfantryRagdollSet, WeightSource> sources = new Dictionary<InfantryRagdollSet, WeightSource>();
        sealed class WeightSource
        {
            const float Cell = .15f;
            readonly Vector3[] vertices;
            readonly BoneWeight[] weights;
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            static Vector3Int Key(Vector3 p) => Vector3Int.FloorToInt(p / Cell);
            public WeightSource(InfantryRagdollSet rig, Matrix4x4[] pose = null)
            {
                vertices = rig.Mesh.vertices; weights = rig.Mesh.boneWeights;
                if (pose != null) for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = SkinMatrix(pose, weights[i]).MultiplyPoint3x4(vertices[i]);
                for (int i = 0; i < vertices.Length; i++)
                {
                    var key = Key(vertices[i]);
                    if (!cells.TryGetValue(key, out var bucket)) cells.Add(key, bucket = new List<int>());
                    bucket.Add(i);
                }
            }
            public BoneWeight Nearest(Vector3 point)
            {
                int best = -1; float distance = float.PositiveInfinity;
                var key = Key(point);
                for (int x = -2; x <= 2; x++) for (int y = -2; y <= 2; y++) for (int z = -2; z <= 2; z++)
                    if (cells.TryGetValue(key + new Vector3Int(x,y,z), out var bucket))
                        foreach (int i in bucket)
                        {
                            float d = (vertices[i] - point).sqrMagnitude;
                            if (d < distance) { distance = d; best = i; }
                        }
                // Tall hats and extended equipment can lie outside the common silhouette.
                if (best < 0) for (int i = 0; i < vertices.Length; i++)
                {
                    float d = (vertices[i] - point).sqrMagnitude;
                    if (d < distance) { distance = d; best = i; }
                }
                return weights[best];
            }
        }

        public InfantryRagdollSet Resolve(InfantryCrowdSet appearance, InfantryRagdollSet reference, int command = -1)
        {
            var lod = appearance.Lods[0];
            var poses = command >= 0 && lod.Command != null && lod.Command.Length > command
                ? lod.Command[command]?.Idle : lod.Idle;
            if (poses == null || poses.Length == 0 || !poses[0]) return null;
            var source = poses[0];
            return ResolveMesh(source, appearance.Material, reference, command == 2, false);
        }
        public InfantryRagdollSet ResolveMounted(CavalryCrowdSet appearance, InfantryRagdollSet reference)
        {
            // Fall frame zero contains only the rider, still in the saddle. Never use
            // a mounted idle mesh here: it also contains the horse and tack.
            var poses = appearance?.Lods?[0]?.Clip(MountedMotion.RiderFall)?.Poses;
            if (poses == null || poses.Length == 0 || !poses[0] || reference?.Mounted == null || reference.Mounted.Length == 0) return null;
            return ResolveMesh(poses[0], appearance.Material, reference, false, true);
        }
        static Matrix4x4 SkinMatrix(Matrix4x4[] pose, BoneWeight w)
        {
            var result = new Matrix4x4();
            for (int i = 0; i < 16; i++) result[i] = pose[w.boneIndex0][i] * w.weight0
                + pose[w.boneIndex1][i] * w.weight1 + pose[w.boneIndex2][i] * w.weight2 + pose[w.boneIndex3][i] * w.weight3;
            return result;
        }
        InfantryRagdollSet ResolveMesh(Mesh source, Material atlas, InfantryRagdollSet reference, bool standard, bool mounted)
        {
            if (sets.TryGetValue(source, out var result)) return result;
            Matrix4x4[] saddle = null;
            if (mounted)
            {
                var pose = reference.Mounted[0]; var binds = reference.Mesh.bindposes;
                saddle = new Matrix4x4[binds.Length];
                for (int i = 0; i < saddle.Length; i++) saddle[i] = Matrix4x4.TRS(pose.Positions[i], pose.Rotations[i], Vector3.one) * binds[i];
            }
            var cache = mounted ? mountedSources : sources;
            if (!cache.TryGetValue(reference, out var weights)) cache.Add(reference, weights = new WeightSource(reference, saddle));
            var mesh = standard?CrowdInfantryRenderer.PrepareStandard(source):UnityEngine.Object.Instantiate(source);
            mesh.name = source.name + " physical casualty";
            var vertices = mesh.vertices; var skin = new BoneWeight[vertices.Length];
            for (int i = 0; i < skin.Length; i++) skin[i] = weights.Nearest(vertices[i]);
            var equipmentPose = mounted ? new InfantryRagdollSet { Names=reference.Names,
                Positions=reference.Mounted[0].Positions, Rotations=reference.Mounted[0].Rotations } : reference;
            var props=BindEquipment(mesh,vertices,skin,equipmentPose);
            if (mounted)
            {
                // Bring the saddle mesh back into the common rest pose so pooled
                // physics, detached equipment and every mounted gait share a bind pose.
                var normals = mesh.normals;
                for (int i = 0; i < vertices.Length; i++)
                {
                    var transform = SkinMatrix(saddle, skin[i]);
                    vertices[i] = transform.inverse.MultiplyPoint3x4(vertices[i]);
                    normals[i] = transform.transpose.MultiplyVector(normals[i]).normalized;
                }
                mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
            }
            mesh.boneWeights = skin; mesh.bindposes = reference.Mesh.bindposes;
            var material = new Material(reference.Material) { name = source.name + " casualty atlas" };
            material.SetTexture("_BaseMap", atlas.GetTexture("_BaseMap"));
            material.SetColor("_BaseColor", atlas.GetColor("_BaseColor"));
            result = new InfantryRagdollSet { Mesh=mesh, Material=material, Names=reference.Names, Parents=reference.Parents,
                Positions=reference.Positions, Rotations=reference.Rotations, Idle=reference.Idle, Fire=reference.Fire,
                Reload=reference.Reload, March=reference.March, Run=reference.Run, Charge=reference.Charge, Mounted=reference.Mounted, Props=props };
            sets.Add(source,result);
            return result;
        }
        // Long rigid weapons must not inherit several limb weights from nearby cloth. Join
        // UV/normal seams, identify slender connected pieces, and bind each to one hand.
        static InfantryRagdollProp[] BindEquipment(Mesh mesh,Vector3[] vertices,BoneWeight[] skin,InfantryRagdollSet rig)
        {
            var parents=new int[vertices.Length];for(int i=0;i<parents.Length;i++)parents[i]=i;
            int Find(int i){while(parents[i]!=i){parents[i]=parents[parents[i]];i=parents[i];}return i;}
            void Join(int a,int b){parents[Find(a)]=Find(b);}
            var points=new Dictionary<Vector3Int,int>();
            for(int i=0;i<vertices.Length;i++)
            {
                var key=Vector3Int.RoundToInt(vertices[i]*10000);
                if(points.TryGetValue(key,out int other))Join(i,other);else points.Add(key,i);
            }
            var triangles=mesh.triangles;for(int i=0;i<triangles.Length;i+=3){Join(triangles[i],triangles[i+1]);Join(triangles[i],triangles[i+2]);}
            var components=new Dictionary<int,List<int>>();
            for(int i=0;i<vertices.Length;i++){int key=Find(i);if(!components.TryGetValue(key,out var list))components.Add(key,list=new List<int>());list.Add(i);}
            int left=Array.IndexOf(rig.Names,"l_hand"),right=Array.IndexOf(rig.Names,"r_hand");
            var props=new List<InfantryRagdollProp>();
            foreach(var indices in components.Values)
            {
                var bounds=new Bounds(vertices[indices[0]],Vector3.zero);foreach(int i in indices)bounds.Encapsulate(vertices[i]);
                Vector3 size=bounds.size;int axis=size.y>size.x?1:0;if(size.z>size[axis])axis=2;
                if(size[axis]<.7f||size[(axis+1)%3]>.18f||size[(axis+2)%3]>.18f)continue;
                int hand=right;float nearest=float.PositiveInfinity;
                foreach(int i in indices)foreach(int bone in new[]{left,right})
                {float d=(vertices[i]-rig.Positions[bone]).sqrMagnitude;if(d<nearest){nearest=d;hand=bone;}}
                foreach(int i in indices)skin[i]=new BoneWeight{boneIndex0=hand,weight0=1};
                Vector3 direction=Vector3.zero;direction[axis]=1;
                var inverse=Quaternion.Inverse(rig.Rotations[hand]);
                props.Add(new InfantryRagdollProp{Bone=hand,Center=inverse*(bounds.center-rig.Positions[hand]),Axis=inverse*direction,
                    Length=size[axis],Radius=Mathf.Clamp(Mathf.Max(size[(axis+1)%3],size[(axis+2)%3])*.5f,.015f,.07f)});
            }
            return props.ToArray();
        }
        public void Dispose()
        {
            foreach (var set in sets.Values) { Release(set.Mesh); Release(set.Material); }
            sets.Clear(); sources.Clear(); mountedSources.Clear();
        }
        static void Release(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
