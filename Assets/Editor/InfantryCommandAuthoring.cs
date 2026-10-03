using System;
using EmpireBattle.AnimationAuthoring;
using UnityEngine;
using UnityEditor;

// Simple shared geometry on the existing rig, baked with the faction atlas.
public static class InfantryCommandAuthoring
{
    public static Material[] Colours(int side)
    {
        var colours=new[]{new Color(.08f,.16f,.42f),new Color(.88f,.85f,.73f),new Color(.65f,.06f,.04f),new Color(.72f,.52f,.17f),new Color(.10f,.07f,.045f),new Color(.32f,.18f,.09f)};
        var result=new Material[colours.Length];
        for(int i=0;i<colours.Length;i++){
            string path="Assets/Art/NapoleonicInfantry/Materials/CommandColour"+i+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));material.color=colours[i];AssetDatabase.CreateAsset(material,path);}
            result[i]=material;
        }
        return result;
    }
    public static GameObject Create(GameObject rig,InfantryMotionKind kind,Material cloth,Material metal,Material wood,int side,bool moving=false)
    {
        var root=new GameObject("Command equipment");root.transform.SetParent(rig.transform,false);
        Transform Bone(string name)=>Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name==name);
        void Part(string name,PrimitiveType primitive,Transform parent,Vector3 position,Vector3 scale,Material material,Quaternion rotation)
        {
            var part=GameObject.CreatePrimitive(primitive);part.name=name;UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;part.transform.localRotation=rotation;
            part.GetComponent<Renderer>().sharedMaterial=material;
        }
        if(kind==InfantryMotionKind.Officer||kind==InfantryMotionKind.Colonel){
            // Geometry stays in metres despite the imported hand's FBX unit scale.
            var sword=new GameObject("Sabre");sword.transform.SetParent(Bone("r_hand"),false);sword.transform.localScale=Vector3.one/Bone("r_hand").lossyScale.x;
            Part("Sabre blade",PrimitiveType.Cube,sword.transform,new Vector3(0,.43f,0),new Vector3(.024f,.78f,.009f),metal,Quaternion.Euler(0,0,-8));
            Part("Sabre guard",PrimitiveType.Cube,sword.transform,new Vector3(0,.045f,0),new Vector3(.13f,.018f,.055f),metal,Quaternion.identity);
            Part("Sabre grip",PrimitiveType.Cylinder,sword.transform,Vector3.zero,new Vector3(.028f,.055f,.028f),wood,Quaternion.identity);
            // Reparent under equipment while retaining the bone attachment via a marker below.
            sword.AddComponent<CommandEquipmentMarker>().Owner=root;
            var palette=Colours(side);float lift=kind==InfantryMotionKind.Colonel?.65f:0;
            // A recognisable sidearm at the left hip: barrel, six-shot cylinder and angled grip.
            Part("Revolver barrel",PrimitiveType.Cube,root.transform,new Vector3(.25f,1.00f+lift,-.07f),new Vector3(.035f,.035f,.20f),palette[4],Quaternion.identity);
            Part("Revolver cylinder",PrimitiveType.Cylinder,root.transform,new Vector3(.25f,1.00f+lift,.055f),new Vector3(.067f,.045f,.067f),palette[3],Quaternion.Euler(90,0,0));
            Part("Revolver grip",PrimitiveType.Cube,root.transform,new Vector3(.25f,.94f+lift,.11f),new Vector3(.045f,.12f,.045f),palette[4],Quaternion.Euler(-20,0,0));
            for(int sign=-1;sign<=1;sign+=2)Part("Gold epaulette",PrimitiveType.Sphere,root.transform,new Vector3(sign*.21f,1.46f+lift,0),new Vector3(.13f,.035f,.11f),palette[3],Quaternion.identity);
            if(kind==InfantryMotionKind.Colonel){
                var horse=EmpireBattle.SupportingArt.ProceduralSupportingArt.CreateHorseRig(root.transform,palette[5]);
                horse.Root.transform.localRotation=Quaternion.Euler(0,180,0);
                var horseMarker=root.AddComponent<CommandHorseMarker>();horseMarker.Rig=horse;horseMarker.Moving=moving;
                Part("Saddle leather",PrimitiveType.Cube,root.transform,new Vector3(0,1.61f,.10f),new Vector3(.54f,.10f,.62f),palette[4],Quaternion.identity);
                for(int sign=-1;sign<=1;sign+=2){
                    Part("Saddlecloth",PrimitiveType.Cube,root.transform,new Vector3(sign*.28f,1.30f,.18f),new Vector3(.025f,.48f,.75f),palette[side==0?0:2],Quaternion.identity);
                    Part("Saddlecloth gold edging",PrimitiveType.Cube,root.transform,new Vector3(sign*.295f,1.08f,.18f),new Vector3(.025f,.035f,.76f),palette[3],Quaternion.identity);
                    Part("Stirrup",PrimitiveType.Cube,root.transform,new Vector3(sign*.39f,.68f,-.12f),new Vector3(.12f,.025f,.16f),palette[3],Quaternion.identity);
                    Part("Bridle",PrimitiveType.Cube,root.transform,new Vector3(sign*.135f,1.77f,-.91f),new Vector3(.018f,.21f,.025f),palette[3],Quaternion.Euler(-22,0,0));
                    Part("Rein",PrimitiveType.Cylinder,root.transform,new Vector3(sign*.16f,1.72f,-.47f),new Vector3(.013f,.37f,.013f),palette[4],Quaternion.Euler(68,0,0));
                }
                Part("Colonel sash",PrimitiveType.Cube,root.transform,new Vector3(0,1.79f,-.15f),new Vector3(.31f,.055f,.02f),palette[3],Quaternion.Euler(0,0,-25));
                Part("Colonel plume",PrimitiveType.Sphere,root.transform,new Vector3(0,2.50f,.015f),new Vector3(.08f,.30f,.08f),palette[1],Quaternion.Euler(-12,0,0));
            }
        }else if(kind==InfantryMotionKind.Drummer){
            Part("Drum shell",PrimitiveType.Cylinder,root.transform,new Vector3(0,.83f,-.40f),new Vector3(.43f,.16f,.43f),Colours(side)[side==0?0:2],Quaternion.identity);
            for(int sign=-1;sign<=1;sign+=2){
                Part("Drum skin",PrimitiveType.Cylinder,root.transform,new Vector3(0,.83f+sign*.165f,-.40f),new Vector3(.44f,.012f,.44f),metal,Quaternion.identity);
                var hand=Bone(sign<0?"l_hand":"r_hand");var stick=new GameObject("Drumstick");stick.transform.SetParent(hand,false);stick.transform.localScale=Vector3.one/hand.lossyScale.x;
                Part("Stick",PrimitiveType.Cylinder,stick.transform,new Vector3(0,0,.12f),new Vector3(.012f,.17f,.012f),wood,Quaternion.Euler(90,0,0));
                stick.AddComponent<CommandEquipmentMarker>().Owner=root;
            }
        }else if(kind==InfantryMotionKind.Sergeant){
            var palette=Colours(side);
            Part("Sergeant pike shaft",PrimitiveType.Cylinder,root.transform,new Vector3(.08f,1.52f,-.37f),new Vector3(.027f,1.48f,.027f),palette[5],Quaternion.identity);
            Part("Pike blade",PrimitiveType.Cube,root.transform,new Vector3(.08f,3.10f,-.37f),new Vector3(.065f,.22f,.014f),palette[1],Quaternion.Euler(0,0,18));
            var blade=new Mesh{name="Pointed sergeant pike head"};
            blade.vertices=new[]{new Vector3(0,.5f,0),new Vector3(0,-.5f,0),new Vector3(-.5f,0,0),new Vector3(.5f,0,0),new Vector3(0,0,-.5f),new Vector3(0,0,.5f)};
            blade.triangles=new[]{0,2,4,0,4,3,0,3,5,0,5,2,1,4,2,1,3,4,1,5,3,1,2,5};
            blade.uv=Array.ConvertAll(blade.vertices,v=>Vector2.one*.5f);blade.RecalculateNormals();blade.RecalculateBounds();
            root.transform.Find("Pike blade").GetComponent<MeshFilter>().sharedMesh=blade;
            Part("Pike crossbar",PrimitiveType.Cube,root.transform,new Vector3(.08f,2.97f,-.37f),new Vector3(.14f,.018f,.025f),palette[3],Quaternion.identity);
            Part("Sergeant sash",PrimitiveType.Cube,root.transform,new Vector3(0,.98f,-.12f),new Vector3(.31f,.055f,.025f),palette[2],Quaternion.identity);
        }else{
            Part("Colour staff",PrimitiveType.Cylinder,root.transform,new Vector3(.08f,1.56f,-.37f),new Vector3(.028f,1.48f,.028f),wood,Quaternion.identity);
            Part("Colour finial",PrimitiveType.Sphere,root.transform,new Vector3(.08f,3.07f,-.37f),new Vector3(.075f,.13f,.075f),metal,Quaternion.identity);
            CreateCloth(root, side, false);
            CreateCloth(root, side, true);

        }
        return root;
    }
    public static void PikePose(Transform equipment,float phase,bool thrust)
    {
        float extension=thrust?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.18f,.45f,phase))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.52f,1,phase))):0;
        Vector3 anchor=new Vector3(.08f,1.22f,-.37f);
        Quaternion turn=Quaternion.FromToRotation(Vector3.up,new Vector3(.03f,.12f,-1).normalized);
        foreach(Transform part in equipment){
            if(part.name=="Sergeant sash")continue;
            Vector3 rest=part.name=="Sergeant pike shaft"?new Vector3(.08f,1.52f,-.37f):part.name=="Pike blade"?new Vector3(.08f,3.10f,-.37f):new Vector3(.08f,2.97f,-.37f);
            part.localPosition=anchor+turn*(rest-anchor)+new Vector3(0,0,-extension*.4f);
            part.localRotation=turn*(part.name=="Pike blade"?Quaternion.Euler(0,0,18):Quaternion.identity);
        }
    }
    public static void OfficerFirePose(Transform equipment,float phase)
    {
        float recoil=Mathf.Sin(Mathf.Clamp01(phase)*Mathf.PI);
        Vector3 shift=new Vector3(.29f,1.24f+recoil*.02f,-.48f+recoil*.035f)-new Vector3(.25f,.94f,.11f);
        foreach(Transform part in equipment)if(part.name.StartsWith("Revolver")){
            Vector3 rest=part.name=="Revolver barrel"?new Vector3(.25f,1.00f,-.07f):part.name=="Revolver cylinder"?new Vector3(.25f,1.00f,.055f):new Vector3(.25f,.94f,.11f);
            part.localPosition=rest+shift;
        }
    }
    // Subdivided, double-sided cloth shares the crowd atlas and baked pose interpolation.
    static void CreateCloth(GameObject root,int side,bool pennant)
    {
        const int columns=24,rows=12;
        float width=pennant?.58f:1.02f,height=pennant?.19f:.75f;
        float top=pennant?3.06f:2.80f;
        var vertices=new System.Collections.Generic.List<Vector3>();
        var uv=new System.Collections.Generic.List<Vector2>();
        var triangles=new System.Collections.Generic.List<int>[3];
        for(int i=0;i<3;i++)triangles[i]=new System.Collections.Generic.List<int>();
        for(int y=0;y<rows;y++)for(int x=0;x<columns;x++){
            var polygons=new System.Collections.Generic.List<System.Collections.Generic.List<Vector2>>{
                new System.Collections.Generic.List<Vector2>{new Vector2(x/(float)columns,y/(float)rows),new Vector2((x+1f)/columns,y/(float)rows),new Vector2((x+1f)/columns,(y+1f)/rows),new Vector2(x/(float)columns,(y+1f)/rows)}};
            if(!pennant&&side==1){
                // Split at the exact heraldic boundaries; diagonals stay crisp rather than stair-stepped.
                foreach(float edge in new[]{.41f,.455f,.545f,.59f})SplitPolygons(polygons,new Vector2(1,0),edge);
                foreach(float edge in new[]{.39f,.445f,.555f,.61f})SplitPolygons(polygons,new Vector2(0,1),edge);
                foreach(float edge in new[]{-.075f,-.025f,.025f,.075f}){SplitPolygons(polygons,new Vector2(-1,1),edge);SplitPolygons(polygons,new Vector2(1,1),1+edge);}
            }
            if(pennant){SplitPolygons(polygons,new Vector2(1,-.64f),.68f);SplitPolygons(polygons,new Vector2(1,.64f),1.32f);}
            foreach(var polygon in polygons){
                Vector2 centre=Vector2.zero;foreach(var t in polygon)centre+=t;centre/=polygon.Count;
                if(pennant&&centre.x>1f-.32f*(1f-Mathf.Abs(centre.y*2-1)))continue;
                int colour=pennant?(side==0?0:2):side==0?Mathf.Min(2,x/(columns/3)):BritishColour(centre.x,centre.y);
                for(int face=0;face<2;face++){
                    int first=vertices.Count;
                    foreach(var t in polygon){vertices.Add(new Vector3(.08f+t.x*width,top-t.y*height,-.37f+(face==0?.002f:-.002f)));uv.Add(t);}
                    for(int i=1;i<polygon.Count-1;i++)triangles[colour].AddRange(face==0?new[]{first,first+i+1,first+i}:new[]{first,first+i,first+i+1});
                }
            }
        }
        var mesh=new Mesh{name=pennant?"Forked command pennant":"Subdivided regimental colour"};
        mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=3;
        for(int i=0;i<3;i++)mesh.SetTriangles(triangles[i],i);
        mesh.RecalculateNormals();mesh.RecalculateBounds();
        var cloth=new GameObject(pennant?"Colour pennant":"Colour cloth");cloth.transform.SetParent(root.transform,false);
        cloth.AddComponent<MeshFilter>().sharedMesh=mesh;cloth.AddComponent<MeshRenderer>().sharedMaterials=Colours(side);
        var marker=cloth.AddComponent<CommandClothMarker>();marker.Rest=vertices.ToArray();marker.Width=width;marker.Pennant=pennant;
    }
    static void SplitPolygons(System.Collections.Generic.List<System.Collections.Generic.List<Vector2>> polygons,Vector2 normal,float edge)
    {
        int count=polygons.Count;
        for(int index=count-1;index>=0;index--){
            var source=polygons[index];var negative=new System.Collections.Generic.List<Vector2>();var positive=new System.Collections.Generic.List<Vector2>();
            for(int i=0;i<source.Count;i++){
                Vector2 a=source[i],b=source[(i+1)%source.Count];float da=Vector2.Dot(a,normal)-edge,db=Vector2.Dot(b,normal)-edge;
                if(da<=.000001f)negative.Add(a);if(da>=-.000001f)positive.Add(a);
                if(da*db<-.000000001f){var crossing=Vector2.Lerp(a,b,da/(da-db));negative.Add(crossing);positive.Add(crossing);}
            }
            if(negative.Count>=3&&positive.Count>=3){polygons[index]=negative;polygons.Add(positive);}
        }
    }
    static int BritishColour(float u,float v)
    {
        float diagonal=Mathf.Min(Mathf.Abs(v-u),Mathf.Abs(v-(1-u)));
        if(Mathf.Abs(u-.5f)<.045f||Mathf.Abs(v-.5f)<.055f)return 2;
        if(Mathf.Abs(u-.5f)<.09f||Mathf.Abs(v-.5f)<.11f)return 1;
        if(diagonal<.025f)return 2;
        return diagonal<.075f?1:0;
    }
    public static void Sample(GameObject rig,float phase=0,int commandVariant=0)
    {
        foreach(var cloth in rig.GetComponentsInChildren<CommandClothMarker>())cloth.Sample(phase);
        foreach(var horse in rig.GetComponentsInChildren<CommandHorseMarker>())EmpireBattle.SupportingArt.ProceduralSupportingArt.SampleGait(horse.Rig,horse.Moving?EmpireBattle.SupportingArt.ProceduralSupportingArt.HorseGait.Walk:EmpireBattle.SupportingArt.ProceduralSupportingArt.HorseGait.Idle,phase);
        foreach(var marker in rig.GetComponentsInChildren<CommandEquipmentMarker>()){
            string side=marker.transform.parent.name.StartsWith("l_")?"l":"r";
            var hand=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Closed "+(side=="l"?"left":"right")+" hand");
            marker.transform.position=hand.position+hand.forward*.04f;
            if(marker.name=="Sabre")marker.transform.rotation=rig.transform.rotation*Quaternion.Euler(-20-(commandVariant==3?Mathf.Sin(phase*Mathf.PI)*80:0),0,15+(commandVariant==3?Mathf.Sin(phase*Mathf.PI)*55:0));
            else{
                Vector3 target=rig.transform.TransformPoint(new Vector3(side=="l"?.08f:-.08f,.995f,-.50f));
                marker.transform.rotation=Quaternion.LookRotation(target-marker.transform.position,rig.transform.up);
            }
        }
    }
    public static void Destroy(GameObject root,GameObject rig){
        foreach(var marker in rig.GetComponentsInChildren<CommandEquipmentMarker>())if(marker.Owner==root)UnityEngine.Object.DestroyImmediate(marker.gameObject);
        UnityEngine.Object.DestroyImmediate(root);
    }
}
public sealed class CommandEquipmentMarker:MonoBehaviour { public GameObject Owner; }

public sealed class CommandClothMarker:MonoBehaviour
{
    public Vector3[] Rest;public float Width;public bool Pennant;
    public void Sample(float phase)
    {
        var vertices=new Vector3[Rest.Length];float time=phase*Mathf.PI*2;
        for(int i=0;i<vertices.Length;i++){
            var p=Rest[i];float free=Mathf.Clamp01((p.x-.08f)/Width);
            float wave=Mathf.Sin(time-free*4.5f+(Pennant?.7f:0));
            p.z+=free*((Pennant?.085f:.16f)*wave+.035f*Mathf.Sin(time*2-free*9+p.y*5));
            p.y+=free*(.025f*Mathf.Sin(time-free*3)-.045f*free);
            vertices[i]=p;
        }
        var mesh=GetComponent<MeshFilter>().sharedMesh;mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
    }
}

public sealed class CommandHorseMarker:MonoBehaviour
{
    [NonSerialized] public EmpireBattle.SupportingArt.ProceduralSupportingArt.HorseRig Rig;
    public bool Moving;
}
