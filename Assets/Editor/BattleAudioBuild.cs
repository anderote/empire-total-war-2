using System;
using EmpireBattle;
using UnityEditor;
using UnityEngine;

public static class BattleAudioBuild
{
    public static void Prepare()
    {
        AssetDatabase.Refresh();
        foreach(string name in new[]{"Marching","FifeAndDrum","Gallop","HoofWalk","HorseCall","Leather1","Leather2","Leather3","Nature","Rain","Wood","Effort"})
        {
            string path="Assets/Resources/BattleAudio/"+name+".wav";
            var importer=(AudioImporter)AssetImporter.GetAtPath(path);
            if(importer==null)throw new Exception("Missing cleared audio: "+path);
            importer.forceToMono=name!="Nature";
            var settings=importer.defaultSampleSettings;
            settings.loadType=AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat=AudioCompressionFormat.Vorbis;
            settings.quality=.85f;
            importer.defaultSampleSettings=settings;importer.SaveAndReimport();
        }
        const string bankPath="Assets/Resources/BattleAudio/Bank.asset";
        var bank=AssetDatabase.LoadAssetAtPath<BattleAudioBank>(bankPath);
        if(!bank){bank=ScriptableObject.CreateInstance<BattleAudioBank>();AssetDatabase.CreateAsset(bank,bankPath);}
        bank.MarchingLoop=Resources.Load<AudioClip>("BattleAudio/Marching");
        bank.MarchMusic=Resources.Load<AudioClip>("BattleAudio/FifeAndDrum");
        bank.HoofWalk=Resources.Load<AudioClip>("BattleAudio/HoofWalk");
        bank.Gallop=Resources.Load<AudioClip>("BattleAudio/Gallop");
        bank.Nature=Resources.Load<AudioClip>("BattleAudio/Nature");
        bank.Rain=Resources.Load<AudioClip>("BattleAudio/Rain");
        bank.Tack=new[]{Resources.Load<AudioClip>("BattleAudio/Leather1"),Resources.Load<AudioClip>("BattleAudio/Leather2"),Resources.Load<AudioClip>("BattleAudio/Leather3")};
        bank.Wheel=new[]{Resources.Load<AudioClip>("BattleAudio/Wood")};
        bank.Effort=new[]{Resources.Load<AudioClip>("BattleAudio/Effort")};
        bank.HorseCall=new[]{Resources.Load<AudioClip>("BattleAudio/HorseCall")};
        bank.Credits=Resources.Load<TextAsset>("BattleAudio/Credits");
        EditorUtility.SetDirty(bank);AssetDatabase.SaveAssets();
    }

    public static void Verify()
    {
        Prepare();
        var bank=Resources.Load<BattleAudioBank>("BattleAudio/Bank");
        Require(bank&&bank.MarchingLoop&&bank.MarchMusic&&bank.Credits,"Both recordings and attribution ship in Resources");
        Require(bank.MarchingLoop.length>20&&bank.MarchMusic.length>55,"Complete working recordings");
        Require(bank.MarchingLoop.channels==1&&bank.MarchMusic.channels==1,"Spatial sources are mono");
        Require(bank.Credits.text.Contains("WebbFilmsUK")&&bank.Credits.text.Contains("Kevin MacLeod")
            &&bank.Credits.text.Contains("creativecommons.org/licenses/by/4.0/"),"Credits name creators, sources and license");
        Require(bank.HoofWalk&&bank.Gallop&&bank.Nature&&bank.Rain&&bank.Tack[0]&&bank.Wheel[0]
            &&bank.Effort[0]&&bank.HorseCall[0],"Atmospheric recording bank complete");
        Require(bank.Nature.channels==2,"Natural soundscape preserves stereo width");
        Require(BattleAudio.HorseGait(2)==0&&BattleAudio.HorseGait(4)==1&&BattleAudio.HorseGait(7)==2,"Distinct mounted gaits");
        Require(BattleAudio.NatureMix(true,16,0,1)<BattleAudio.NatureMix(false,16,0,1),"Rain suppresses natural bed");
        Require(BattleAudio.NatureMix(false,16,1,1)<BattleAudio.NatureMix(false,16,0,1),"Battle activity ducks natural bed");
        Require(BattleAudio.NatureMix(false,0,0,1)==0,"No daytime birds after dark");
        foreach(string kind in new[]{"Equipment","Cartridge","Ramrod","Lock","Hoof","Tack","CrewStep","Sponge","ShotLoad","Wheel","Flag","MeleeHit","Swish","Thunder","Wind"})
        {
            var samples=BattleAudio.GenerateSamples(kind,.35f,22050,1815);double energy=0;
            foreach(float sample in samples)
            {Require(float.IsFinite(sample)&&Mathf.Abs(sample)<=1,"Foley samples finite and bounded");energy+=sample*sample;}
            Require(energy>.01,"Foley is audible");
        }
        Require(BattleAudio.CrossedFoleyCue(1,1.1f,1.05f),"Cue crossed during frame");
        Require(!BattleAudio.CrossedFoleyCue(1.1f,1.2f,1.05f),"No duplicate cue on following frame");
        Require(!BattleAudio.CrossedFoleyCue(1,1,1),"Paused clock does not retrigger");
        Require(!BattleAudio.CrossedFoleyCue(1,2,1.1f),"Late frame discards stale foley");
        Require(!BattleAudio.CrossedFoleyCue(2,1,1.5f),"Clock reset does not replay old cue");
        BattleEffectsVerification.Verify();
        Debug.Log("BATTLE_AUDIO_VERIFIED: cleared mono loops, credits, finite foley and non-repeating cue timing.");
    }

    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    public static void VerifyAndBuild(){Verify();InfantryBattleBuild.BuildMatchedPlaytests();}
}
