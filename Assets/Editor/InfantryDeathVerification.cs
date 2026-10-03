using System;
using System.Collections.Generic;
using System.IO;
using EmpireBattle;
using EmpireBattle.Art;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline checks and offscreen production-mesh review; no player build or game launch.
public static class InfantryDeathVerification
{
    const string Output = "Artifacts/MusketDeaths";
    static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Infantry death motion: " + message); }

    static SoldierDeath Death(int soldier) => new SoldierDeath {
        Soldier = soldier, Kind = UnitKind.Infantry, Cause = DeathCause.Musket
    };

    public static void Verify()
    {
        Directory.CreateDirectory(Output);
        const int samples = 16384;
        var counts = new int[10];
        float shortest = float.MaxValue, longest = 0;
        for (int id = 0; id < samples; id++)
        {
            var death = Death(id);
            int variant = InfantryDeathMotion.Variant(death, 10);
            counts[variant]++;
            float duration = InfantryDeathMotion.Duration(death, variant);
            shortest = Mathf.Min(shortest, duration); longest = Mathf.Max(longest, duration);
            Require(duration >= .9f && duration <= 1.7f, "Bounded fall timing");
            Require(variant == InfantryDeathMotion.Variant(death, 10), "Stable per-soldier selection");
            death.Born = 200;
            Require(variant == InfantryDeathMotion.Variant(death, 10), "Battle clock cannot change a selected fall");
            foreach (int size in new[] { 0, 1, 3, 12 })
            {
                int selected = InfantryDeathMotion.Variant(death, size);
                Require(size == 0 ? selected == -1 : selected >= 0 && selected < size, "Legacy library selection stays in range");
            }
            foreach (var cause in new[] { DeathCause.Melee, DeathCause.Roundshot, DeathCause.Shell, DeathCause.Fire })
            {
                death.Cause = cause;
                int previous = Mathf.Min(9, (int)(RifleFireTiming.Variation(id, 83) * 10));
                Require(InfantryDeathMotion.Variant(death, 10) == previous, "Other death causes retain their selection");
                Require(Mathf.Abs(InfantryDeathMotion.Duration(death, variant) - 1.25f * Mathf.Lerp(.82f, 1.18f, RifleFireTiming.Variation(id, 89))) < .00001f,
                    "Other death causes retain their timing");
            }
            death.Cause = DeathCause.Musket;
            int review = Mathf.Min(9, (int)(RifleFireTiming.Variation(id, 83 + Mathf.FloorToInt(death.Born * 19)) * 10));
            Require(InfantryDeathMotion.Variant(death, 10, true) == review, "Asset review retains all variants equally");
        }
        float forward = (counts[1] + counts[3] + counts[7]) / (float)samples;
        float backward = (counts[0] + counts[2] + counts[6]) / (float)samples;
        Require(forward > .62f && forward < .66f, "Most musket deaths collapse forward");
        Require(backward > .26f && backward < .30f, "Backward falls remain a substantial contrast");
        Require(counts[1] > samples * .20f && counts[3] > samples * .16f && counts[7] > samples * .13f,
            "All three forward falls occur frequently");
        Require(longest - shortest > .65f, "Volley casualties have visibly varied timing");
        for (int start = 0; start < 512; start += 32)
        {
            var variants = new HashSet<int>();
            for (int i = 0; i < 32; i++) variants.Add(InfantryDeathMotion.Variant(Death(start + i), 10));
            Require(variants.Count >= 5, "Contiguous rank casualties mix several falls");
        }

        var library = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        Require(library, "Common infantry library exists");
        int tracks = 0;
        void CheckSet(InfantryCrowdSet set)
        {
            foreach (var lod in set.Lods)
            {
                Require(lod.DeathVariants?.Length == 10, "Both LODs carry the directional variant contract");
                foreach (var variant in lod.DeathVariants)
                {
                    var poses = variant.Poses;
                    Require(poses?.Length >= 12, "Complete animated death track");
                    for (int i = 0; i < poses.Length; i++)
                    {
                        var pose = poses[i]; var next = poses[Mathf.Min(i + 1, poses.Length - 1)];
                        var targets = new List<Vector3>(); pose.GetUVs(2, targets);
                        Require(pose.vertexCount == next.vertexCount && targets.Count == pose.vertexCount, "Continuous mesh interpolation");
                        var points = next.vertices;
                        for (int v = 0; v < points.Length; v++)
                            Require((points[v] - targets[v]).sqrMagnitude < 1e-8f, "Baked body and loose gear interpolate to the next pose");
                    }
                    Require(poses[poses.Length - 1].bounds.min.y >= -.002f, "Settled body and dropped gear remain above the ground");
                    tracks++;
                }
            }
        }
        CheckSet(library.French); CheckSet(library.British);
        var cartridges=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
        Require(cartridges,"Cartridge infantry library exists");
        CheckSet(cartridges.French);CheckSet(cartridges.British);
        CheckSet(cartridges.SingleFrench);CheckSet(cartridges.SingleBritish);
        foreach (string faction in new[] { "ming", "india" })
        foreach (string family in new[] { "line", "light", "sword" })
        {
            var regional = Resources.Load<UnitVisualLibrary>("RegionalRoster/" + faction + "-" + family + "/Library");
            Require(regional, "Regional infantry library exists"); CheckSet(regional.Set);
        }
        string report = $"Passed: {samples} musket casualty selections; forward {forward:P2}, backward {backward:P2}, sideways {1 - forward - backward:P2}.\n" +
            $"Three frequent forward falls; durations {shortest:F2}–{longest:F2}s; stable rank variation, legacy libraries, other causes and asset review checked.\n" +
            $"{tracks} production death tracks checked for interpolation and grounded endpoints, common, cartridge and regional infantry at every LOD.\n" +
            "Variant counts: " + string.Join(", ", counts);
        File.WriteAllText(Output + "/verification.txt", report);
        Debug.Log("MUSKET_DEATHS_VERIFIED " + report);
    }

    public static void VerifyAndCapture()
    {
        Verify();
        var setup = EditorSceneManager.GetSceneManagerSetup();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var library = Resources.Load<InfantryCrowdLibrary>("BakedInfantry/InfantryCrowdLibrary");
        var camera = new GameObject("Death review camera").AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = 4.7f;
        camera.transform.position = new Vector3(10, 9, 14); camera.transform.LookAt(new Vector3(0, .6f, -1.5f));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.13f, .16f, .20f);
        var light = new GameObject("Death review light").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        RenderSettings.ambientLight = Color.gray; RenderSettings.fog = false;
        Shader.SetGlobalVector("_BattleAmbientSky", new Vector4(.7f, .75f, .85f, 1));
        Shader.SetGlobalVector("_BattleAmbientEquator", new Vector4(.65f, .67f, .7f, 1));
        Shader.SetGlobalVector("_BattleAmbientGround", new Vector4(.45f, .45f, .5f, 1));
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.position = Vector3.down * .01f;
        floor.transform.localScale = Vector3.one * 3;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor", new Color(.19f, .22f, .17f));
        floor.GetComponent<Renderer>().sharedMaterial = material;
        const int width = 720, height = 440;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target;
        var tile = new Texture2D(width, height, TextureFormat.RGB24, false);
        var sheet = new Texture2D(width * 3, height * 2, TextureFormat.RGB24, false);
        var filters = new MeshFilter[24]; var renderers = new MeshRenderer[24];
        var properties = new MaterialPropertyBlock();
        for (int i = 0; i < 24; i++)
        {
            var soldier = new GameObject("Rank soldier " + i);
            soldier.transform.position = new Vector3((i % 8 - 3.5f) * 1.05f, 0, -(i / 8) * 1.6f);
            filters[i] = soldier.AddComponent<MeshFilter>(); renderers[i] = soldier.AddComponent<MeshRenderer>();
        }
        try
        {
            var cartridges=Resources.Load<CartridgeRifleLibrary>("CartridgeRifles/Library");
            var reviews=new[] {
                (Name:"French",Set:library.French),(Name:"British",Set:library.British),
                (Name:"French-magazine",Set:cartridges.French),(Name:"British-magazine",Set:cartridges.British),
                (Name:"French-single",Set:cartridges.SingleFrench),(Name:"British-single",Set:cartridges.SingleBritish)};
            foreach (var review in reviews)
            {
                var set = review.Set; var lod = set.Lods[0];
                var times = new[] { 0f, .3f, .6f, .9f, 1.25f, 1.75f };
                for (int frame = 0; frame < times.Length; frame++)
                {
                    for (int i = 0; i < filters.Length; i++)
                    {
                        var death = Death(i + 64); int variant = InfantryDeathMotion.Variant(death, 10);
                        bool hit = i % 3 != 0;
                        var poses = hit ? lod.DeathVariants[variant].Poses : lod.Idle;
                        float phase = hit ? times[frame] / InfantryDeathMotion.Duration(death, variant) : 0;
                        int index = AnimationPlayback.Frame(poses.Length, phase, false, out float blend);
                        filters[i].sharedMesh = poses[index]; renderers[i].sharedMaterial = set.Material;
                        properties.SetFloat("_PoseBlend", blend); renderers[i].SetPropertyBlock(properties);
                    }
                    camera.Render(); RenderTexture.active = target;
                    tile.ReadPixels(new Rect(0, 0, width, height), 0, 0); tile.Apply();
                    sheet.SetPixels(frame % 3 * width, (1 - frame / 3) * height, width, height, tile.GetPixels());
                }
                sheet.Apply(); File.WriteAllBytes(Output + "/" + review.Name + "-ranks.png", sheet.EncodeToPNG());
            }
            File.WriteAllText(Output + "/capture.txt", "Rows read left to right: 0, 0.30, 0.60, 0.90, 1.25, 1.75 seconds.\n24 soldiers in three ranks; 16 simultaneous musket casualties, eight survivors. Production meshes and runtime variant/timing selection.\n");
            Debug.Log("MUSKET_DEATH_RANKS_CAPTURED " + Output);
        }
        finally
        {
            RenderTexture.active = null; camera.targetTexture = null; target.Release();
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(tile);
            UnityEngine.Object.DestroyImmediate(sheet); UnityEngine.Object.DestroyImmediate(material);
            if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else EditorSceneManager.CloseScene(scene, true);
        }
    }
}
