using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace EmpireBattle
{
    /// <summary>Shared-grid terrain and real CC0 scenery meshes, with instanced static vegetation.</summary>
    public sealed class RuralLandscape : MonoBehaviour
    {
        [Serializable]
        sealed class MeshData { public float[] vertices; public float[] normals; public float[] colors; public int[] triangles; }
        sealed class Batch
        {
            public Mesh Mesh,FarMesh;
            public float LodDistance;
            public Matrix4x4[] Transforms;
            public Bounds Bounds;
            public Bounds[] InstanceBounds;
            public Material Material;
            public float MaxDistance;
            public bool CastShadows;
            public bool[] Broken;
            public MaterialPropertyBlock DamageProperties;
        }
        readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        public SceneryImpactMap ImpactSurfaces { get; } = new SceneryImpactMap();
        readonly List<Batch> batches=new List<Batch>();
        readonly Matrix4x4[] scratch=new Matrix4x4[1023];
        readonly Matrix4x4[] crownScratch = new Matrix4x4[1023];
        readonly Plane[] planes=new Plane[6];
        sealed class Tree
        {
            public Batch Batch; public int Index; public Vector3 Root; public float Height, Born = -1; public Vector3 Fall;
        }
        readonly Dictionary<Vector2Int, List<Tree>> treeCells = new Dictionary<Vector2Int, List<Tree>>();
        readonly List<Tree> fallingTrees = new List<Tree>();
        readonly Dictionary<Mesh, Mesh[]> fractures = new Dictionary<Mesh, Mesh[]>();
        readonly Dictionary<int, Vector3> previousBalls = new Dictionary<int, Vector3>();
        readonly HashSet<int> liveBalls = new HashSet<int>();
        readonly List<int> expiredBalls = new List<int>();
        int observedImpact;
        [NonSerialized] public ArtillerySystem Artillery;
        public Func<float> PresentationTime;
        public Action<Vector3,Vector3> TreeBroken;
        public int BrokenTreeCount => fallingTrees.Count;
        float VisualTime => PresentationTime != null ? PresentationTime() : world.Time;
        static Vector2Int TreeCell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / 32f), Mathf.FloorToInt(p.z / 32f));
        void BreakTrees(Vector3 from, Vector3 to, float radius, Vector3 force, bool blast)
        {
            Vector2Int min = TreeCell(Vector3.Min(from,to) - Vector3.one * radius), max = TreeCell(Vector3.Max(from,to) + Vector3.one * radius);
            for (int z = min.y; z <= max.y; z++) for (int x = min.x; x <= max.x; x++)
            {
                if (!treeCells.TryGetValue(new Vector2Int(x,z), out var trees)) continue;
                foreach (var tree in trees)
                {
                    if (tree.Born >= 0) continue;
                    Vector3 delta = to - from;
                    Vector3 trunk = tree.Root + Vector3.up * Mathf.Clamp((from.y + to.y) * .5f - tree.Root.y, 0, tree.Height * .4f);
                    float t = delta.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(trunk - from, delta) / delta.sqrMagnitude) : 0;
                    if (Vector3.Distance(trunk, from + delta * t) > radius + .22f) continue;
                    tree.Born = VisualTime; tree.Batch.Broken[tree.Index] = true;
                    Vector3 fall = blast ? tree.Root - from : force; fall.y = 0;
                    tree.Fall = fall.sqrMagnitude > .001f ? fall.normalized : Vector3.forward; fallingTrees.Add(tree);
                    TreeBroken?.Invoke(tree.Root + Vector3.up * tree.Height * .08f, tree.Fall);
                    ImpactSurfaces.DisableFoliageAt(tree.Root + Vector3.up * tree.Height * .5f);
                    if (!fractures.ContainsKey(tree.Batch.Mesh))
                    {
                        var parts = new[] { ArtilleryTreeFracture.Clip(tree.Batch.Mesh, false), ArtilleryTreeFracture.Clip(tree.Batch.Mesh, true) };
                        fractures[tree.Batch.Mesh] = parts; owned.Add(parts[0]); owned.Add(parts[1]);
                    }
                }
            }
        }
        public Vector3 ClosestTreeRoot(Vector2 point)
        {
            float best = float.MaxValue; Vector3 root = Vector3.zero;
            foreach (var list in treeCells.Values) foreach (var tree in list)
            {
                float distance = (new Vector2(tree.Root.x,tree.Root.z) - point).sqrMagnitude;
                if (distance < best) { best = distance; root = tree.Root; }
            }
            return root;
        }
        public void ConsumeArtilleryDamage()
        {
            if (Artillery == null) return;
            int first = Mathf.Max(observedImpact + 1, Artillery.ImpactSequence - Artillery.Impacts.Length + 1);
            for (int sequence = first; sequence <= Artillery.ImpactSequence; sequence++)
            {
                var hit = Artillery.Impacts[(sequence - 1) % Artillery.Impacts.Length];
                if (hit.Sequence != sequence || ArtillerySystem.IsCloudShell(hit.AmmunitionType) || hit.AmmunitionType == ArtilleryAmmunition.Canister) continue;
                BreakTrees(hit.Position, hit.Position, hit.Detonates ? 7f : .65f, hit.Direction, hit.Detonates);
            }
            observedImpact = Artillery.ImpactSequence;
            liveBalls.Clear();
            foreach (var ball in Artillery.Projectiles)
            {
                if (!ball.Active || ArtillerySystem.IsCloudShell(ball.AmmunitionType) || ball.AmmunitionType == ArtilleryAmmunition.Canister) continue;
                liveBalls.Add(ball.Id);
                Vector3 from = previousBalls.TryGetValue(ball.Id, out var p) ? p : ball.Position - ball.Velocity * Mathf.Min(.1f, ball.SegmentAge);
                BreakTrees(from, ball.Position, .25f, ball.Velocity, false); previousBalls[ball.Id] = ball.Position;
            }
            expiredBalls.Clear(); foreach (int id in previousBalls.Keys) if (!liveBalls.Contains(id)) expiredBalls.Add(id);
            foreach (int id in expiredBalls) previousBalls.Remove(id);
            if (!battleCamera) return;
            foreach (var fracture in fractures)
            {
                int count = 0; Material material = foliageMaterial;
                foreach (var tree in fallingTrees)
                {
                    if (tree.Batch.Mesh != fracture.Key || (tree.Root - battleCamera.transform.position).sqrMagnitude > 1600f*1600f) continue;
                    material = tree.Batch.Material;
                    var matrix = tree.Batch.Transforms[tree.Index];
                    float age = Mathf.Max(0, VisualTime - tree.Born);
                    float angle = Mathf.Min(87f, age * age * 24f);
                    Vector3 axis = Vector3.Cross(Vector3.up, tree.Fall);
                    Vector3 hinge = tree.Root + Vector3.up * tree.Height * .08f;
                    Matrix4x4 fall = Matrix4x4.Translate(hinge) * Matrix4x4.Rotate(Quaternion.AngleAxis(angle, axis)) * Matrix4x4.Translate(-hinge);
                    scratch[count] = matrix; crownScratch[count++] = fall * matrix;
                    if (count == scratch.Length)
                    {
                        DrawBrokenTrees(fracture.Value, material, count); count = 0;
                    }
                }
                if (count > 0) DrawBrokenTrees(fracture.Value, material, count);
            }
        }
        void DrawBrokenTrees(Mesh[] parts, Material material, int count)
        {
            Graphics.DrawMeshInstanced(parts[0],0,material,scratch,count,null,ShadowCastingMode.On,true,0,battleCamera);
            Graphics.DrawMeshInstanced(parts[1],0,material,crownScratch,count,null,ShadowCastingMode.On,true,0,battleCamera);
        }

        BattleWorld world;
        Camera battleCamera;
        Material sceneryMaterial,terrainMaterial,grassMaterial,foliageMaterial;
        Mesh terrainMesh;
        int damageRevision;
        TerrainIndexPatches terrainIndices;
        readonly List<ArtilleryEarthwork> earthworkScratch=new List<ArtilleryEarthwork>(64);
        readonly HashSet<ArtilleryEarthwork> uniqueEarthworks=new HashSet<ArtilleryEarthwork>();
        readonly Vector4[] earthworkShapes=new Vector4[32],earthworkAxes=new Vector4[32];
        readonly Dictionary<Vector2Int, Mesh> damagedGround = new Dictionary<Vector2Int, Mesh>();
        void UpdateEarthworks()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Earthworks);
            if (damageRevision == world.Terrain.DamageRevision) return;
            bool topologyChanged=false;
            foreach (var entry in world.Terrain.DamageCells)
            {
                if (entry.Value <= damageRevision) continue;
                bool existed = damagedGround.TryGetValue(entry.Key, out var mesh);
                if (world.Terrain.HasDamage(entry.Key))
                {
                    mesh=existed?world.Terrain.RefreshDamageMesh(entry.Key):world.Terrain.AcquireDamageMesh(entry.Key);damagedGround[entry.Key]=mesh;
                }
                else if(existed){world.Terrain.ReleaseDamageMesh(entry.Key);damagedGround.Remove(entry.Key);}
                bool damaged=world.Terrain.HasDamage(entry.Key);
                if(existed!=damaged){terrainIndices.SetDamaged(entry.Key,damaged);topologyChanged=true;}
            }
            if(topologyChanged)terrainIndices.Flush();
            foreach (var batch in batches)
            {
                if(batch.Material!=grassMaterial || !world.Terrain.DamageChangedIn(batch.Bounds,damageRevision))continue;
                world.Terrain.EarthworksIn(batch.Bounds,earthworkScratch,uniqueEarthworks);
                var cuts=earthworkScratch;var shapes=earthworkShapes;var axes=earthworkAxes;int count=Mathf.Min(32,cuts.Count);
                for (int i = 0; i < count; i++)
                {
                    var cut = cuts[i]; shapes[i] = new Vector4(cut.Center.x,cut.Center.y,cut.Radius,cut.HalfLength);
                    axes[i] = new Vector4(cut.Forward.x,cut.Forward.y,cut.Seed,cut.Shell ? 1 : 0);
                }
                if (batch.DamageProperties == null) batch.DamageProperties = new MaterialPropertyBlock();
                batch.DamageProperties.SetFloat("_EarthworkCount",count);
                batch.DamageProperties.SetVectorArray("_EarthworkShape",shapes); batch.DamageProperties.SetVectorArray("_EarthworkAxes",axes);
            }
            damageRevision = world.Terrain.DamageRevision;
        }
        public int SceneryInstances { get; private set; }
        public bool GrassEnabled { get; set; } = true;
        public int NearGrassVertices { get; private set; }
        public int FarGrassVertices { get; private set; }
        public int SubmittedGrassVertices { get; private set; }
        public int SubmittedGrassPatches { get; private set; }
        public int GrassPatches { get; private set; }
        public int HedgeSegments { get; private set; }
        public int TerrainVertices => terrainMesh?terrainMesh.vertexCount:0;

        public void Initialize(BattleWorld value,Camera camera)
        {
            world=value;battleCamera=camera;
            var shader=Resources.Load<Shader>("RuralSurface");
            sceneryMaterial=new Material(shader){enableInstancing=true};
            terrainMaterial=new Material(shader);
            terrainMaterial.SetColor("_BaseColor",world.Terrain.Profile?.GroundTint??new Color(.31f,.38f,.17f));
            grassMaterial=new Material(shader){enableInstancing=true};
            grassMaterial.SetFloat("_WindStrength",.16f);
            grassMaterial.SetFloat("_GrassDistance",200f);
            grassMaterial.SetFloat("_Cull",0f);
            grassMaterial.SetFloat("_GrassLodStart",35f);
            grassMaterial.SetFloat("_GrassLodEnd",65f);
            foliageMaterial=new Material(shader){enableInstancing=true,name="Branch and leaf vegetation"};
            foliageMaterial.SetFloat("_Cull",0f);
            foliageMaterial.SetFloat("_SceneryDetail",1f);
            foliageMaterial.SetFloat("_FoliageWind",.12f);
            var bark=Resources.Load<Texture2D>("RuralScenery/bark_brown_02_diff_1k");
            if(bark){bark.wrapMode=TextureWrapMode.Repeat;bark.anisoLevel=8;foliageMaterial.SetTexture("_BarkTex",bark);}
            owned.Add(foliageMaterial);
            owned.Add(grassMaterial);
            owned.Add(sceneryMaterial);owned.Add(terrainMaterial);
            var texture=Resources.Load<Texture2D>("RuralScenery/leafy_grass_diff_1k");
            if(texture)
            {
                texture.wrapMode=TextureWrapMode.Repeat;
                texture.anisoLevel=8;
                terrainMaterial.SetTexture("_DetailTex",texture);
                terrainMaterial.SetFloat("_DetailStrength",.85f*(1-(world.Terrain.Profile?.Snow??0)));
            }
            BuildSurfaceMap();
            BuildGroundCoverTexture();
            BuildGrassHeightMap();
            BuildTerrain();
            BuildScenery();
        }

        void BuildTerrain()
        {
            int n=BattlefieldTerrain.Resolution;
            var vertices=new Vector3[n*n];
            var colors=new Color[n*n];
            var triangles=new int[(n-1)*(n-1)*6];
            float half=world.Config.FieldHalfSize,step=half*2/(n-1);
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
            {
                var point=new Vector2(-half+x*step,-half+z*step);
                int i=z*n+x;
                vertices[i]=world.Terrain.SurfacePoint(point);
                colors[i]=GroundColor(point);
            }
            int cursor=0;
            for(int z=0;z<n-1;z++)for(int x=0;x<n-1;x++)
            {
                int i=z*n+x;
                triangles[cursor++]=i;triangles[cursor++]=i+n;triangles[cursor++]=i+1;
                triangles[cursor++]=i+1;triangles[cursor++]=i+n;triangles[cursor++]=i+n+1;
            }
            terrainMesh=new Mesh{name="Shared battlefield height grid",indexFormat=IndexFormat.UInt32};
            terrainMesh.vertices=vertices;terrainMesh.colors=colors;terrainMesh.triangles=triangles;
            terrainMesh.RecalculateNormals();terrainMesh.RecalculateBounds();
            terrainIndices=new TerrainIndexPatches(terrainMesh);
            owned.Add(terrainMesh);
        }

        void BuildSurfaceMap()
        {
            const int resolution=1024;
            var texture=new Texture2D(resolution,resolution,TextureFormat.RGBA32,true,true)
                {name="Rural field surface map",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Trilinear,anisoLevel=4};
            var pixels=new Color[resolution*resolution];
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++)
            {
                var p=new Vector2((x+.5f)/resolution*3000f-1500f,(z+.5f)/resolution*3000f-1500f);
                Color color=RuralFieldLayout.Surface(p);
                float scale=world.Config.FieldHalfSize/1500f;
                for(int w=0;w<world.Terrain.WoodsCenters.Length;w++)
                {
                    float distance=Vector2.Distance(p*scale,world.Terrain.WoodsCenters[w]);
                    float radius=world.Terrain.WoodsRadii[w];
                    float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(radius*.65f,radius*1.12f,distance));
                    Color shade=new Color(.19f,.235f,.10f,color.a);
                    color=Color.Lerp(color,shade,weight*.65f);
                }
                var profile=world.Terrain.Profile;
                if(profile!=null)
                {
                    color=Color.Lerp(color,new Color(.66f,.52f,.31f,color.a),profile.Aridity);
                    color=Color.Lerp(color,new Color(.88f,.91f,.92f,color.a),profile.Snow);
                    if(profile.Aridity>.6f||profile.Snow>.5f)color.a=0;
                    if(profile.Coast&&p.x<-1150)color=new Color(.69f,.62f,.42f,0);
                    if(world.Terrain.InsideTown(p*scale))color=new Color(.45f,.40f,.32f,0);
                }
                pixels[z*resolution+x]=color;
            }
            texture.SetPixels(pixels);texture.Apply(true,true);owned.Add(texture);
            terrainMaterial.SetTexture("_SurfaceMap",texture);
            terrainMaterial.SetFloat("_UseSurfaceMap",world.Config.EnableLandscape?1f:0f);
            terrainMaterial.SetFloat("_MapSize",world.Config.FieldHalfSize*2f);
        }

        Color GroundColor(Vector2 point) => Color.white;

        Mesh LoadMesh(string name)
        {
            var path=Path.Combine(Application.streamingAssetsPath,"RuralScenery",name+".json");
            if(!File.Exists(path))path=Path.Combine("Assets","StreamingAssets","RuralScenery",name+".json");
            if(!File.Exists(path)){Debug.LogWarning("Missing rural scenery mesh: "+name);return null;}
            var data=JsonUtility.FromJson<MeshData>(File.ReadAllText(path));
            if(data==null||data.vertices==null||data.normals==null||data.colors==null||data.triangles==null||
                data.vertices.Length%3!=0||data.normals.Length!=data.vertices.Length||data.colors.Length!=data.vertices.Length/3*4)
                throw new InvalidDataException("Invalid rural scenery mesh: "+name);
            int count=data.vertices.Length/3;
            var vertices=new Vector3[count];var normals=new Vector3[count];var colors=new Color[count];
            for(int i=0;i<count;i++)
            {
                int v=i*3,c=i*4;
                vertices[i]=new Vector3(data.vertices[v],data.vertices[v+1],data.vertices[v+2]);
                normals[i]=new Vector3(data.normals[v],data.normals[v+1],data.normals[v+2]);
                colors[i]=new Color(data.colors[c],data.colors[c+1],data.colors[c+2],data.colors[c+3]);
            }
            var mesh=new Mesh{name="CC0 "+name,indexFormat=count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.vertices=vertices;mesh.normals=normals;mesh.colors=colors;mesh.triangles=data.triangles;
            mesh.RecalculateBounds();owned.Add(mesh);return mesh;
        }

        void AddBatch(Mesh mesh,List<Matrix4x4> transforms,Material material=null,float maxDistance=0f,bool castShadows=true,Mesh farMesh=null,float lodDistance=0f,bool partition=true)
        {
            if(!mesh||transforms.Count==0)return;
            if(partition && material==foliageMaterial && transforms.Count>16)
            {
                // A whole woodland/field must not enter close LOD when only one corner is nearby.
                var groups=new Dictionary<Vector2Int,List<Matrix4x4>>();
                foreach(var matrix in transforms)
                {
                    Vector3 root=matrix.GetColumn(3);
                    var cell=new Vector2Int(Mathf.FloorToInt(root.x/64f),Mathf.FloorToInt(root.z/64f));
                    if(!groups.TryGetValue(cell,out var group)){group=new List<Matrix4x4>();groups.Add(cell,group);}
                    group.Add(matrix);
                }
                foreach(var group in groups.Values)AddBatch(mesh,group,material,maxDistance,castShadows,farMesh,lodDistance,false);
                return;
            }
            // Compact patches allow inexpensive frustum culling without per-instance allocations.
            var bounds=new Bounds();
            // Include transformed geometry, including tall trees and wide grass patches.
            for(int i=0;i<transforms.Count;i++)
            {
                var matrix=transforms[i];var local=mesh.bounds;var extent=local.extents;
                Vector3 ex=matrix.MultiplyVector(new Vector3(extent.x,0,0));
                Vector3 ey=matrix.MultiplyVector(new Vector3(0,extent.y,0));
                Vector3 ez=matrix.MultiplyVector(new Vector3(0,0,extent.z));
                Vector3 size=new Vector3(Mathf.Abs(ex.x)+Mathf.Abs(ey.x)+Mathf.Abs(ez.x),
                    Mathf.Abs(ex.y)+Mathf.Abs(ey.y)+Mathf.Abs(ez.y),Mathf.Abs(ex.z)+Mathf.Abs(ey.z)+Mathf.Abs(ez.z))*2;
                var instanceBounds=new Bounds(matrix.MultiplyPoint3x4(local.center),size);
                string meshName=mesh.name.ToLowerInvariant();
                if(meshName.Contains("farmhouse")||meshName.Contains("stable"))ImpactSurfaces.Register(instanceBounds,SmallArmsSurface.Masonry);
                else if(meshName.Contains("fence"))ImpactSurfaces.Register(instanceBounds,SmallArmsSurface.Wood);
                else if(meshName.Contains("tree")||meshName.Contains("hedgerow"))ImpactSurfaces.Register(instanceBounds,SmallArmsSurface.Foliage);
                if(i==0)bounds=instanceBounds;else bounds.Encapsulate(instanceBounds);
            }
            bounds.Expand(1f);
            var newBatch = new Batch{Mesh=mesh,Transforms=transforms.ToArray(),Bounds=bounds, Broken = new bool[transforms.Count],
                Material=material?material:sceneryMaterial,MaxDistance=maxDistance,CastShadows=castShadows,FarMesh=farMesh,LodDistance=lodDistance};
            batches.Add(newBatch);
            if (mesh.name.ToLowerInvariant().Contains("tree"))
                for (int i = 0; i < transforms.Count; i++)
                {
                    var matrix = transforms[i]; Vector3 root = matrix.MultiplyPoint3x4(new Vector3(0, mesh.bounds.min.y, 0));
                    var key = TreeCell(root);
                    if (!treeCells.TryGetValue(key, out var trees)) { trees = new List<Tree>(); treeCells[key] = trees; }
                    trees.Add(new Tree { Batch = newBatch, Index = i, Root = root, Height = matrix.MultiplyVector(Vector3.up * mesh.bounds.size.y).magnitude });
                }
            SceneryInstances+=transforms.Count;
        }

        static float RandomFloat(System.Random random,float min,float max) => min+(float)random.NextDouble()*(max-min);
        Vector3 HeightScale(Mesh mesh,float height)
        {
            float scale=height/Mathf.Max(.01f,mesh.bounds.size.y);
            return Vector3.one*scale;
        }

        void BuildScenery()
        {
            if(world.Terrain.WoodsCenters.Length==0&&world.Terrain.FarmCenters.Length==0&&world.Terrain.TownCenters.Length==0)return;
            var trees=new Mesh[6];var farTrees=new Mesh[6];
            for(int kind=0;kind<trees.Length;kind++)
            {
                trees[kind]=ProceduralVegetation.Tree(kind/2,8103+kind*73);
                farTrees[kind]=ProceduralVegetation.Tree(kind/2,8103+kind*73,true);
                owned.Add(trees[kind]);owned.Add(farTrees[kind]);
            }
            var grass=LoadMesh("grass_large");var rock=LoadMesh("rock_largeA");
            var fence=LoadMesh("fence_simple");var wheat=LoadMesh("crops_wheatStageB");
            var houses=new[]{LoadMesh("farmhouse"),LoadMesh("farmhouse_b")};var stable=LoadMesh("stable");
            var random=new System.Random(world.Config.Seed+309);
            float mapScale=world.Config.FieldHalfSize/1500f;
            for(int w=0;w<world.Terrain.WoodsCenters.Length;w++)
            {
                var center=world.Terrain.WoodsCenters[w];float radius=world.Terrain.WoodsRadii[w];
                for(int kind=0;kind<trees.Length;kind++)
                {
                    if(!trees[kind])continue;
                    var transforms=new List<Matrix4x4>(60);
                    for(int i=0;i<60;i++)
                    {
                        float angle=RandomFloat(random,0,Mathf.PI*2),r=Mathf.Sqrt(RandomFloat(random,0,1))*radius;
                        float edge=.78f+.15f*Mathf.Sin(angle*3+w)+.10f*Mathf.Sin(angle*7-w*.4f);
                        var p=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r*edge;
                        if(world.Terrain.IsBlocked(p,5))continue;
                        float height=RandomFloat(random,10,19)*Mathf.Min(1,mapScale);
                        transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p,-.2f),Quaternion.Euler(0,RandomFloat(random,0,360),0),Vector3.Scale(HeightScale(trees[kind],height),new Vector3(RandomFloat(random,.82f,1.2f),1,RandomFloat(random,.85f,1.18f)))));
                    }
                    AddBatch(trees[kind],transforms,foliageMaterial,0f,true,farTrees[kind],180f);
                }
                if(grass)
                {
                    var transforms=new List<Matrix4x4>(130);
                    for(int i=0;i<130;i++)
                    {
                        float angle=RandomFloat(random,0,Mathf.PI*2),r=Mathf.Sqrt(RandomFloat(random,0,1))*radius;
                        float edge=.78f+.15f*Mathf.Sin(angle*3+w)+.10f*Mathf.Sin(angle*7-w*.4f);
                        var p=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*r*edge;
                        transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p),Quaternion.Euler(0,RandomFloat(random,0,360),0),HeightScale(grass,RandomFloat(random,.3f,.7f))));
                    }
                    AddBatch(grass,transforms);
                }
                if(rock)
                {
                    var transforms=new List<Matrix4x4>(12);
                    for(int i=0;i<12;i++)
                    {
                        var p=center+new Vector2(RandomFloat(random,-radius,radius),RandomFloat(random,-radius,radius))*.7f;
                        transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p,-.15f),Quaternion.Euler(0,RandomFloat(random,0,360),0),HeightScale(rock,RandomFloat(random,.6f,2f))));
                    }
                    AddBatch(rock,transforms);
                }
            }
            for(int block=0;block<world.Terrain.TownCenters.Length;block++)for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++)
            {
                var house=houses[(block+x+z+6)%houses.Length];if(!house)continue;
                Vector2 point=world.Terrain.TownCenters[block]+new Vector2(x*22,z*17);
                Vector3 size=house.bounds.size;
                AddBatch(house,new List<Matrix4x4>{Matrix4x4.TRS(world.Terrain.SurfacePoint(point),Quaternion.identity,new Vector3(18/size.x,11/size.y,14/size.z))});
            }
            if(world.Terrain.Profile==null||world.Terrain.Profile.Aridity<.4f&&world.Terrain.Profile.Snow<.4f)
                BuildFieldBoundaries(trees,farTrees,random,mapScale);
            if(world.Terrain.Profile==null||world.Terrain.Profile.Aridity<.6f&&world.Terrain.Profile.Snow<.5f)BuildMeadowGrass();
            for(int f=0;f<world.Terrain.FarmCenters.Length;f++)
            {
                var center=world.Terrain.FarmCenters[f];var half=world.Terrain.FarmHalfExtents[f];
                Mesh house=houses[f%houses.Length];
                if(house)
                {
                    var size=house.bounds.size;
                    Vector3 scale=new Vector3(half.x*1.15f/size.x,11f*Mathf.Min(1,mapScale)/size.y,half.y*1.8f/size.z);
                    AddBatch(house,new List<Matrix4x4>{Matrix4x4.TRS(world.Terrain.SurfacePoint(center+Vector2.left*half.x*.35f),Quaternion.identity,scale)});
                }
                if(stable)
                {
                    var size=stable.bounds.size;
                    Vector3 scale=new Vector3(half.x*.6f/size.x,6f*Mathf.Min(1,mapScale)/size.y,half.y*1.5f/size.z);
                    AddBatch(stable,new List<Matrix4x4>{Matrix4x4.TRS(world.Terrain.SurfacePoint(center+Vector2.right*half.x*.68f),Quaternion.identity,scale)});
                }
                if(fence)
                {
                    var transforms=new List<Matrix4x4>(160);
                    Vector2 field=new Vector2(140,95)*mapScale;
                    float segment=5f*Mathf.Min(1,mapScale);
                    float meshLength=Mathf.Max(fence.bounds.size.x,fence.bounds.size.z);
                    Vector3 scale=Vector3.one*(segment/Mathf.Max(.01f,meshLength));
                    for(float x=-field.x;x<field.x;x+=segment)
                        for(int side=-1;side<=1;side+=2)
                        {
                            if(Mathf.Abs(x)<8*mapScale)continue;
                            var p=center+new Vector2(x,side*field.y);
                            transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p),Quaternion.identity,scale));
                        }
                    for(float z=-field.y;z<field.y;z+=segment)
                        for(int side=-1;side<=1;side+=2)
                        {
                            var p=center+new Vector2(side*field.x,z);
                            transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p),Quaternion.Euler(0,90,0),scale));
                        }
                    AddBatch(fence,transforms);
                }
                if(wheat)
                {
                    var transforms=new List<Matrix4x4>(240);
                    for(int row=0;row<12;row++)for(int col=0;col<20;col++)
                    {
                        var p=center+new Vector2(-120+col*12,-75+row*13)*mapScale;
                        if(world.Terrain.IsBlocked(p,10))continue;
                        transforms.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(p),Quaternion.identity,HeightScale(wheat,.7f)));
                    }
                    AddBatch(wheat,transforms);
                }
            }
        }

        void BuildFieldBoundaries(Mesh[] trees,Mesh[] farTrees,System.Random random,float mapScale)
        {
            var hedgesMeshes=new Mesh[3];var distantHedges=new Mesh[3];
            for(int variant=0;variant<3;variant++)
            {
                hedgesMeshes[variant]=ProceduralVegetation.Hedge(7103+variant*131);
                distantHedges[variant]=ProceduralVegetation.Hedge(7103+variant*131,true);
                owned.Add(hedgesMeshes[variant]);owned.Add(distantHedges[variant]);
            }
            int fieldIndex=0;
            foreach(var field in RuralFieldLayout.Fields)
            {
                var hedges=new List<Matrix4x4>();var standards=new List<Matrix4x4>();
                for(int side=0;side<4;side++)
                {
                    bool horizontal=side<2;
                    float length=(horizontal?field.Half.x:field.Half.y)*2f;
                    int count=Mathf.CeilToInt(length/4f);
                    for(int i=0;i<count;i++)
                    {
                        float along=-length*.5f+(i+.5f)*length/count;
                        // Wide, readable gates in each boundary; farm entrances remain open.
                        if(Mathf.Abs(along)<10f)continue;
                        Vector2 p=field.Center+(horizontal?new Vector2(along,side==0?-field.Half.y:field.Half.y):
                            new Vector2(side==2?-field.Half.x:field.Half.x,along));
                        if(RuralFieldLayout.RoadDistance(p)<10f)continue;
                        Vector2 actual=p*mapScale;
                        float height=RandomFloat(random,1.25f,2.05f)*Mathf.Min(1f,mapScale);
                        hedges.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(actual,-.12f),Quaternion.Euler(0,horizontal?0:90,0),
                            new Vector3(length/count*mapScale/4f,height/2f,RandomFloat(random,1.2f,2.1f)*mapScale/1.8f)));
                        if(i%22==9 && trees[0])
                            standards.Add(Matrix4x4.TRS(world.Terrain.SurfacePoint(actual),Quaternion.Euler(0,RandomFloat(random,0,360),0),
                                HeightScale(trees[0],RandomFloat(random,9f,15f)*Mathf.Min(1f,mapScale))));
                    }
                }
                HedgeSegments+=hedges.Count;
                int variant=fieldIndex++%3;
                AddBatch(hedgesMeshes[variant],hedges,foliageMaterial,0f,true,distantHedges[variant],110f);
                AddBatch(trees[variant*2],standards,foliageMaterial,0f,true,farTrees[variant*2],180f);
            }
        }

        // Texel centers correspond exactly to the simulation's height-grid vertices.
        void BuildGroundCoverTexture()
        {
            // Static, seamless turf relief: thousands of fine blades become one mipmapped sample.
            const int size=512;
            var pixels=new Color32[size*size];
            var random=new System.Random(1981);
            for(int i=0;i<pixels.Length;i++)pixels[i]=new Color32(128,128,128,24);
            for(int blade=0;blade<9000;blade++)
            {
                float x=random.Next(size),y=random.Next(size),angle=RandomFloat(random,0,Mathf.PI*2);
                float length=RandomFloat(random,7,28),width=RandomFloat(random,.65f,1.8f);
                float shade=RandomFloat(random,.42f,.88f);
                Vector2 direction=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                Vector2 side=new Vector2(-direction.y,direction.x);
                for(int t=0;t<Mathf.CeilToInt(length);t++)
                {
                    float phase=t/length,span=width*(1-phase);
                    Vector2 center=new Vector2(x,y)+direction*t+side*Mathf.Sin(phase*Mathf.PI)*length*.12f;
                    for(int w=-2;w<=2;w++)
                    {
                        float coverage=Mathf.Clamp01(span+.5f-Mathf.Abs(w));
                        if(coverage<=0)continue;
                        Vector2 point=center+side*w;
                        int px=((Mathf.RoundToInt(point.x)%size)+size)%size,py=((Mathf.RoundToInt(point.y)%size)+size)%size;
                        float value=Mathf.Lerp(.3f,shade,phase);
                        Color color=pixels[py*size+px];
                        pixels[py*size+px]=Color.Lerp(color,new Color(value,value,value,.2f+phase*.65f),coverage);
                    }
                }
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,true,true)
                {name="Static fine turf color and relief",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8};
            texture.SetPixels32(pixels);texture.Apply(true,true);owned.Add(texture);
            terrainMaterial.SetTexture("_GroundCoverTex",texture);
            var profile=world.Terrain.Profile;
            terrainMaterial.SetFloat("_GroundCoverStrength",.65f*(1-(profile?.Snow??0))*(1-(profile?.Aridity??0)));
        }

        void BuildGrassHeightMap()
        {
            int n=BattlefieldTerrain.Resolution;
            float half=world.Config.FieldHalfSize;
            var texture=new Texture2D(n,n,TextureFormat.RFloat,false,true)
                {name="Grass root terrain heights",wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
            var heights=new float[n*n];
            for(int z=0;z<n;z++)for(int x=0;x<n;x++)
                heights[z*n+x]=world.Terrain.HeightAt(new Vector2(-half+x*half*2/(n-1),-half+z*half*2/(n-1)));
            texture.SetPixelData(heights,0);texture.Apply(false,true);owned.Add(texture);
            grassMaterial.SetTexture("_HeightMap",texture);
            grassMaterial.SetTexture("_SurfaceMap",terrainMaterial.GetTexture("_SurfaceMap"));
            grassMaterial.SetFloat("_MapSize",half*2);
            grassMaterial.SetFloat("_HeightResolution",n);
        }

        void BuildMeadowGrass()
        {
            const float tile=16f;
            var mesh=BuildGrassMesh(tile);owned.Add(mesh);
            // Fine sod is supplied by the terrain texture and relief, without extra geometry.
            var farMesh=BuildGrassMesh(tile,true);owned.Add(farMesh);
            NearGrassVertices=mesh.vertexCount;FarGrassVertices=farMesh.vertexCount;
            float half=world.Config.FieldHalfSize;
            int n=Mathf.CeilToInt(half*2/tile);
            var random=new System.Random(world.Config.Seed+927);
            for(int gz=0;gz<n;gz+=4)for(int gx=0;gx<n;gx+=4)
            {
                var transforms=new List<Matrix4x4>(16);
                for(int z=gz;z<Mathf.Min(n,gz+4);z++)for(int x=gx;x<Mathf.Min(n,gx+4);x++)
                {
                    var p=new Vector2(-half+(x+.5f)*tile,-half+(z+.5f)*tile);
                    // Coverage and grounding are evaluated per tuft in the vertex shader.
                    // Tiles are only batching units, never vegetation exclusion masks.
                    transforms.Add(Matrix4x4.TRS(new Vector3(p.x,0,p.y),Quaternion.Euler(0,random.Next(4)*90,0),
                        new Vector3(1,RandomFloat(random,.65f,1f),1)));
                }
                GrassPatches+=transforms.Count;
                AddBatch(mesh,transforms,grassMaterial,200f,false,farMesh,65f,false);
                // The shader raises roots from y=0 onto the landscape: include those heights in culling.
                var batch=batches[batches.Count-1];
                batch.InstanceBounds=new Bounds[transforms.Count];
                float step=half*2/(BattlefieldTerrain.Resolution-1);
                for(int i=0;i<transforms.Count;i++)
                {
                    Vector3 root=transforms[i].GetColumn(3);
                    // Near and far geometry, height-map interpolation and wind all fit this box.
                    Vector3 min=root-new Vector3(tile*.5f+1,0,tile*.5f+1),max=root+new Vector3(tile*.5f+1,0,tile*.5f+1);
                    float minHeight=float.MaxValue,maxHeight=float.MinValue;
                    int x0=Mathf.FloorToInt((min.x+half)/step),x1=Mathf.CeilToInt((max.x+half)/step);
                    int z0=Mathf.FloorToInt((min.z+half)/step),z1=Mathf.CeilToInt((max.z+half)/step);
                    for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
                    {
                        float h=world.Terrain.HeightAt(new Vector2(-half+x*step,-half+z*step));
                        minHeight=Mathf.Min(minHeight,h);maxHeight=Mathf.Max(maxHeight,h);
                    }
                    var bounds=new Bounds();
                    bounds.SetMinMax(new Vector3(min.x,minHeight-.1f,min.z),new Vector3(max.x,maxHeight+1.8f,max.z));
                    batch.InstanceBounds[i]=bounds;
                    if(i==0)batch.Bounds=bounds;else batch.Bounds.Encapsulate(bounds);
                }
            }
        }

        Mesh BuildGrassMesh(float width,bool distant=false)
        {
            var vertices=new List<Vector3>();var normals=new List<Vector3>();var colors=new List<Color>();var triangles=new List<int>();
            var roots=new List<Vector2>();
            var random=new System.Random(891);
            for(int clump=0;clump<700;clump++)
            {
                var root=new Vector3(RandomFloat(random,-width*.5f,width*.5f),0,RandomFloat(random,-width*.5f,width*.5f));
                bool tallTuft=clump%4==0;
                int blades=tallTuft?7:5;
                for(int blade=0;blade<blades;blade++)
                {
                    float angle=RandomFloat(random,0,Mathf.PI*2);
                    float height=RandomFloat(random,tallTuft?.65f:.22f,tallTuft?1.15f:.48f);
                    float span=RandomFloat(random,tallTuft?.012f:.025f,tallTuft?.028f:.05f);
                    var bladeRoot=root+new Vector3(RandomFloat(random,-.075f,.075f),0,RandomFloat(random,-.075f,.075f));
                    var across=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*span;
                    var lean=new Vector3(-Mathf.Sin(angle),0,Mathf.Cos(angle))*height*.25f;
                    var normal=new Vector3(-Mathf.Sin(angle),.35f,Mathf.Cos(angle)).normalized;
                    Color bottom=Color.Lerp(new Color(.17f,.23f,.065f),new Color(.3f,.33f,.12f),RandomFloat(random,0,1));
                    Color top=Color.Lerp(bottom,new Color(.49f,.47f,.24f),RandomFloat(random,.15f,.55f));
                    bool retained=tallTuft && blade<2;
                    if(distant && !retained)continue;
                    bottom.a=top.a=retained?1f:0f;
                    int start=vertices.Count;
                    vertices.Add(bladeRoot-across);vertices.Add(bladeRoot+across);
                    if(distant)
                    {
                        vertices.Add(bladeRoot+Vector3.up*height+lean);
                        for(int v=0;v<3;v++){normals.Add(normal);colors.Add(v<2?bottom:top);roots.Add(new Vector2(root.x,root.z));}
                        triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
                        continue;
                    }
                    vertices.Add(bladeRoot+Vector3.up*height*.55f+lean*.4f-across*.5f);
                    vertices.Add(bladeRoot+Vector3.up*height*.55f+lean*.4f+across*.5f);
                    vertices.Add(bladeRoot+Vector3.up*height+lean);
                    for(int v=0;v<5;v++){normals.Add(normal);colors.Add(v<2?bottom:top);roots.Add(new Vector2(root.x,root.z));}
                    triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
                    triangles.Add(start+1);triangles.Add(start+3);triangles.Add(start+2);
                    triangles.Add(start+2);triangles.Add(start+3);triangles.Add(start+4);
                }
            }
            var mesh=new Mesh{name=distant?"Distant meadow silhouettes":"Meadow blade patch",indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(vertices);mesh.SetNormals(normals);
            mesh.SetColors(colors);mesh.SetUVs(0,roots);mesh.SetTriangles(triangles,0);mesh.RecalculateBounds();
            // Wind displaces tips beyond their rest bounds.
            var bounds=mesh.bounds;bounds.Expand(.5f);mesh.bounds=bounds;return mesh;
        }

        void DrawMeadowBatch(Batch batch)
        {
            int nearCount=0,farCount=0;
            Vector3 camera=battleCamera.transform.position;
            for(int i=0;i<batch.Transforms.Length;i++)
            {
                Bounds bounds=batch.InstanceBounds[i];
                float distance=bounds.SqrDistance(camera);
                if(distance>batch.MaxDistance*batch.MaxDistance || !GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                if(distance<=batch.LodDistance*batch.LodDistance)scratch[nearCount++]=batch.Transforms[i];
                else crownScratch[farCount++]=batch.Transforms[i];
            }
            if(nearCount>0)Graphics.DrawMeshInstanced(batch.Mesh,0,batch.Material,scratch,nearCount,batch.DamageProperties,ShadowCastingMode.Off,true,0,battleCamera,LightProbeUsage.Off);
            if(farCount>0)Graphics.DrawMeshInstanced(batch.FarMesh,0,batch.Material,crownScratch,farCount,batch.DamageProperties,ShadowCastingMode.Off,true,0,battleCamera,LightProbeUsage.Off);
            SubmittedGrassPatches+=nearCount+farCount;
            SubmittedGrassVertices+=nearCount*batch.Mesh.vertexCount+farCount*batch.FarMesh.vertexCount;
        }

        void Update()
        {
            using var metricScope = BattleMetrics.Measure(BattleMetrics.Stage.Landscape);
            if(world==null||!battleCamera)return;
            UpdateEarthworks();
            ConsumeArtilleryDamage();
            foreach (var mesh in damagedGround.Values)
                if (mesh.bounds.SqrDistance(battleCamera.transform.position) < 1600f * 1600f)
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, terrainMaterial, 0, battleCamera);
            Graphics.DrawMesh(terrainMesh,Matrix4x4.identity,terrainMaterial,0,battleCamera);
            GeometryUtility.CalculateFrustumPlanes(battleCamera,planes);
            SubmittedGrassVertices=SubmittedGrassPatches=0;
            foreach(var batch in batches)
            {
                bool meadow=batch.Material==grassMaterial;
                if(meadow && !GrassEnabled)continue;
                if(batch.MaxDistance>0 && batch.Bounds.SqrDistance(battleCamera.transform.position)>batch.MaxDistance*batch.MaxDistance)continue;
                if(!GeometryUtility.TestPlanesAABB(planes,batch.Bounds))continue;
                if(meadow)
                {
                    DrawMeadowBatch(batch);
                    continue;
                }
                for(int start=0;start<batch.Transforms.Length;start+=scratch.Length)
                {
                    int end = Mathf.Min(start + scratch.Length, batch.Transforms.Length), count = 0;
                    for (int i = start; i < end; i++)
                    {
                        if (batch.Broken[i]) continue;
                        var m = batch.Transforms[i];
                        Vector3 root = m.GetColumn(3);
                        if ((batch.Mesh.name.Contains("grass_large") || batch.Mesh.name.Contains("wheat"))
                            && Mathf.Abs(world.Terrain.HeightAt(new Vector2(root.x, root.z)) - world.Terrain.BaseHeightAt(new Vector2(root.x, root.z))) > .045f) continue;
                        scratch[count++] = m;
                    }
                    if (count == 0) continue;
                    var mesh=batch.FarMesh && batch.Bounds.SqrDistance(battleCamera.transform.position)>batch.LodDistance*batch.LodDistance?batch.FarMesh:batch.Mesh;
                    if(meadow){SubmittedGrassVertices+=mesh.vertexCount*count;SubmittedGrassPatches+=count;}
                    Graphics.DrawMeshInstanced(mesh,0,batch.Material,scratch,count,batch.DamageProperties,batch.CastShadows?ShadowCastingMode.On:ShadowCastingMode.Off,true,0,battleCamera,LightProbeUsage.Off);
                }
            }
        }
        void OnDestroy()
        {
            foreach(var key in damagedGround.Keys)world.Terrain.ReleaseDamageMesh(key);
            damagedGround.Clear();
            foreach(var item in owned)if(item)
            {
                if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);
            }
            owned.Clear();batches.Clear();
        }
    }
}
