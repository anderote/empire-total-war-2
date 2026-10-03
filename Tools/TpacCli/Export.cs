using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using TpacTool.Lib;
using TpacTool.IO;
static class Export {
 public static void Models(Resolver resolver,string folder,string filter) {
  var report=new List<object>(); var opts=new JsonSerializerOptions{WriteIndented=false};
  foreach(var model in resolver.LoadedAssets.OfType<Metamesh>().Where(m=>Regex.IsMatch(m.Name,filter))) {
   try {
    if(model.Name.IndexOfAny(new[]{'/','\\',':'})>=0)throw new Exception("Unsafe asset name");
    var meshes=new List<object>();
    foreach(var mesh in model.Meshes.Where(m=>m.Lod==0 && m.VertexStream!=null)) {
     var v=mesh.VertexStream.Data;
     var mat=mesh.Material.TryGetItem(out var found)?found:null;
     string diffuse=null;
     if(mat!=null && mat.Textures.TryGetValue(0,out var dep) && dep.TryGetItem(out var tex) && tex.HasPixelData) {
      diffuse=tex.Name+".dds";
      if(diffuse.IndexOfAny(new[]{'/','\\',':'})>=0)throw new Exception("Unsafe texture name");
      Directory.CreateDirectory(Path.Combine(folder,"Textures"));
      TextureExporter.ExportToFile(Path.Combine(folder,"Textures",diffuse),tex);
     }
     var vertices=Enumerable.Range(0,v.Positions.Length).Select(i=>new {
      position=new[]{v.Positions[i].X,v.Positions[i].Y,v.Positions[i].Z},
      uv=i<v.Uv1.Length?new[]{v.Uv1[i].X,v.Uv1[i].Y}:new float[]{0,0},
      bones=i<v.BoneIndices.Length?new[]{(int)v.BoneIndices[i].B1,v.BoneIndices[i].B2,v.BoneIndices[i].B3,v.BoneIndices[i].B4}:new int[0],
      weights=i<v.BoneWeights.Length?new[]{v.BoneWeights[i].W1/255f,v.BoneWeights[i].W2/255f,v.BoneWeights[i].W3/255f,v.BoneWeights[i].W4/255f}:new float[0]
     });
     meshes.Add(new {name=mesh.Name,material=mat?.Name,diffuse,vertices,triangles=Enumerable.Range(0,v.Indices.Length/3).Select(i=>v.Indices.Skip(i*3).Take(3).ToArray())});
    }
    File.WriteAllText(Path.Combine(folder,model.Name+".json"),JsonSerializer.Serialize(new{name=model.Name,meshes},opts));
    report.Add(new{name=model.Name,ok=true,meshes=meshes.Count});
   } catch(Exception ex) {report.Add(new{name=model.Name,ok=false,error=ex.ToString()});}
  }
  File.WriteAllText(Path.Combine(folder,"export-receipt.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
 }
}
