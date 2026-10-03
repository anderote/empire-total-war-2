using System;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEngine;

public static class RegimentPresentationVerification
{
    public static void Verify()
    {
        var library=Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods){
            Require(lod.Command.Length==5,"Complete command hierarchy");
            foreach(var poses in new[]{lod.Command[3].Idle,lod.Command[3].March})foreach(var pose in poses)
                Require(pose.bounds.max.y>3.1f,"Sergeant's pike rises above the files");
            foreach(var poses in new[]{lod.Command[4].Idle,lod.Command[4].March})foreach(var pose in poses)
                Require(pose.bounds.max.y>2.4f&&pose.bounds.size.z>1.5f,"Colonel has mounted height and horse silhouette");
        }
        foreach(var set in new[]{library.French,library.British})foreach(var lod in set.Lods)
        foreach(var poses in new[]{lod.Command[2].Idle,lod.Command[2].March}){
            var first=poses[0].vertices;float travel=0;int cloth=0;
            foreach(var pose in poses){
                var points=pose.vertices;Require(points.Length==first.Length,"Stable cloth interpolation topology");
                for(int i=0;i<points.Length;i++)if(first[i].y>2.1f&&first[i].x<-.2f){
                    travel=Mathf.Max(travel,Mathf.Abs(points[i].z-first[i].z));cloth++;
                    Require(points[i].y>1.95f,"Colour stays clear of bearer's head");
                }
                Require(pose.bounds.size.y<3.3f,"Standard remains at human scale");
            }
            Require(cloth>1000&&travel>.15f,"Subdivided colour visibly billows");
        }
        var world=new BattleWorld(new BattleConfig{BattalionsPerSide=1,SoldiersPerBattalion=250,EnableLandscape=false,EnableAI=false});
        var unit=world.Battalions[0];unit.Formation=FormationKind.Line;
        Require(CommandPartyLayout.Platoons(unit,16)==4,"Four command sections for the default battalion");
        foreach(var formation in new[]{FormationKind.Line,FormationKind.Column,FormationKind.Square,FormationKind.Skirmish}){
            world.SetFormation(unit.Id,formation);
            int platoons=CommandPartyLayout.Platoons(unit,16);
            for(int role=0;role<5;role++)for(int i=0;i<CommandPartyLayout.Count(unit,role,platoons);i++){
                Vector2 offset=CommandPartyLayout.Offset(unit,role,i,platoons);
                Require(!float.IsNaN(offset.x)&&!float.IsNaN(offset.y),"Finite command positions");
                if(formation==FormationKind.Square)Require(Mathf.Abs(offset.x)<FormationMath.HalfExtents(unit).x&&Mathf.Abs(offset.y)<FormationMath.HalfExtents(unit).y,"Command stays inside square");
            }
        }
        unit.AliveCount=7;Require(CommandPartyLayout.Platoons(unit,16)==0,"Shattered battalions lose decorative command party");
        world.MovementJobs?.Dispose();
        Debug.Log("REGIMENT_PRESENTATION_VERIFIED: both factions/LODs, billowing loops and all formation layouts.");
    }
    static void Require(bool pass,string message){if(!pass)throw new Exception("Regiment presentation: "+message);}
}
