using System; using System.IO; using System.Text; using UnityEditor; using UnityEngine;
public static partial class InfantryAssetBuild {
 const string Root="Assets/Art/NapoleonicInfantry";
 public static void Inspect(){
  var report=new StringBuilder();
  foreach(var f in Directory.GetFiles(Root,"*.FBX",SearchOption.AllDirectories)) InspectOne(f,report);
  foreach(var f in Directory.GetFiles(Root,"*.fbx",SearchOption.AllDirectories)) InspectOne(f,report);
  File.WriteAllText("Logs/infantry-import-inspection.txt",report.ToString());
 }
 static void InspectOne(string f,StringBuilder report){
 var imp=(ModelImporter)AssetImporter.GetAtPath(f); imp.animationType=ModelImporterAnimationType.Generic;imp.importAnimation=true;imp.materialImportMode=ModelImporterMaterialImportMode.ImportStandard;imp.SaveAndReimport();
 var go=AssetDatabase.LoadAssetAtPath<GameObject>(f);report.AppendLine("FILE "+f);foreach(var take in imp.defaultClipAnimations)report.AppendLine($"TAKE {take.name} frames={take.firstFrame}..{take.lastFrame}");
 foreach(var t in go.GetComponentsInChildren<Transform>(true))report.AppendLine($"T {t.name} pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale}");
 foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))report.AppendLine($"SKIN {r.name} bounds={r.localBounds} verts={r.sharedMesh.vertexCount} tris={r.sharedMesh.triangles.Length/3} bones={r.bones.Length} mats={string.Join(",",Array.ConvertAll(r.sharedMaterials,m=>m?m.name:"null"))}");
 foreach(var r in go.GetComponentsInChildren<MeshRenderer>(true))report.AppendLine($"MESH {r.name} bounds={r.GetComponent<MeshFilter>().sharedMesh.bounds} tris={r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3}");
 foreach(var a in AssetDatabase.LoadAllAssetsAtPath(f))if(a is AnimationClip c)report.AppendLine($"CLIP {c.name} duration={c.length} curves={AnimationUtility.GetCurveBindings(c).Length} firstpath={(AnimationUtility.GetCurveBindings(c).Length>0?AnimationUtility.GetCurveBindings(c)[0].path:"")}");
 }
 public static void Build(){ BuildScene(true,3); }
 static bool prepared;
 static bool cartridgeBake;
 static bool rosterSword, swordGuardBake;
 static void BuildScene(bool build,int lod){
 if(!prepared){Inspect(); Directory.CreateDirectory(Root+"/Materials");
 foreach(var f in Directory.GetFiles(Root+"/Textures","*.png")){var ti=(TextureImporter)AssetImporter.GetAtPath(f);ti.maxTextureSize=2048;if(f.EndsWith("_n.png"))ti.textureType=TextureImporterType.NormalMap;ti.SaveAndReimport();}prepared=true;}
 var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
 var rigs=new GameObject[2];var report=new StringBuilder();
 for(int side=0;side<2;side++){
 var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Animations/musket_idle.FBX"));rig.name=side==0?"French":"British";var an=rig.GetComponent<Animator>();if(an)UnityEngine.Object.DestroyImmediate(an);
 var bones=new System.Collections.Generic.Dictionary<string,Transform>();foreach(var t in rig.GetComponentsInChildren<Transform>())bones[t.name]=t;
 var parts= side==0?new[]{"gap_french_infantry.FBX","gap_infantry_pants_french_infantry.fbx","gap_french_shako_line.FBX","gap_french_equipment_beige.FBX","combat_gloves_white.FBX"}:new[]{"gap_british_infantry.FBX","gap_british_infantry_pants.FBX","gap_british_stovepipe_line.FBX","gap_british_equipment_infantry.FBX","combat_gloves_white.FBX"};
 int tris=0;
 foreach(var part in parts){var obj=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/"+part));obj.transform.SetParent(rig.transform,false);foreach(var a in obj.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(a);foreach(var lg in obj.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(lg);
 foreach(var r in obj.GetComponentsInChildren<SkinnedMeshRenderer>()){
 bool selected=r.name.ToLower().Contains(".lod"+lod);r.enabled=selected;if(!selected)continue;
 var originalBones=r.bones;var mesh=UnityEngine.Object.Instantiate(r.sharedMesh);mesh.name=r.sharedMesh.name+"_CommonRig";var bindposes=mesh.bindposes;for(int bi=0;bi<bindposes.Length;bi++){var from=originalBones[bi].lossyScale;var to=bones[originalBones[bi].name].lossyScale;bindposes[bi]=Matrix4x4.Scale(new Vector3(from.x/to.x,from.y/to.y,from.z/to.z))*bindposes[bi];}mesh.bindposes=bindposes;Directory.CreateDirectory(Root+"/Meshes");string meshPath=Root+"/Meshes/"+rig.name+"_"+r.name+".asset";AssetDatabase.DeleteAsset(meshPath);AssetDatabase.CreateAsset(mesh,meshPath);r.sharedMesh=mesh;
 r.bones=Array.ConvertAll(originalBones,b=>bones[b.name]);r.rootBone=bones["pelvis"];r.updateWhenOffscreen=true;
 var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=MaterialFor(mats[i].name,side);r.sharedMaterials=mats;tris+=r.sharedMesh.triangles.Length/3;
 report.AppendLine($"{rig.name} {r.name}: {r.sharedMesh.triangles.Length/3} tris; materials={string.Join(",",Array.ConvertAll(mats,m=>m.name))}");
 }
 }
 var head=GameObject.CreatePrimitive(PrimitiveType.Sphere);head.name="Procedural missing head";head.GetComponent<MeshFilter>().sharedMesh=LowHead();head.transform.position=bones["head"].position+Vector3.up*.085f;head.transform.localScale=new Vector3(.155f,.23f,.17f);head.transform.SetParent(bones["head"],true);head.GetComponent<Renderer>().sharedMaterial=MaterialFor("skin",side);UnityEngine.Object.DestroyImmediate(head.GetComponent<Collider>());
 var weapon=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Models/newmusket.FBX"));weapon.transform.SetParent(rig.transform,false);foreach(var lg in weapon.GetComponentsInChildren<LODGroup>())UnityEngine.Object.DestroyImmediate(lg);
 foreach(var r in weapon.GetComponentsInChildren<MeshRenderer>()){r.enabled=r.name=="newmusket.lod"+lod;if(!r.enabled)continue;r.sharedMaterial=MaterialFor(lod==5?"CoarseWeapon":"newmusket",side);if(lod==5)r.GetComponent<MeshFilter>().sharedMesh=CoarseWeapon();r.transform.position=bones["r_hand"].position-r.transform.TransformVector(new Vector3(0,0,-25));r.transform.SetParent(bones["r_hand"],true);tris+=r.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3;}
 foreach(string sideName in new[]{"l","r"}){var handBone=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name==sideName+"_hand");var hand=new GameObject(sideName=="l"?"Closed left hand":"Closed right hand");hand.transform.SetParent(handBone,false);hand.AddComponent<MeshFilter>().sharedMesh=ClosedHandMesh(sideName=="l");hand.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor("ClosedGloves",side);hand.transform.localScale=Vector3.one/handBone.lossyScale.x;}
 var ramrod=new GameObject("Authored ramrod");ramrod.transform.SetParent(rig.transform,false);ramrod.AddComponent<MeshFilter>().sharedMesh=RamrodMesh();ramrod.AddComponent<MeshRenderer>().sharedMaterial=MaterialFor("Ramrod",side);
 rigs[side]=rig;report.AppendLine(rig.name+" selected imported triangles="+tris);
 }
 var clips=new AnimationClip[3];string[] names={"musket_idle","gp_musket_aimfire","gp_reload_musket_reworked"};for(int i=0;i<3;i++){foreach(var a in AssetDatabase.LoadAllAssetsAtPath(Root+"/Animations/"+names[i]+".FBX"))if(a is AnimationClip c&&!c.name.StartsWith("__preview"))clips[i]=c;}
 foreach(var rig in rigs){clips[1].SampleAnimation(rig,clips[1].length*.4f);var transforms=rig.GetComponentsInChildren<Transform>();var left=Array.Find(transforms,t=>t.name=="l_hand");var right=Array.Find(transforms,t=>t.name=="r_hand");foreach(var renderer in rig.GetComponentsInChildren<MeshRenderer>())if(renderer.enabled&&renderer.name=="newmusket.lod"+lod){renderer.transform.rotation=Quaternion.LookRotation((left.position-right.position).normalized,Vector3.up);renderer.transform.position=right.position-renderer.transform.TransformVector(new Vector3(0,0,-25));}}
 foreach(var rig in rigs){var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name=="newmusket.lod"+lod);EmpireBattle.Art.MusketReloadAuthoring.StowRod(gun.transform,rod);}
 for(int side=0;side<rigs.Length;side++)foreach(var c in clips){var bounds=new Bounds(Vector3.zero,Vector3.zero);for(int frame=0;frame<=8;frame++){c.SampleAnimation(rigs[side],c.length*frame/8);foreach(var renderer in rigs[side].GetComponentsInChildren<SkinnedMeshRenderer>())if(renderer.enabled){var baked=new Mesh();renderer.BakeMesh(baked);foreach(var vertex in baked.vertices){var world=renderer.transform.TransformPoint(vertex);if(float.IsNaN(world.x)||float.IsNaN(world.y)||float.IsNaN(world.z))throw new Exception("Nonfinite baked infantry vertex");bounds.Encapsulate(world);}UnityEngine.Object.DestroyImmediate(baked);}}report.AppendLine($"{rigs[side].name} {c.name} 9-pose baked bounds={bounds}");}
 for(int k=0;k<clips.Length;k++){clips[k].SampleAnimation(rigs[0],clips[k].length*.4f);foreach(var t in rigs[0].GetComponentsInChildren<Transform>())if(t.name=="l_hand"||t.name=="r_hand"||t.name=="head")report.AppendLine($"{clips[k].name} {t.name} world={t.position} rot={t.eulerAngles}");}
 for(int i=0;i<2;i++){clips[0].SampleAnimation(rigs[i],0);var b=new Bounds(rigs[i].transform.position,Vector3.zero);foreach(var r in rigs[i].GetComponentsInChildren<Renderer>())if(r.enabled)b.Encapsulate(r.bounds);report.AppendLine($"{rigs[i].name} idle bounds={b}");rigs[i].transform.position=new Vector3(i==0?-1:1,0,0);}
 var ground=GameObject.CreatePrimitive(PrimitiveType.Plane);ground.transform.localScale=Vector3.one*2;ground.GetComponent<Renderer>().sharedMaterial=MaterialFor("ground",0);
 var sun=new GameObject("Inspection sunlight").AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=2;sun.transform.rotation=Quaternion.Euler(40,-35,0);sun.shadows=LightShadows.Soft;RenderSettings.ambientLight=new Color(.5f,.55f,.65f);
 var cam=new GameObject("Inspection camera").AddComponent<Camera>();cam.transform.position=new Vector3(3.6f,2.7f,-5.3f);cam.transform.LookAt(new Vector3(0,1,0));cam.fieldOfView=37;cam.backgroundColor=new Color(.22f,.3f,.4f);cam.clearFlags=CameraClearFlags.SolidColor;
 var inspect=new GameObject("Inspection").AddComponent<EmpireBattle.Art.InfantryInspection>();inspect.Soldiers=rigs;inspect.Clips=clips;
 Directory.CreateDirectory(Root+"/Prefabs");foreach(var rig in rigs){var position=rig.transform.position;rig.transform.position=Vector3.zero;PrefabUtility.SaveAsPrefabAsset(rig,Root+"/Prefabs/"+rig.name+"Inspection.prefab");rig.transform.position=position;}
 string path="Assets/Scenes/InfantryInspection.unity";UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,path);File.WriteAllText("Logs/infantry-assembly.txt",report.ToString());
 if(!build)return;var result=BuildPipeline.BuildPlayer(new[]{path},"Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Infantry inspection build failed");
 }
 static Material MaterialFor(string key,int side){
 string prefix=key;if(key=="gap_french_infantry_old")prefix="gap_french_oldinfantry";if(key=="newmusket")prefix=side==0?"newmusket_france":"newmusket_british";
 string path=Root+"/Materials/"+prefix+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);bool create=mat==null;if(create)mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));mat.DisableKeyword("_SPECULAR_SETUP");mat.DisableKeyword("_SPECGLOSSMAP");mat.SetFloat("_WorkflowMode",1);mat.SetTexture("_SpecGlossMap",null);mat.name=prefix;
 var diffuse=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+prefix+"_d.png");if(diffuse)mat.SetTexture("_BaseMap",diffuse);
 var normal=AssetDatabase.LoadAssetAtPath<Texture2D>(Root+"/Textures/"+(key=="newmusket"?"newmusket":prefix)+"_n.png");if(normal){mat.SetTexture("_BumpMap",normal);mat.EnableKeyword("_NORMALMAP");}
 mat.SetFloat("_Smoothness",.18f);if(key=="skin")mat.SetColor("_BaseColor",new Color(.65f,.43f,.31f));if(key=="ClosedGloves")mat.SetColor("_BaseColor",new Color(.72f,.65f,.49f));if(key=="Ramrod")mat.SetColor("_BaseColor",new Color(.52f,.55f,.59f));if(key=="CoarseWeapon")mat.SetColor("_BaseColor",new Color(.2f,.12f,.07f));if(key=="ground")mat.SetColor("_BaseColor",new Color(.24f,.29f,.19f));if(create)AssetDatabase.CreateAsset(mat,path);else EditorUtility.SetDirty(mat);return mat;
 }

 public static void BakeFormationPostures(){
  prepared=true;const string output="Assets/Resources/BakedInfantry";
  var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
  library.hideFlags=HideFlags.DontUnloadUnusedAsset;var report=new StringBuilder();
  for(int level=0;level<2;level++){
   BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();
   for(int side=0;side<2;side++){
    var rig=inspection.Soldiers[side];var lod=(side==0?library.French:library.British).Lods[level];
    var rects=new System.Collections.Generic.Dictionary<string,Rect>();foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
    var uv=new Vector2[lod.Fire.Length][];for(int frame=0;frame<uv.Length;frame++)uv[frame]=lod.Fire[frame].uv;
    lod.KneelingFire=BakePoses(rig,inspection.Clips[1],lod.Fire.Length,rects,output,level,report,null,true);
    for(int frame=0;frame<uv.Length;frame++){
     if(lod.KneelingFire[frame].vertexCount!=uv[frame].Length)throw new Exception("Kneeling fire topology changed");
     lod.KneelingFire[frame].uv=uv[frame];EditorUtility.SetDirty(lod.KneelingFire[frame]);
    }
   }
  }
  library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
  File.WriteAllText("Logs/infantry-formation-postures.txt",report.ToString());InfantryPoseBlendBuild.Prepare();
  Debug.Log("FORMATION_POSTURES_BAKED: kneeling aim/fire for both factions and LODs.");
 }
 public static void BakeExpandedRefinements(){
  prepared=true;const string output="Assets/Resources/BakedInfantry";
  var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
  library.hideFlags=HideFlags.DontUnloadUnusedAsset;
  var report=new StringBuilder();
  for(int level=0;level<2;level++){
   BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();
   for(int side=0;side<2;side++){
    var rig=inspection.Soldiers[side];var set=side==0?library.French:library.British;var v=set.Lods[level].Locomotion[2];
    // Geometry-only rebake: preserve the existing atlas coordinates and vertex order.
    var rects=new System.Collections.Generic.Dictionary<string,Rect>();
    foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)foreach(var material in renderer.sharedMaterials)rects[material.name]=new Rect(0,0,1,1);
    for(int sequence=-1;sequence<10;sequence++){
     var old=sequence<0?v.March:set.Lods[level].DeathVariants[sequence].Poses;
     var savedUV=new Vector2[12][];for(int i=0;i<savedUV.Length;i++){var existing=AssetDatabase.LoadAssetAtPath<Mesh>(output+"/"+rig.name+"_L"+level+"_"+(sequence<0?"March_V2":"Death_V"+sequence)+"_"+i+".asset");if(!existing)throw new Exception("Missing source refinement mesh");savedUV[i]=existing.uv;}
     var poses=BakePoses(rig,inspection.Clips[0],12,rects,output,level,report,sequence<0?EmpireBattle.AnimationAuthoring.InfantryMotionKind.March:EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death,false,sequence<0?2:sequence);
     for(int i=0;i<poses.Length;i++){if(poses[i].vertexCount!=savedUV[i].Length)throw new Exception("Refinement topology changed");poses[i].uv=savedUV[i];EditorUtility.SetDirty(poses[i]);}
     if(sequence<0)v.March=poses;else set.Lods[level].DeathVariants[sequence].Poses=poses;
    }
   }
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/infantry-raised-march.txt",report.ToString());
  BuildScene(false,3);var inspect=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();inspect.Library=library;foreach(var rig in inspect.Soldiers)rig.SetActive(false);
  UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/InfantryInspection.unity");
  library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
  FinishInspection();InfantryMotionVerification.VerifyExpandedLibrary();InfantryBattleBuild.BuildMac();
 }

 public static void RefreshBehaviorInspection(){
  prepared=true;var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");library.hideFlags=HideFlags.DontUnloadUnusedAsset;
  BuildScene(false,3);var inspect=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();inspect.Library=library;foreach(var rig in inspect.Soldiers)rig.SetActive(false);
  UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/InfantryInspection.unity");library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
  InfantryPoseBlendBuild.Prepare();var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/InfantryInspection.unity"},"Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);
  if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Behavior inspection refresh failed");Debug.Log("INFANTRY_INSPECTION_REFRESHED");
 }
 public static void RepairFrenchDrumColours(){
  var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
  foreach(var lod in library.French.Lods){
   var flag=lod.Command[2].Idle[0];var points=flag.vertices;var texcoords=flag.uv;Vector2 colour=Vector2.zero;int samples=0;
   for(int n=0;n<points.Length;n++)if(points[n].y>2.15f&&points[n].y<2.8f&&points[n].x<-.12f&&points[n].x>-.30f&&points[n].z>.3f){colour+=texcoords[n];samples++;}
   if(samples==0)throw new Exception("Missing blue flag edge");colour/=samples;
   foreach(var poses in new[]{lod.Command[1].Idle,lod.Command[1].March})foreach(var mesh in poses){
    var vertices=mesh.vertices;var uv=mesh.uv;Vector2 old=Vector2.zero;int most=0;
    var candidates=new System.Collections.Generic.Dictionary<Vector2,int>();
    for(int n=0;n<vertices.Length;n++)if(vertices[n].y>.66f&&vertices[n].y<.75f&&vertices[n].z>.2f&&Mathf.Abs(vertices[n].x)<.22f){
     candidates.TryGetValue(uv[n],out int count);candidates[uv[n]]=++count;if(count>most){most=count;old=uv[n];}
    }
    if(most==0)throw new Exception("Missing drum shell");int changed=0;
    for(int n=0;n<uv.Length;n++)if((uv[n]-old).sqrMagnitude<1e-12f){uv[n]=colour;changed++;}
    if(changed<30)throw new Exception("Drum shell colour repair: "+mesh.name+" candidates="+most+" changed="+changed);mesh.uv=uv;EditorUtility.SetDirty(mesh);
   }
  }
  AssetDatabase.SaveAssets();InfantryPoseBlendBuild.Prepare();
  var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/InfantryInspection.unity"},"Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);
  if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Inspection colour refresh failed");
  InfantryBehaviorVerification.Verify();InfantryBattleBuild.BuildMatchedPlaytests();
 }
 public static void BakeRegimentPresentation(){BakeRegimentPresentation(false);}
 public static void BakePikeSergeants(){BakeRegimentPresentation(true);}
 public static void BakeCombatCommand(){BakeRegimentPresentation(true);}
 static void BakeRegimentPresentation(bool pikesOnly){
  prepared=true;const string output="Assets/Resources/BakedInfantry";var report=new StringBuilder();
  var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
  library.hideFlags=HideFlags.DontUnloadUnusedAsset;
  var atlasMaps=new System.Collections.Generic.Dictionary<string,Rect>[2];
  for(int level=0;level<2;level++){
   BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();
   for(int side=0;side<2;side++){
    var rig=inspection.Soldiers[side];var lod=(side==0?library.French:library.British).Lods[level];
    var atlas=new Texture2D(2,2);atlas.LoadImage(File.ReadAllBytes(output+"/"+rig.name+"Atlas.png"));
    var palette=InfantryCommandAuthoring.Colours(side);
    if(level==0)atlasMaps[side]=RecoverCommandAtlasMaps(rig,atlas,palette);
    var rects=new System.Collections.Generic.Dictionary<string,Rect>(atlasMaps[side]);
    var old=lod.Command[2].Idle[0];var points=old.vertices;var oldUV=old.uv;
    for(int colour=0;colour<palette.Length;colour++){
     var material=palette[colour];float best=float.PositiveInfinity;Vector2 sample=Vector2.zero;
     void Consider(Vector2 uv){Color pixel=atlas.GetPixelBilinear(uv.x,uv.y),target=material.color;
      float error=(new Vector3(pixel.r,pixel.g,pixel.b)-new Vector3(target.r,target.g,target.b)).sqrMagnitude;
      if(error<best){best=error;sample=uv;}}
     if(colour<3){for(int i=0;i<points.Length;i++)if(points[i].y>2.1f&&points[i].x<-.09f)Consider(oldUV[i]);}
     else for(int y=4;y<atlas.height;y+=8)for(int x=4;x<atlas.width;x+=8)Consider(new Vector2((x+.5f)/atlas.width,(y+.5f)/atlas.height));
     if(best>.025f)throw new Exception("Missing atlas command colour: "+material.name+" error="+best);
     rects[material.name]=new Rect(sample.x,sample.y,0,0);
    }
    UnityEngine.Object.DestroyImmediate(atlas);
    var cloth=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.enabled&&r.name.Contains(side==0?"french_infantry":"british_infantry")).sharedMaterial;
    var metal=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.name=="Authored ramrod").sharedMaterial;
    var wood=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod")).sharedMaterial;
    var oldCommand=lod.Command;var updated=new EmpireBattle.Art.InfantryCommandVariant[5];Array.Copy(oldCommand,updated,Mathf.Min(oldCommand.Length,5));
    var kinds=new[]{EmpireBattle.AnimationAuthoring.InfantryMotionKind.Officer,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Drummer,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Standard,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Sergeant,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel};
    foreach(int role in pikesOnly?new[]{0,3}:new[]{0,2,3,4}){
     var result=new EmpireBattle.Art.InfantryCommandVariant();
     for(int sequence=0;sequence<(role==0?10:role==3?9:2);sequence++){
      inspection.Clips[0].SampleAnimation(rig,0);
      var equipment=InfantryCommandAuthoring.Create(rig,kinds[role],cloth,metal,wood,side,sequence==1);
      bool kneel=role==0?sequence>=8:role==3&&sequence>=7;int motionVariant=kneel?(role==0?(sequence==8?0:3):(sequence==7?2:3)):sequence;
      var poses=BakePoses(rig,inspection.Clips[0],12,rects,output,level,report,kinds[role],kneel,motionVariant);
      if(sequence==0)result.Idle=poses;else if(sequence==1)result.March=poses;
      else if(kneel){if((role==0&&sequence==8)||(role==3&&sequence==7))result.KneelingIdle=poses;else result.KneelingStrike=poses;}
      else if(role==0){if(sequence==2)result.OfficerReady=poses;else if(sequence==3)result.OfficerSlash=poses;else if(sequence==4)result.OfficerKnockdown=poses;else if(sequence==5)result.OfficerGetUp=poses;else if(sequence==6)result.OfficerDeath=poses;else result.OfficerFire=poses;}
      else if(sequence==2)result.PikeReady=poses;else if(sequence==3)result.PikeThrust=poses;else if(sequence==4)result.PikeKnockdown=poses;else if(sequence==5)result.PikeGetUp=poses;else result.PikeDeath=poses;
      InfantryCommandAuthoring.Destroy(equipment,rig);
     }
     updated[role]=result;
    }
    lod.Command=updated;SyncCommandRecovery(lod);
   }
  }
  library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
  File.WriteAllText("Logs/regiment-presentation-bake.txt",report.ToString());
  RefreshBehaviorInspection();RegimentPresentationVerification.Verify();InfantryBehaviorVerification.Verify();
 }
 // Recreate the original packing without writing the atlas. Exact pixel agreement guards the mapping.
 static System.Collections.Generic.Dictionary<string,Rect> RecoverCommandAtlasMaps(GameObject rig,Texture2D existing,Material[] palette,int paletteSize=3){
  var names=new System.Collections.Generic.List<string>();var textures=new System.Collections.Generic.List<Texture2D>();
  foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)foreach(var material in renderer.sharedMaterials)if(!names.Contains(material.name)){
   names.Add(material.name);var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);var source=material.GetTexture("_BaseMap");
   if(source)texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));else{texture.SetPixels(new[]{material.color,material.color,material.color,material.color});texture.Apply();}textures.Add(texture);
  }
  for(int i=0;i<paletteSize;i++){var material=palette[i];names.Add(material.name);var texture=new Texture2D(2,2);texture.SetPixels(new[]{material.color,material.color,material.color,material.color});texture.Apply();textures.Add(texture);}
  names.Add("CoarseWeapon");var wood=new Texture2D(2,2);var brown=new Color(.2f,.12f,.07f);wood.SetPixels(new[]{brown,brown,brown,brown});wood.Apply();textures.Add(wood);
  var reconstructed=new Texture2D(4096,4096,TextureFormat.RGBA32,false);var packed=reconstructed.PackTextures(textures.ToArray(),8,4096);
  if(reconstructed.width!=existing.width||reconstructed.height!=existing.height)throw new Exception("Command atlas dimensions changed");
  var pixels=reconstructed.GetPixels32();var original=existing.GetPixels32();int differences=0;
  for(int i=0;i<pixels.Length;i++)if(!pixels[i].Equals(original[i]))differences++;
  if(differences>0){
   foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(reconstructed);
   // Full crowd rebakes include the expanded command palette; retain support for that atlas too.
   if(paletteSize!=palette.Length)return RecoverCommandAtlasMaps(rig,existing,palette,palette.Length);
   throw new Exception("Command atlas reconstruction differs: "+rig.name+" pixels="+differences);
  }
  var result=new System.Collections.Generic.Dictionary<string,Rect>();for(int i=0;i<names.Count;i++)result[names[i]]=packed[i];
  foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);UnityEngine.Object.DestroyImmediate(reconstructed);
  Debug.Log("COMMAND_ATLAS_MAPPING_VERIFIED "+rig.name+" exactPixels="+pixels.Length);return result;
 }
 public static void FinishCommandRefinements(){BakeRegimentPresentation();InfantryBattleBuild.BuildMatchedPlaytests();}

 public static void FinishBehaviorRefinements(){
  prepared=true;const string output="Assets/Resources/BakedInfantry";
  var library=Resources.Load<EmpireBattle.Art.InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");library.hideFlags=HideFlags.DontUnloadUnusedAsset;library.IdleDuration=6.4f;
  var report=new StringBuilder();
  for(int level=0;level<2;level++){
   BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();
   for(int side=0;side<2;side++){
    var rig=inspection.Soldiers[side];var lod=(side==0?library.French:library.British).Lods[level];
    var rects=new System.Collections.Generic.Dictionary<string,Rect>();foreach(var r in rig.GetComponentsInChildren<Renderer>())if(r.enabled)foreach(var m in r.sharedMaterials)rects[m.name]=new Rect(0,0,1,1);
    for(int sequence=0;sequence<2;sequence++)for(int variant=0;variant<3;variant++){
     var kind=sequence==0?EmpireBattle.AnimationAuthoring.InfantryMotionKind.Idle:EmpireBattle.AnimationAuthoring.InfantryMotionKind.UnderFire;
     var target=sequence==0?lod.IdleVariants[variant]:lod.UnderFireVariants[variant];var uv=new Vector2[12][];for(int frame=0;frame<12;frame++)uv[frame]=target.Poses[frame].uv;
     target.Poses=BakePoses(rig,inspection.Clips[0],12,rects,output,level,report,kind,false,variant);
     for(int frame=0;frame<12;frame++){if(target.Poses[frame].vertexCount!=uv[frame].Length)throw new Exception("Idle refinement topology changed");target.Poses[frame].uv=uv[frame];EditorUtility.SetDirty(target.Poses[frame]);}
    }
   }
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/infantry-idle-refinements.txt",report.ToString());
  BuildScene(false,3);var inspect=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();inspect.Library=library;foreach(var rig in inspect.Soldiers)rig.SetActive(false);
  UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/InfantryInspection.unity");library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();
  FinishInspection();InfantryBehaviorVerification.Verify();InfantryBattleBuild.BuildMatchedPlaytests();
 }
 public static void BakeAndVerifyBehavior(){BakeCrowd();InfantryBehaviorVerification.Verify();InfantryBattleBuild.BuildMac();}
 public static void BakeCrowd(){VerifyReload();
  const string output="Assets/Resources/BakedInfantry";Directory.CreateDirectory(output);
  var library=AssetDatabase.LoadAssetAtPath<EmpireBattle.Art.InfantryCrowdLibrary>(output+"/InfantryCrowdLibrary.asset");if(!library){library=ScriptableObject.CreateInstance<EmpireBattle.Art.InfantryCrowdLibrary>();AssetDatabase.CreateAsset(library,output+"/InfantryCrowdLibrary.asset");}
  library.hideFlags=HideFlags.DontUnloadUnusedAsset;
  library.French=new EmpireBattle.Art.InfantryCrowdSet{Lods=new EmpireBattle.Art.InfantryCrowdLod[2]};library.British=new EmpireBattle.Art.InfantryCrowdSet{Lods=new EmpireBattle.Art.InfantryCrowdLod[2]};
  var maps=new System.Collections.Generic.Dictionary<string,Rect>[2];var report=new StringBuilder();
  for(int level=0;level<2;level++){
   BuildScene(false,level==0?3:5);var inspection=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();
   for(int side=0;side<2;side++){
    var rig=inspection.Soldiers[side];var set=side==0?library.French:library.British;
    if(level==0){maps[side]=new System.Collections.Generic.Dictionary<string,Rect>();var names=new System.Collections.Generic.List<string>();var textures=new System.Collections.Generic.List<Texture2D>();
     foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled)foreach(var mat in renderer.sharedMaterials)if(!names.Contains(mat.name)){
      names.Add(mat.name);Texture2D texture=new Texture2D(2,2,TextureFormat.RGBA32,false);var source=mat.GetTexture("_BaseMap");if(source){texture.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(source)));}else{texture.SetPixels(new[]{mat.color,mat.color,mat.color,mat.color});texture.Apply();}textures.Add(texture);
     }
     foreach(var mat in InfantryCommandAuthoring.Colours(side)){
      names.Add(mat.name);var texture=new Texture2D(2,2);texture.SetPixels(new[]{mat.color,mat.color,mat.color,mat.color});texture.Apply();textures.Add(texture);
     }
     names.Add("CoarseWeapon");var wood=new Texture2D(2,2);wood.SetPixels(new[]{new Color(.2f,.12f,.07f),new Color(.2f,.12f,.07f),new Color(.2f,.12f,.07f),new Color(.2f,.12f,.07f)});wood.Apply();textures.Add(wood);
     var atlas=new Texture2D(4096,4096,TextureFormat.RGBA32,false);var rects=atlas.PackTextures(textures.ToArray(),8,4096);for(int i=0;i<names.Count;i++)maps[side][names[i]]=rects[i];string atlasPath=output+"/"+rig.name+"Atlas.png";File.WriteAllBytes(atlasPath,atlas.EncodeToPNG());AssetDatabase.ImportAsset(atlasPath);var importer=(TextureImporter)AssetImporter.GetAtPath(atlasPath);importer.maxTextureSize=4096;importer.mipmapEnabled=true;importer.textureCompression=TextureImporterCompression.Compressed;importer.SaveAndReimport();
     string materialPath=output+"/"+rig.name+"Crowd.mat";var material=AssetDatabase.LoadAssetAtPath<Material>(materialPath);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,materialPath);}material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPath));material.SetFloat("_Smoothness",.12f);material.enableInstancing=true;EditorUtility.SetDirty(material);set.Material=material;
    }
    var data=new EmpireBattle.Art.InfantryCrowdLod{MaxDistance=level==0?40f:100000f};set.Lods[level]=data;data.Idle=BakePoses(rig,inspection.Clips[0],4,maps[side],output,level,report);data.Fire=BakePoses(rig,inspection.Clips[1],8,maps[side],output,level,report);data.Reload=BakePoses(rig,inspection.Clips[2],24,maps[side],output,level,report);data.KneelingReload=BakePoses(rig,inspection.Clips[2],24,maps[side],output,level,report,null,true);data.March=BakePoses(rig,inspection.Clips[0],8,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.March);data.Run=BakePoses(rig,inspection.Clips[0],8,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run);data.Charge=BakePoses(rig,inspection.Clips[0],8,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Charge);data.Hit=BakePoses(rig,inspection.Clips[0],4,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Hit);data.Death=BakePoses(rig,inspection.Clips[0],8,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death);
    data.IdleVariants=new EmpireBattle.Art.InfantryDeathVariant[3];data.UnderFireVariants=new EmpireBattle.Art.InfantryDeathVariant[3];
    for(int variant=0;variant<3;variant++){
     data.IdleVariants[variant]=new EmpireBattle.Art.InfantryDeathVariant{Poses=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Idle,false,variant)};
     data.UnderFireVariants[variant]=new EmpireBattle.Art.InfantryDeathVariant{Poses=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.UnderFire,false,variant)};
    }
    data.Command=new EmpireBattle.Art.InfantryCommandVariant[5];
    var commandKinds=new[]{EmpireBattle.AnimationAuthoring.InfantryMotionKind.Officer,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Drummer,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Standard,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Sergeant,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel};
    for(int role=0;role<commandKinds.Length;role++){
     var cloth=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.enabled&&r.name.Contains(side==0?"french_infantry":"british_infantry")).sharedMaterial;
     var metal=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.name=="Authored ramrod").sharedMaterial;
     var wood=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod")).sharedMaterial;
     var equipment=InfantryCommandAuthoring.Create(rig,commandKinds[role],cloth,metal,wood,side);
     data.Command[role]=new EmpireBattle.Art.InfantryCommandVariant{
      Idle=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,commandKinds[role],false,0),
      March=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,commandKinds[role],false,1)};
     InfantryCommandAuthoring.Destroy(equipment,rig);
     if(role==0||role==3)for(int sequence=2;sequence<(role==0?10:9);sequence++){
      inspection.Clips[0].SampleAnimation(rig,0);
      equipment=InfantryCommandAuthoring.Create(rig,commandKinds[role],cloth,metal,wood,side);
      bool kneel=role==0?sequence>=8:sequence>=7;int motionVariant=kneel?(role==0?(sequence==8?0:3):(sequence==7?2:3)):sequence;
      var poses=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,commandKinds[role],kneel,motionVariant);
      if(kneel){if((role==0&&sequence==8)||(role==3&&sequence==7))data.Command[role].KneelingIdle=poses;else data.Command[role].KneelingStrike=poses;}
      else if(role==0){if(sequence==2)data.Command[role].OfficerReady=poses;else if(sequence==3)data.Command[role].OfficerSlash=poses;else if(sequence==4)data.Command[role].OfficerKnockdown=poses;else if(sequence==5)data.Command[role].OfficerGetUp=poses;else if(sequence==6)data.Command[role].OfficerDeath=poses;else data.Command[role].OfficerFire=poses;}
      else if(sequence==2)data.Command[role].PikeReady=poses;else if(sequence==3)data.Command[role].PikeThrust=poses;else if(sequence==4)data.Command[role].PikeKnockdown=poses;else if(sequence==5)data.Command[role].PikeGetUp=poses;else data.Command[role].PikeDeath=poses;
      InfantryCommandAuthoring.Destroy(equipment,rig);
     }
    }
    data.DeathVariants=new EmpireBattle.Art.InfantryDeathVariant[10];
    for(int variant=0;variant<10;variant++)data.DeathVariants[variant]=new EmpireBattle.Art.InfantryDeathVariant{Poses=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death,false,variant)};
    data.Locomotion=new EmpireBattle.Art.InfantryLocomotionVariant[3];
    for(int variant=0;variant<3;variant++){
     var v=new EmpireBattle.Art.InfantryLocomotionVariant();data.Locomotion[variant]=v;
     v.Walk=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Walk,false,variant);
     v.March=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.March,false,variant);
     v.Jog=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Jog,false,variant);
     v.Run=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Run,false,variant);
     v.Charge=BakePoses(rig,inspection.Clips[0],12,maps[side],output,level,report,EmpireBattle.AnimationAuthoring.InfantryMotionKind.Charge,false,variant);
    }

   }
  }
  EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/infantry-crowd-bake.txt",report.ToString());
  BuildScene(false,3);var inspect=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>();inspect.Library=library;library.hideFlags=HideFlags.None;EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();foreach(var rig in inspect.Soldiers)rig.SetActive(false);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Scenes/InfantryInspection.unity");
  UpdateMuzzleAnchors();InfantryPoseBlendBuild.Prepare();
  var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/InfantryInspection.unity"},"Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Baked infantry inspection build failed");
 }
 public static void VerifyReload(){
  var report=new StringBuilder();AnimationClip idle=null;foreach(var a in AssetDatabase.LoadAllAssetsAtPath(Root+"/Animations/musket_idle.FBX"))if(a is AnimationClip c&&!c.name.StartsWith("__preview"))idle=c;
  for(int side=0;side<2;side++){string faction=side==0?"French":"British";var rig=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+faction+"Inspection.prefab"));PrefabUtility.UnpackPrefabInstance(rig,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);rig.transform.position=Vector3.zero;idle.SampleAnimation(rig,0);var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod")).transform;var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");gun.SetParent(rig.transform,true);var grips=new EmpireBattle.Art.ClosedGripAuthoring(rig,gun,rod);grips.Apply(false,false,0);var reload=new EmpireBattle.Art.MusketReloadAuthoring(rig,gun,rod);var bones=new System.Collections.Generic.Dictionary<string,Transform>();foreach(var t in rig.GetComponentsInChildren<Transform>())if(!bones.ContainsKey(t.name))bones[t.name]=t;
   Vector3 lastLeft=Vector3.zero,lastRight=Vector3.zero;float minClear=100,minSide=100,maxStep=0,maxWrist=0,maxContact=0,worst=0,maxBore=0;
   for(int frame=0;frame<=96;frame++){float phase=frame/96f;reload.Sample(phase);grips.Apply(false,true,phase);if(!reload.Validate(phase,out string error))throw new Exception(error);float clear=100,lateral=100,step=0;
    foreach(string arm in new[]{"l","r"}){var shoulder=bones[arm+"_upperarm_twist"].position;var elbow=bones[arm+"_foretwist"].position;var hand=bones[arm+"_hand"].position;if(side==0&&(frame==0||frame==15||frame==34||frame==48||frame==72))report.AppendLine($"BONES t={phase:F4} {arm} shoulder={rig.transform.InverseTransformPoint(shoulder):F3} elbow={rig.transform.InverseTransformPoint(elbow):F3} wrist={rig.transform.InverseTransformPoint(hand):F3}");clear=Mathf.Min(clear,EmpireBattle.Art.AnatomicalArmIK.Clearance(rig.transform,shoulder,elbow,hand));lateral=Mathf.Min(lateral,(arm=="l"?1:-1)*rig.transform.InverseTransformPoint(elbow).x);if(frame>0){step=Mathf.Max(step,Vector3.Distance(elbow,arm=="l"?lastLeft:lastRight));maxStep=Mathf.Max(maxStep,step);}if(arm=="l")lastLeft=elbow;else lastRight=elbow;}
    if(clear<minClear){minClear=clear;worst=phase;}minSide=Mathf.Min(minSide,lateral);maxWrist=Mathf.Max(maxWrist,grips.MaxWristAngle);maxContact=Mathf.Max(maxContact,grips.MaxContactError);float bore=phase>=.48f&&phase<=.75f?new Vector2(reload.MuzzlePosition.x-rig.transform.InverseTransformPoint(rod.position).x,reload.MuzzlePosition.z-rig.transform.InverseTransformPoint(rod.position).z).magnitude:0;maxBore=Mathf.Max(maxBore,bore);report.AppendLine($"{faction} t={phase:F4} torsoRadius={clear:F4} elbowLateral={lateral:F4} contact={grips.MaxContactError:F4} wrist={grips.MaxWristAngle:F1} elbowStep={step:F4} boreError={bore:F5}");
   }report.AppendLine($"SUMMARY {faction} minTorsoRadius={minClear:F4} minLateral={minSide:F4} maxElbowStep={maxStep:F4} maxContact={maxContact:F4} maxWrist={maxWrist:F1} maxBore={maxBore:F5} worstPhase={worst:F4}");File.WriteAllText("Logs/infantry-reload-anatomy.txt",report.ToString());if(minClear<1.01f||minSide<.1f||maxStep>.08f||maxContact>.005f||maxWrist>60||maxBore>.002f)throw new Exception($"Reload anatomy gate failed: clear={minClear} side={minSide} step={maxStep} contact={maxContact} wrist={maxWrist} bore={maxBore}");UnityEngine.Object.DestroyImmediate(rig);
  }File.WriteAllText("Logs/infantry-reload-anatomy.txt",report.ToString());
 }
 public static void FinishInspection(){UpdateMuzzleAnchors();InfantryPoseBlendBuild.Prepare();var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/InfantryInspection.unity"},"Builds/Infantry Inspection.app",BuildTarget.StandaloneOSX,BuildOptions.None);if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("Inspection build failed");}
 public static void UpdateMuzzleAnchors(){
  var library=AssetDatabase.LoadAssetAtPath<EmpireBattle.Art.InfantryCrowdLibrary>("Assets/Resources/BakedInfantry/InfantryCrowdLibrary.asset");
  AnimationClip clip=null;foreach(var a in AssetDatabase.LoadAllAssetsAtPath(Root+"/Animations/gp_musket_aimfire.FBX"))if(a is AnimationClip c&&!c.name.StartsWith("__preview"))clip=c;
  if(!clip)throw new Exception("Fire clip missing");var report=new StringBuilder();
  for(int side=0;side<2;side++){string faction=side==0?"French":"British";var set=side==0?library.French:library.British;Vector3 muzzle=Vector3.zero,stock=Vector3.zero;
   for(int sample=3;sample<=4;sample++){
    var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/Prefabs/"+faction+"Inspection.prefab");var rig=(GameObject)PrefabUtility.InstantiatePrefab(prefab);PrefabUtility.UnpackPrefabInstance(rig,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);rig.transform.position=Vector3.zero;
    clip.SampleAnimation(rig,clip.length*sample/7f);var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name=="newmusket.lod3").transform;var rod=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="Authored ramrod");gun.SetParent(rig.transform,true);new EmpireBattle.Art.ClosedGripAuthoring(rig,gun,rod).Apply(true,false,sample/7f);
    report.AppendLine($"ANCHOR {faction} frame{sample} rootrot={rig.transform.eulerAngles} gunrot={gun.eulerAngles} gunforward={gun.forward} gunscale={gun.lossyScale} parent={gun.parent.name}");var turn=Quaternion.Euler(0,180,0);var mesh=gun.GetComponent<MeshFilter>().sharedMesh;var bounds=mesh.bounds;Vector3 muzzleSum=Vector3.zero;int muzzlePoints=0;foreach(var vertex in mesh.vertices)if(vertex.z>bounds.max.z-.35f){muzzleSum+=vertex;muzzlePoints++;}Vector3 muzzleLocal=new Vector3(muzzleSum.x/muzzlePoints,muzzleSum.y/muzzlePoints,bounds.max.z);muzzle+=turn*rig.transform.InverseTransformPoint(gun.TransformPoint(muzzleLocal))*.5f;stock+=turn*rig.transform.InverseTransformPoint(gun.TransformPoint(new Vector3(.7f,.4f,-22f)))*.5f;UnityEngine.Object.DestroyImmediate(rig);
   }set.MuzzleOffset=muzzle;set.LockOffset=stock;report.AppendLine($"{faction}: phase .5 muzzle={muzzle:F4} lock={stock:F4}");
  }EditorUtility.SetDirty(library);AssetDatabase.SaveAssets();File.WriteAllText("Logs/infantry-fire-anchors.txt",report.ToString());
 }
 static void ValidateReloadInterpolation(Transform root,Vector3[][] joints,int lod,StringBuilder report,float chestDrop=0){float minimum=100,lateral=100,worst=0;for(int frame=0;frame<joints.Length-1;frame++)for(int fraction=0;fraction<=5;fraction++){float blend=fraction/5f;for(int arm=0;arm<2;arm++){int start=arm*3;Vector3 shoulder=Vector3.Lerp(joints[frame][start],joints[frame+1][start],blend),elbow=Vector3.Lerp(joints[frame][start+1],joints[frame+1][start+1],blend),hand=Vector3.Lerp(joints[frame][start+2],joints[frame+1][start+2],blend);float clear=EmpireBattle.Art.AnatomicalArmIK.Clearance(root,root.TransformPoint(shoulder),root.TransformPoint(elbow),root.TransformPoint(hand),chestDrop);if(clear<minimum){minimum=clear;worst=(frame+blend)/(joints.Length-1);}lateral=Mathf.Min(lateral,(arm==0?1:-1)*elbow.x);}}report.AppendLine($"RELOAD_GPU_INTERPOLATION {root.name} L{lod}: samples={joints.Length} pairs={joints.Length-1} fractions=6 minTorsoRadius={minimum:F4} minLateral={lateral:F4} worstPhase={worst:F4}");File.WriteAllText("Logs/infantry-crowd-bake.txt",report.ToString());if(minimum<1.01f||lateral<.1f)throw new Exception($"GPU interpolated reload crosses torso/side: {root.name} L{lod} clearance={minimum} side={lateral} phase={worst}");}
 static Mesh[] BakePoses(GameObject rig,AnimationClip clip,int count,System.Collections.Generic.Dictionary<string,Rect> rects,string output,int lod,StringBuilder report,EmpireBattle.AnimationAuthoring.InfantryMotionKind? kind=null,bool kneeling=false,int variant=-1){
  bool pike=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Sergeant;
  bool captain=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Officer;
  EmpireBattle.AnimationAuthoring.ProceduralInfantryRig motion=null;if(kind.HasValue){clip.SampleAnimation(rig,0);
  if((pike||captain)&&variant>=4&&variant<=6){var equipment=rig.transform.Find("Command equipment");var pelvis=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name=="pelvis");equipment.SetParent(pelvis,true);}
  motion=new EmpireBattle.AnimationAuthoring.ProceduralInfantryRig(rig);}
  var transforms=rig.GetComponentsInChildren<Transform>();var gun=Array.Find(rig.GetComponentsInChildren<MeshRenderer>(),r=>r.enabled&&r.name.StartsWith("newmusket.lod"));var rod=Array.Find(transforms,t=>t.name=="Authored ramrod");Transform gunParent=gun.transform.parent;Vector3 gunLocalScale=gun.transform.localScale;Vector3 gunPosition=gun.transform.localPosition;Quaternion gunRotation=gun.transform.localRotation;var grips=new EmpireBattle.Art.ClosedGripAuthoring(rig,gun.transform,rod,kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel?-.65f:kneeling?.43f:0);EmpireBattle.Art.MusketReloadAuthoring reload=null;if(clip.name.Contains("reload")&&!kind.HasValue){var idle=AssetDatabase.LoadAllAssetsAtPath(Root+"/Animations/"+(cartridgeBake?"gp_musket_aimfire":"musket_idle")+".FBX");foreach(var asset in idle)if(asset is AnimationClip c&&!c.name.StartsWith("__preview"))c.SampleAnimation(rig,cartridgeBake?c.length*.4f:0);gun.transform.SetParent(rig.transform,true);grips.Apply(cartridgeBake,false,.4f);if(kneeling){var legs=new EmpireBattle.AnimationAuthoring.ProceduralInfantryRig(rig);legs.Kneel();gun.transform.position+=Vector3.down*.43f;EmpireBattle.Art.MusketReloadAuthoring.StowRod(gun.transform,rod);}reload=new EmpireBattle.Art.MusketReloadAuthoring(rig,gun.transform,rod,kneeling,cartridge:cartridgeBake);}

  string poseName=kind.HasValue?(kneeling?"Kneeling":"")+kind.Value: kneeling?(clip.name.Contains("aimfire")?"KneelingFire":"KneelingReload"):clip.name;if(supplementalPoseName!=null)poseName=supplementalPoseName;if(variant>=0)poseName+="_V"+variant;var poses=new Mesh[count];var looseGear=new System.Collections.Generic.Dictionary<Renderer,LooseDeathGear>();var reloadJoints=reload!=null?new Vector3[count][]:null;for(int frame=0;frame<count;frame++){
   bool melee=kind.HasValue&&kind.Value>=EmpireBattle.AnimationAuthoring.InfantryMotionKind.MeleeReady;
   bool recovery=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.GetUp||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Knockdown||(pike||captain)&&variant>=4&&variant<=6;
   if(kind.HasValue){motion.Sample(kind.Value,bayonetOrderBake?BayonetOrderMotionPhase(kind.Value,frame/(float)(count-1)):(melee||recovery||(pike||captain)&&variant==3||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Hit)?frame/(float)(count-1):frame/(float)count,Mathf.Max(0,variant));
    bool fall=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Knockdown||(pike||captain)&&(variant==4||variant==6);
    bool rise=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.GetUp||(pike||captain)&&variant==5;
    float contactPhase=frame/(float)(count-1);
    if(fall||rise)motion.SettleGrounded(rise?1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.15f,contactPhase)):Mathf.SmoothStep(0,1,Mathf.InverseLerp(.7f,1f,contactPhase)));
    if(!motion.ValidatePose(out string error))throw new Exception(error);}else if(reload!=null){float phase=frame/(float)(count-1);reload.Sample(phase);if(!reload.Validate(phase,out string error))throw new Exception(error);report.AppendLine($"RELOAD {rig.name} L{lod} t={phase:F3} butt={reload.ButtPosition} muzzle={reload.MuzzlePosition} rightError={reload.RightHandError:F3} leftError={reload.LeftHandError:F3}");}else clip.SampleAnimation(rig,clip.length*frame/(clip.name.Contains("aimfire")?(count-1):count));
   if(kneeling&&reload==null)new EmpireBattle.AnimationAuthoring.ProceduralInfantryRig(rig).Kneel();
   Transform poseGunParent=gun.transform.parent;Vector3 poseGunPosition=gun.transform.localPosition,poseGunScale=gun.transform.localScale;Quaternion poseGunRotation=gun.transform.localRotation;gun.transform.SetParent(rig.transform,true);bool command=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Officer||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Drummer||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Standard||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Sergeant||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel;
   if(command){if(kneeling)rig.transform.Find("Command equipment").localPosition=Vector3.down*.43f;foreach(var horse in rig.GetComponentsInChildren<CommandHorseMarker>())horse.Moving=variant==1;if(pike&&variant>=2&&variant<4){
    float phase=frame/(float)(count-1);var equipment=rig.transform.Find("Command equipment");
    InfantryCommandAuthoring.PikePose(equipment,phase,variant==3);
    grips.ApplyPike(equipment,phase,variant==3);
   }else if(captain&&(variant>=2||kneeling)&&!recovery){
    float phase=frame/(float)(count-1);grips.ApplyCaptain(phase,variant==3,variant==7);
    if(variant==7)InfantryCommandAuthoring.OfficerFirePose(rig.transform.Find("Command equipment"),phase);
   }else if(!recovery)grips.ApplyCommand(kind.Value,frame/(float)count);if(!recovery)InfantryCommandAuthoring.Sample(rig,frame/(float)(captain&&variant==3?count-1:count),captain?variant:0);
    else{
     float phase=frame/(float)(count-1);float flat=variant==5?1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.55f,phase)):Mathf.SmoothStep(0,1,phase);
     foreach(var marker in rig.GetComponentsInChildren<CommandEquipmentMarker>()){
      Vector3 axis=marker.name=="Sabre"?marker.transform.up:marker.transform.forward;
      Vector3 tangent=Vector3.ProjectOnPlane(axis,rig.transform.up);if(tangent.sqrMagnitude<.001f)tangent=rig.transform.forward;
      marker.transform.rotation=Quaternion.Slerp(marker.transform.rotation,Quaternion.FromToRotation(axis,tangent.normalized)*marker.transform.rotation,flat);
     }
    }
    gun.enabled=false;rod.GetComponent<Renderer>().enabled=false;}
   // Sword cuts solve from their own sampled stance. Rifle IK history must not
   // influence a sword endpoint or produce a different guard after each cut.
   else if(rosterSword&&melee){grips=new EmpireBattle.Art.ClosedGripAuthoring(rig,gun.transform,rod);}
   else if(melee){var action=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Thrust?EmpireBattle.MeleeAction.Thrust:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Parry?EmpireBattle.MeleeAction.Parry:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.ButtStrike?EmpireBattle.MeleeAction.ButtStrike:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Stagger?EmpireBattle.MeleeAction.Stagger:EmpireBattle.MeleeAction.Ready;grips.ApplyMelee(action,frame/(float)(count-1));if(grips.MaxContactError>.025f)throw new Exception("Melee grip disconnected: "+poseName+" "+grips.MaxContactError);}
   else if(recovery||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death){}
   else if(variant>=0&&kind!=EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death)grips.ApplyLocomotion(kind.Value,frame/(float)count,variant);else if(!(cartridgeBake&&reload!=null))grips.Apply(clip.name.Contains("aimfire")&&!kind.HasValue,reload!=null,frame/(float)(count-1),kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Hit||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death);if(!command&&variant>=0&&grips.MaxContactError>.015f)throw new Exception($"Locomotion grip lost contact: {poseName} {frame} {grips.MaxContactError}");if(reload!=null&&!reload.Validate(frame/(float)(count-1),out string reloadError))throw new Exception(reloadError);if(kneeling&&grips.MaxContactError>.02f)throw new Exception("Kneeling reload hand contact exceeds 2cm");report.AppendLine($"GRIP {rig.name} {poseName} frame{frame}: contactError={grips.MaxContactError:F3}m wristAngle={grips.MaxWristAngle:F1}deg");
   if(bayonetOrderBake)BayonetOrderPose(rig,gun.transform,rod,frame/(float)(count-1));
   if(rosterSword){var sword=Array.Find(transforms,t=>t.name=="Roster sword");var action=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Thrust?EmpireBattle.MeleeAction.SlashRight:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.ButtStrike?EmpireBattle.MeleeAction.SlashLeft:kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Parry?EmpireBattle.MeleeAction.Parry:EmpireBattle.MeleeAction.Ready;if(recovery||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Hit){
    grips.FollowSword(sword);
    if(recovery||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death){
     float phase=frame/(float)(count-1);float flat=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.GetUp?1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.55f,phase)):Mathf.SmoothStep(0,1,phase);
     Vector3 tangent=Vector3.ProjectOnPlane(sword.up,rig.transform.up);if(tangent.sqrMagnitude<.001f)tangent=rig.transform.forward;
     sword.rotation=Quaternion.Slerp(sword.rotation,Quaternion.FromToRotation(sword.up,tangent.normalized)*sword.rotation,flat);
    }
   }else if(melee)grips.ApplySword(action,frame/(float)(count-1),sword);
   else grips.ApplySwordCarry(kind??EmpireBattle.AnimationAuthoring.InfantryMotionKind.Idle,frame/(float)count,sword,swordGuardBake);
   gun.enabled=false;rod.GetComponent<Renderer>().enabled=false;}
   supplementalPose?.Invoke(rig,frame/(float)(count-1));
   if(supplementalPose==null&&!rosterSword&&!melee&&!recovery&&kind!=EmpireBattle.AnimationAuthoring.InfantryMotionKind.Hit&&kind!=EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death){var anatomical=new System.Collections.Generic.Dictionary<string,Transform>();foreach(var t in rig.GetComponentsInChildren<Transform>())if(!anatomical.ContainsKey(t.name))anatomical[t.name]=t;float clear=100,lateral=100;foreach(string arm in new[]{"l","r"}){var shoulder=anatomical[arm+"_upperarm_twist"].position;var elbow=anatomical[arm+"_foretwist"].position;var hand=anatomical[arm+"_hand"].position;clear=Mathf.Min(clear,EmpireBattle.Art.AnatomicalArmIK.Clearance(rig.transform,shoulder,elbow,hand,kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Colonel?-.65f:kneeling?.43f:0));lateral=Mathf.Min(lateral,(arm=="l"?1:-1)*rig.transform.InverseTransformPoint(elbow).x);}if(reloadJoints!=null)reloadJoints[frame]=new[]{rig.transform.InverseTransformPoint(anatomical["l_upperarm_twist"].position),rig.transform.InverseTransformPoint(anatomical["l_foretwist"].position),rig.transform.InverseTransformPoint(anatomical["l_hand"].position),rig.transform.InverseTransformPoint(anatomical["r_upperarm_twist"].position),rig.transform.InverseTransformPoint(anatomical["r_foretwist"].position),rig.transform.InverseTransformPoint(anatomical["r_hand"].position)};report.AppendLine($"ANATOMY {rig.name} {poseName} frame{frame}: torsoRadius={clear:F4} elbowLateral={lateral:F4}");if(clear<1||lateral<.07f)throw new Exception($"Infantry arm intersects torso: {poseName} frame{frame} clearance={clear} lateral={lateral}");}
   if(!command&&recovery){
    float phase=frame/(float)(count-1);
    float grounded=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Knockdown?Mathf.SmoothStep(0,1,phase):1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.55f,phase));
    var hand=Array.Find(transforms,t=>t.name=="r_hand");
    Vector3 direction=Vector3.ProjectOnPlane(gun.transform.forward,rig.transform.up);
    if(direction.sqrMagnitude<.001f)direction=-rig.transform.forward;
    gun.transform.rotation=Quaternion.Slerp(gun.transform.rotation,Quaternion.LookRotation(direction.normalized,rig.transform.up),grounded);
    gun.transform.position=hand.position-gun.transform.TransformVector(new Vector3(0,0,-25));
   }
   if(reload==null)EmpireBattle.Art.MusketReloadAuthoring.StowRod(gun.transform,rod);
   if(cartridgeBake)rod.GetComponent<Renderer>().enabled=false;
   var palmVertices=new System.Collections.Generic.Dictionary<string,System.Collections.Generic.List<int>>();var groundProps=new System.Collections.Generic.List<System.Collections.Generic.List<int>>();var groundSupport=new System.Collections.Generic.List<int>();var contactGroups=new System.Collections.Generic.Dictionary<string,float>();var bodyVertices=new System.Collections.Generic.List<int>();var vertices=new System.Collections.Generic.List<Vector3>();var normals=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var triangles=new System.Collections.Generic.List<int>();
   foreach(var renderer in rig.GetComponentsInChildren<Renderer>())if(renderer.enabled){
    var skin=renderer as SkinnedMeshRenderer;Mesh mesh=skin?new Mesh():renderer.GetComponent<MeshFilter>().sharedMesh;if(skin)skin.BakeMesh(mesh,true);
    var matrix=Matrix4x4.Rotate(Quaternion.Euler(0,180,0))*rig.transform.worldToLocalMatrix*renderer.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;var points=mesh.vertices;var sourceNormals=mesh.normals;var sourceUV=mesh.uv;if(frame==0){var component=new Bounds(matrix.MultiplyPoint3x4(points[0]),Vector3.zero);foreach(var point in points)component.Encapsulate(matrix.MultiplyPoint3x4(point));report.AppendLine($"COMPONENT {rig.name} {poseName} {renderer.name}: determinant={matrix.determinant}, baked-local={mesh.bounds}, root-bounds={component}");}
    if(skin&&frame==count-1&&(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Knockdown||(pike||captain)&&(variant==4||variant==6))){
     var weights=skin.sharedMesh.boneWeights;
     for(int v=0;v<points.Length&&v<weights.Length;v++){
      var w=weights[v];int bone=w.boneIndex0;float weight=w.weight0;
      if(w.weight1>weight){bone=w.boneIndex1;weight=w.weight1;}if(w.weight2>weight){bone=w.boneIndex2;weight=w.weight2;}if(w.weight3>weight){bone=w.boneIndex3;weight=w.weight3;}
      string name=skin.bones[bone].name;
      if(weight<.4f)continue;float y=matrix.MultiplyPoint3x4(points[v]).y;
      if(!contactGroups.TryGetValue(name,out float low)||y<low)contactGroups[name]=y;
     }
    }
    bool detached=false;
    if(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death&&variant>=0&&frame>=2){
     string part=renderer.name.ToLowerInvariant();bool hat=part.Contains("shako")||part.Contains("stovepipe");bool weapon=part.StartsWith("newmusket")||part=="authored ramrod";
     if(hat&&variant%3!=0 || weapon){
      if(!looseGear.TryGetValue(renderer,out var gear)){gear=new LooseDeathGear(points,sourceNormals,matrix,variant,hat,part=="authored ramrod");looseGear.Add(renderer,gear);}
      detached=true;gear.Sample((frame-2)/(float)(count-1)*1.6f,out points,out sourceNormals);matrix=Matrix4x4.identity;normalMatrix=Matrix4x4.identity;
     }
    }
    var propVertices=new System.Collections.Generic.List<int>();
    bool restingProp=!skin&&!detached&&(renderer.name.StartsWith("newmusket")||renderer.name=="Authored ramrod"||renderer.name=="Roster sword"||renderer.GetComponentInParent<CommandEquipmentMarker>()||renderer.name.Contains("pike shaft")||renderer.name.StartsWith("Pike "));
    for(int sub=0;sub<mesh.subMeshCount;sub++){
     var material=renderer.sharedMaterials[Mathf.Min(sub,renderer.sharedMaterials.Length-1)];var rect=rects[material.name];var indices=mesh.GetTriangles(sub);var remap=new System.Collections.Generic.Dictionary<int,int>();foreach(var index in indices){if(remap.TryGetValue(index,out int mapped)){triangles.Add(mapped);continue;}int destination=vertices.Count;remap[index]=destination;var point=matrix.MultiplyPoint3x4(points[index]);if(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsNaN(point.z))throw new Exception("Nonfinite crowd vertex");if(!detached){bodyVertices.Add(destination);if(skin)groundSupport.Add(destination);}if(restingProp)propVertices.Add(destination);
     vertices.Add(point);normals.Add(sourceNormals.Length==points.Length?normalMatrix.MultiplyVector(sourceNormals[index]).normalized:Vector3.up);var texcoord=sourceUV.Length==points.Length?sourceUV[index]:Vector2.one*.5f;uv.Add(new Vector2(rect.x+Mathf.Clamp01(texcoord.x)*rect.width,rect.y+Mathf.Clamp01(texcoord.y)*rect.height));triangles.Add(destination);}
    }
    if(propVertices.Count>0)groundProps.Add(propVertices);
    if(renderer.name=="Closed left hand"||renderer.name=="Closed right hand"){
     string palm=renderer.name=="Closed left hand"?"l_palm":"r_palm";
     var indices=new System.Collections.Generic.List<int>();
     // Rigid hands are appended contiguously, unlike the skinned sleeve/cuff weights.
     int first=vertices.Count;
     // Count unique vertices per submesh, matching the combined topology contract.
     for(int sub=0;sub<mesh.subMeshCount;sub++)first-=new System.Collections.Generic.HashSet<int>(mesh.GetTriangles(sub)).Count;
     for(int index=first;index<vertices.Count;index++)indices.Add(index);palmVertices[palm]=indices;
    }
    if(skin)UnityEngine.Object.DestroyImmediate(mesh);
   }
   if(melee||recovery||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death&&variant>=0&&frame>0){float low=float.PositiveInfinity;foreach(int index in groundSupport.Count>0?groundSupport:bodyVertices)low=Mathf.Min(low,vertices[index].y);bool settled=(kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Knockdown||(pike||captain)&&(variant==4||variant==6))&&frame>=count-3;float lift=settled?.008f-low:Mathf.Max(0,.008f-low);foreach(int index in bodyVertices)vertices[index]+=Vector3.up*lift;
    foreach(var palm in palmVertices)if(contactGroups.Count>0){float palmLow=float.PositiveInfinity;foreach(int index in palm.Value)palmLow=Mathf.Min(palmLow,vertices[index].y);report.AppendLine($"GROUND_CONTACT {rig.name} L{lod} {poseName} {palm.Key} low={palmLow:F4}");}
    foreach(var group in contactGroups){report.AppendLine($"GROUND_CONTACT {rig.name} L{lod} {poseName} {group.Key} low={group.Value+lift:F4}");}
   }
   if(recovery||rosterSword&&kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death){
    float phase=frame/(float)(count-1);bool rising=kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.GetUp||(pike||captain)&&variant==5;
    float rest=rising?1f-Mathf.SmoothStep(0,1,Mathf.InverseLerp(0,.35f,phase)):Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,1f,phase));
    foreach(var prop in groundProps){float low=float.PositiveInfinity;foreach(int index in prop)low=Mathf.Min(low,vertices[index].y);foreach(int index in prop)vertices[index]+=Vector3.up*((.008f-low)*rest);}
   }
   var combined=new Mesh{name=rig.name+"_L"+lod+"_"+poseName+"_"+frame};combined.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;combined.SetVertices(vertices);combined.SetNormals(normals);combined.SetUVs(0,uv);combined.SetTriangles(triangles,0);combined.RecalculateBounds();string path=output+"/"+combined.name+".asset";if(!swordGuardBake){var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(combined,existing);UnityEngine.Object.DestroyImmediate(combined);combined=existing;EditorUtility.SetDirty(combined);}else AssetDatabase.CreateAsset(combined,path);}poses[frame]=combined;
   if(combined.bounds.size.y<(melee||recovery||kind==EmpireBattle.AnimationAuthoring.InfantryMotionKind.Death ? .15f : 1f)||combined.bounds.size.y>4||combined.bounds.size.x>5||combined.bounds.size.z>5)throw new Exception("Implausible crowd pose bounds "+combined.name+" "+combined.bounds);
   if(command){gun.enabled=true;rod.GetComponent<Renderer>().enabled=true;}
   report.AppendLine($"{combined.name}: {triangles.Count/3} triangles, bounds {combined.bounds}");gun.transform.SetParent(poseGunParent,false);gun.transform.localPosition=poseGunPosition;gun.transform.localRotation=poseGunRotation;gun.transform.localScale=poseGunScale;
   if(rosterSword){gun.enabled=true;rod.GetComponent<Renderer>().enabled=true;}
  }if(reloadJoints!=null)ValidateReloadInterpolation(rig.transform,reloadJoints,lod,report,kneeling?.43f:0);gun.transform.SetParent(gunParent,false);gun.transform.localScale=gunLocalScale;gun.transform.localPosition=gunPosition;gun.transform.localRotation=gunRotation;return poses;
 }

 // Detached pieces are baked into each death loop: fixed topology, no duplicated held gear,
 // no per-casualty physics object, and a permanent settled endpoint.
 sealed class LooseDeathGear {
  readonly Vector3[] points,normals;readonly Vector3 centre,velocity,axis;readonly float landing;readonly Quaternion settled;
  public LooseDeathGear(Vector3[] source,Vector3[] sourceNormals,Matrix4x4 matrix,int variant,bool hat,bool ramrod){
   points=new Vector3[source.Length];normals=new Vector3[source.Length];var nm=matrix.inverse.transpose;
   for(int i=0;i<points.Length;i++){points[i]=matrix.MultiplyPoint3x4(source[i]);centre+=points[i];normals[i]=nm.MultiplyVector(sourceNormals[i]).normalized;}centre/=points.Length;
   float angle=variant*2.39996f;velocity=new Vector3(Mathf.Cos(angle)*(hat?1.2f:.8f),hat?1.1f:.35f,Mathf.Sin(angle)*(hat?1.2f:.8f));axis=new Vector3(.7f,.3f,1).normalized;
   settled=hat?Quaternion.AngleAxis(115+variant*17,axis):Quaternion.FromToRotation(matrix.MultiplyVector(ramrod?Vector3.up:Vector3.forward).normalized,new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle)));
   landing=(velocity.y+Mathf.Sqrt(velocity.y*velocity.y+19.62f*Mathf.Max(.1f,centre.y)))/9.81f;
  }
  public void Sample(float age,out Vector3[] positions,out Vector3[] directions){
   float t=Mathf.Min(age,landing);var rotation=Quaternion.Slerp(Quaternion.AngleAxis(t*260,axis),settled,Mathf.SmoothStep(0,1,Mathf.InverseLerp(.55f,1,t/landing)));Vector3 shift=velocity*t+Vector3.down*(4.905f*t*t);
   positions=new Vector3[points.Length];directions=new Vector3[points.Length];float low=float.PositiveInfinity;
   for(int i=0;i<points.Length;i++){positions[i]=centre+shift+rotation*(points[i]-centre);directions[i]=rotation*normals[i];low=Mathf.Min(low,positions[i].y);}
   if(low<.015f)for(int i=0;i<positions.Length;i++)positions[i].y+=.015f-low;
  }
 }

 static Mesh LowHead(){string path=Root+"/Meshes/LowHead.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;var vertices=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var indices=new System.Collections.Generic.List<int>();for(int y=0;y<=4;y++)for(int x=0;x<8;x++){float theta=Mathf.PI*y/4,phi=2*Mathf.PI*x/8;vertices.Add(new Vector3(Mathf.Sin(theta)*Mathf.Cos(phi),Mathf.Cos(theta),Mathf.Sin(theta)*Mathf.Sin(phi))*.5f);uv.Add(new Vector2(x/8f,y/4f));}for(int y=0;y<4;y++)for(int x=0;x<8;x++){int a=y*8+x,b=y*8+(x+1)%8,c=a+8,d=b+8;if(y>0)indices.AddRange(new[]{a,b,c});if(y<3)indices.AddRange(new[]{b,d,c});}var mesh=new Mesh{name="Procedural low head"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(indices,0);mesh.RecalculateNormals();mesh.RecalculateBounds();Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.CreateAsset(mesh,path);return mesh;}
 static Mesh CoarseWeapon(){string path=Root+"/Meshes/CoarseWeapon.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;var mesh=new Mesh{name="24 triangle coarse musket"};mesh.CombineMeshes(new[]{new CombineInstance{mesh=source,transform=Matrix4x4.TRS(new Vector3(0,0,-30),Quaternion.identity,new Vector3(1.5f,5,20))},new CombineInstance{mesh=source,transform=Matrix4x4.TRS(new Vector3(0,1.8f,3),Quaternion.identity,new Vector3(.8f,.8f,46))}},true,true);UnityEngine.Object.DestroyImmediate(cube);Directory.CreateDirectory(Root+"/Meshes");AssetDatabase.CreateAsset(mesh,path);return mesh;}

 static Mesh RamrodMesh(){string path=Root+"/Meshes/OriginalRamrod.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;var v=new System.Collections.Generic.List<Vector3>();var uv=new System.Collections.Generic.List<Vector2>();var tri=new System.Collections.Generic.List<int>();for(int y=0;y<2;y++)for(int x=0;x<6;x++){float angle=2*Mathf.PI*x/6;v.Add(new Vector3(.5f*Mathf.Cos(angle),y-.5f,.5f*Mathf.Sin(angle)));uv.Add(new Vector2(.5f,.5f));}v.Add(Vector3.down*.5f);v.Add(Vector3.up*.5f);uv.Add(Vector2.one*.5f);uv.Add(Vector2.one*.5f);for(int x=0;x<6;x++){int n=(x+1)%6;tri.AddRange(new[]{x,n,x+6,n,n+6,x+6,12,n,x,13,x+6,n+6});}var mesh=new Mesh{name="Original steel ramrod — 24 triangles"};mesh.SetVertices(v);mesh.SetUVs(0,uv);mesh.SetTriangles(tri,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;}

 public static void InspectHands(){BuildScene(false,3);var rig=UnityEngine.Object.FindFirstObjectByType<EmpireBattle.Art.InfantryInspection>().Soldiers[0];var skin=Array.Find(rig.GetComponentsInChildren<SkinnedMeshRenderer>(),r=>r.name=="combat_gloves_white");var mesh=new Mesh();skin.BakeMesh(mesh,true);var vertices=mesh.vertices;var weights=skin.sharedMesh.boneWeights;var text=new StringBuilder();foreach(string name in new[]{"l_hand","l_finger0","r_hand","r_finger0"}){int bone=Array.FindIndex(skin.bones,t=>t.name==name);var hand=Array.Find(rig.GetComponentsInChildren<Transform>(),t=>t.name==name.Substring(0,1)+"_hand");var b=new Bounds();bool first=true;Vector3 mean=Vector3.zero;int count=0;for(int v=0;v<vertices.Length;v++){var w=weights[v];float amount=(w.boneIndex0==bone?w.weight0:0)+(w.boneIndex1==bone?w.weight1:0)+(w.boneIndex2==bone?w.weight2:0)+(w.boneIndex3==bone?w.weight3:0);if(amount>.5f){var point=hand.InverseTransformPoint(skin.transform.TransformPoint(vertices[v]));if(first){b=new Bounds(point,Vector3.zero);first=false;}else b.Encapsulate(point);mean+=point;count++;}}text.AppendLine($"{name} count={count} mean={mean/Mathf.Max(1,count)} localBounds={b}");}File.WriteAllText("Logs/hand-geometry.txt",text.ToString());}

 static Mesh ClosedHandMesh(bool left){string path=Root+"/Meshes/Closed"+(left?"Left":"Right")+"Hand.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing)return existing;var vertices=new System.Collections.Generic.List<Vector3>();var indices=new System.Collections.Generic.List<int>();var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);var source=cube.GetComponent<MeshFilter>().sharedMesh;foreach(var v in source.vertices)vertices.Add(Vector3.Scale(v,new Vector3(.065f,.023f,.068f)));indices.AddRange(source.triangles);UnityEngine.Object.DestroyImmediate(cube);for(int finger=0;finger<4;finger++){float x=-.024f+finger*.016f;HandTube(vertices,indices,new[]{new Vector3(x,0,.032f),new Vector3(x,.018f,.067f),new Vector3(x,.049f,.043f)},.007f);}HandTube(vertices,indices,new[]{new Vector3(-.031f,0,-.005f),new Vector3(-.046f,.024f,.023f),new Vector3(-.019f,.046f,.034f)},.009f);if(left){for(int v=0;v<vertices.Count;v++){var point=vertices[v];point.x=-point.x;vertices[v]=point;}for(int t=0;t<indices.Count;t+=3){int swap=indices[t];indices[t]=indices[t+1];indices[t+1]=swap;}}var mesh=new Mesh{name="Authored closed "+(left?"left":"right")+" hand"};mesh.SetVertices(vertices);mesh.SetTriangles(indices,0);mesh.SetUVs(0,new System.Collections.Generic.List<Vector2>(Array.ConvertAll(vertices.ToArray(),v=>Vector2.one*.5f)));mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;}
 static void HandTube(System.Collections.Generic.List<Vector3> vertices,System.Collections.Generic.List<int> triangles,Vector3[] centres,float radius){int start=vertices.Count;for(int ring=0;ring<3;ring++){Vector3 tangent=ring==0?centres[1]-centres[0]:ring==2?centres[2]-centres[1]:centres[2]-centres[0];Vector3 x=Vector3.Cross(tangent,Vector3.right).normalized;if(x.sqrMagnitude<.01f)x=Vector3.Cross(tangent,Vector3.up).normalized;Vector3 y=Vector3.Cross(tangent.normalized,x).normalized;for(int side=0;side<3;side++){float angle=2*Mathf.PI*side/3;vertices.Add(centres[ring]+radius*(x*Mathf.Cos(angle)+y*Mathf.Sin(angle)));}}for(int ring=0;ring<2;ring++)for(int side=0;side<3;side++){int a=start+ring*3+side,b=start+ring*3+(side+1)%3;triangles.AddRange(new[]{a,b,a+3,b,b+3,a+3});}triangles.AddRange(new[]{start+2,start+1,start,start+6,start+7,start+8});}

}
