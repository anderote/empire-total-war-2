using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using TpacTool.Lib;
class Program {
 static int Main(string[] args) {
  var manager = new Resolver();
  foreach(var path in Directory.EnumerateFiles(args[0], "*.tpac", SearchOption.AllDirectories)) {
   try { manager.Packages.Add(new AssetPackage(path)); }
   catch(Exception ex) { manager.Errors.Add(new {path, error=ex.Message}); }
  }
  manager.LoadedAssets = manager.Packages.SelectMany(p=>p.Items).ToList();
  DefaultDependenceResolver.Instance = manager;
  Directory.CreateDirectory(args[1]);
  var records = manager.LoadedAssets.Select(a => new {name=a.Name, guid=a.Guid, type=a.GetType().Name});
  File.WriteAllText(Path.Combine(args[1], "inventory.json"), JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
  File.WriteAllText(Path.Combine(args[1],"errors.json"),JsonSerializer.Serialize(manager.Errors));
  Console.WriteLine($"Packages: {manager.Packages.Count}; assets: {manager.LoadedAssets.Count}");
  if(args.Length>2) Export.Models(manager,args[1],args[2]);
  return 0;
 }
}

class Resolver : IDependenceResolver {
 public System.Collections.Generic.List<AssetPackage> Packages = new();
 public System.Collections.Generic.List<AssetItem> LoadedAssets = new();
 public System.Collections.Generic.List<object> Errors = new();
 public bool Resolve<T>(Guid guid,string name,out T result) where T:class,IDependence {
  result=LoadedAssets.OfType<T>().FirstOrDefault(a=>a.Guid==guid); return result!=null;
 }
}
