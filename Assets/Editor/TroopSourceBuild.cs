using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class TroopSourceBuild
{
    [Serializable] public class Manifest { public Model[] models; }
    [Serializable] public class Model { public string path,pack,name; public Mapping[] materials; }
    [Serializable] public class Mapping { public string name,texture; }
    [Serializable] public class Receipt { public List<Result> models=new List<Result>(); }
    [Serializable] public class Result { public string name,pack,prefab; public int vertices,triangles,skinnedMeshes,bones,untexturedMaterials; }
    const string Root="Assets/Generated/TroopSources";
    public static void Build()
    {
        AssetDatabase.Refresh();
        var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Art/TroopSources/import-manifest.json"));
        var receipt=new Receipt();
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var shader=Shader.Find("Universal Render Pipeline/Lit");
        if(!shader)throw new Exception("Troop sources: URP Lit unavailable");
        int index=0;
        foreach(var item in manifest.models)
        {
            var importer=AssetImporter.GetAtPath(item.path) as ModelImporter;
            if(!importer)throw new Exception("Missing importer "+item.path);
            importer.animationType=ModelImporterAnimationType.Generic;
            importer.importAnimation=false;importer.isReadable=true;
            importer.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(item.path);
            if(!source)throw new Exception("Failed model import "+item.path);
            var instance=UnityEngine.Object.Instantiate(source);instance.name=item.name;
            var record=new Result{name=item.name,pack=item.pack};
            string output=Root+"/"+item.pack;Directory.CreateDirectory(output+"/Materials");
            var cache=new Dictionary<string,Material>();
            foreach(var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if(item.pack=="RegionalArmies")renderer.enabled=renderer.name.Contains(".LOD0");
                Mesh mesh=null;
                if(renderer is SkinnedMeshRenderer skin)
                {
                    mesh=skin.sharedMesh;record.skinnedMeshes++;record.bones+=skin.bones.Length;
                    if(skin.bones.Any(b=>!b))throw new Exception("Missing skin bone "+item.path);
                    if(mesh&&mesh.boneWeights.Any(w=>float.IsNaN(w.weight0+w.weight1+w.weight2+w.weight3)))throw new Exception("Invalid weights "+item.path);
                }
                else if(renderer is MeshRenderer)mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if(!mesh||mesh.vertexCount==0)throw new Exception("Empty source geometry "+item.path);
                if(mesh.vertices.Any(v=>float.IsNaN(v.sqrMagnitude)||float.IsInfinity(v.sqrMagnitude)))throw new Exception("Nonfinite mesh "+item.path);
                record.vertices+=mesh.vertexCount;record.triangles+=mesh.triangles.Length/3;
                var materials=renderer.sharedMaterials;
                for(int i=0;i<materials.Length;i++)
                {
                    string name=materials[i]?materials[i].name:"Default";
                    if(!cache.TryGetValue(name,out var mat))
                    {
                        var mapping=(item.materials??Array.Empty<Mapping>()).FirstOrDefault(m=>name==m.name||name.StartsWith(m.name+"."));
                        string texturePath=mapping?.texture;
                        Texture2D texture=string.IsNullOrEmpty(texturePath)?null:AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                        if(!texture)texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/TroopSources/"+item.pack+"/Textures/"+name+"_d.png");
                        if(!texture)texture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/NapoleonicInfantry/Textures/"+name+"_d.png");
                        if(!texture&&materials[i])texture=materials[i].mainTexture as Texture2D;
                        string path=output+"/Materials/"+item.name+"-"+i+"-"+name.Replace('/','_')+".mat";
                        mat=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,path);}
                        mat.SetTexture("_BaseMap",texture);mat.SetFloat("_Smoothness",.15f);mat.enableInstancing=true;
                        if(!texture&&name=="gap_french_epaulette_red")mat.SetColor("_BaseColor",new Color(.65f,.035f,.035f));
                        else if(!texture)record.untexturedMaterials++;
                        EditorUtility.SetDirty(mat);cache.Add(name,mat);
                    }
                    materials[i]=mat;
                }
                renderer.sharedMaterials=materials;
            }
            if(record.vertices==0)throw new Exception("No renderers "+item.path);
            record.prefab=output+"/"+item.name+".prefab";
            PrefabUtility.SaveAsPrefabAsset(instance,record.prefab);
            instance.transform.position=new Vector3(index%10*4,0,index/10*4);
            var label=new GameObject(item.name+" label");label.transform.position=instance.transform.position+new Vector3(0,.02f,-1.5f);
            var text=label.AddComponent<TextMesh>();text.text=item.pack+"\n"+item.name;text.characterSize=.12f;text.fontSize=32;text.anchor=TextAnchor.MiddleCenter;
            label.transform.rotation=Quaternion.Euler(90,0,0);
            receipt.models.Add(record);index++;
        }
        var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.2f;sun.transform.rotation=Quaternion.Euler(50,-30,0);
        RenderSettings.ambientLight=new Color(.55f,.55f,.55f);
        var camera=new GameObject("Source review camera").AddComponent<Camera>();camera.transform.position=new Vector3(18,30,-28);camera.transform.LookAt(new Vector3(18,0,16));camera.farClipPlane=200;
        EditorSceneManager.SaveScene(scene,Root+"/TroopSourceReview.unity");AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Artifacts/TroopSources");File.WriteAllText("Artifacts/TroopSources/unity-import-receipt.json",JsonUtility.ToJson(receipt,true));
        Debug.Log("TROOP_SOURCES_VERIFIED models="+receipt.models.Count+" vertices="+receipt.models.Sum(r=>r.vertices));
    }
}
