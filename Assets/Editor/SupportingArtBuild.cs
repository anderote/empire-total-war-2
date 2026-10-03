using EmpireBattle.SupportingArt;
using HorseGait = EmpireBattle.SupportingArt.ProceduralSupportingArt.HorseGait;
using HorseRig = EmpireBattle.SupportingArt.ProceduralSupportingArt.HorseRig;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Builds a persistent scene for visual review of the procedural supporting-art meshes.</summary>
public static class SupportingArtBuild
{
    const string ArtPath = "Assets/Scripts/Battle/SupportingArt";
    const string GeneratedPath = ArtPath + "/InspectionGenerated";
    const string MaterialPath = GeneratedPath + "/Materials";
    const string ScenePath = "Assets/Scenes/SupportingArtInspection.unity";

    [MenuItem("Empire Battle/Build Supporting Art Inspection")]
    public static void BuildInspectionScene()
    {
        EnsureFolder("Assets/Scripts/Battle/SupportingArt", "InspectionGenerated");
        EnsureFolder(GeneratedPath, "Materials");
        EnsureFolder("Assets", "Scenes");

        Material coat = CreateMaterial("HorseCoat", new Color(0.34f, 0.19f, 0.095f), 0.18f);
        Material skin = CreateMaterial("InfantrySkin", new Color(0.66f, 0.43f, 0.30f), 0.22f);
        Material cannonWood = CreateMaterial("CannonWood", new Color(0.29f, 0.17f, 0.075f), 0.19f);
        Material cannonIron = CreateMaterial("CannonIron", new Color(0.12f, 0.13f, 0.13f), 0.35f);
        Material cannonBronze = CreateMaterial("CannonBronze", new Color(0.32f, 0.22f, 0.095f), 0.32f);
        Material ground = CreateMaterial("InspectionGround", new Color(0.24f, 0.29f, 0.20f), 0.08f);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.44f, 0.46f, 0.49f);
        RenderSettings.fog = false;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Matte inspection ground";
        floor.transform.position = new Vector3(0, -0.02f, 0.25f);
        floor.transform.localScale = new Vector3(0.85f, 1f, 0.65f);
        floor.GetComponent<Renderer>().sharedMaterial = ground;
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        var horse = ProceduralSupportingArt.CreateHorseRig(null, coat);
        horse.Root.name = "Low-poly field horse";
        horse.Root.transform.position = new Vector3(-1.45f, 0f, -0.18f);
        horse.Root.transform.rotation = Quaternion.identity;
        SaveMesh(horse.Renderer.sharedMesh, "HorseCoat.asset");
        ValidateHorseGaits(horse);

        var cannonMeshes = ProceduralSupportingArt.CreateFieldCannonMeshes();
        var cannon = new GameObject("Period field cannon");
        cannon.transform.SetPositionAndRotation(new Vector3(1.45f, 0f, -0.25f), Quaternion.identity);
        AddCannonPart(cannon.transform, "Wood", cannonMeshes.Wood, cannonWood);
        AddCannonPart(cannon.transform, "Iron", cannonMeshes.Iron, cannonIron);
        AddCannonPart(cannon.transform, "Bronze", cannonMeshes.Bronze, cannonBronze);

        // Oversized head silhouettes let reviewers see the low-poly facial variants at scene scale.
        float[] headX = { -0.92f, -0.31f, 0.31f, 0.92f };
        Mesh firstHead = null;
        for (int i = 0; i < headX.Length; i++)
        {
            Mesh headMesh = ProceduralSupportingArt.CreateHeadMesh(i);
            if (i == 0) firstHead = headMesh;
            SaveMesh(headMesh, "HeadVariant" + i + ".asset");
            var headObject = new GameObject("Infantry head variant " + i);
            headObject.transform.SetPositionAndRotation(new Vector3(headX[i], 0.38f, 2.06f), Quaternion.identity);
            headObject.transform.localScale = Vector3.one * 3.1f;
            var filter = headObject.AddComponent<MeshFilter>();
            var renderer = headObject.AddComponent<MeshRenderer>();
            filter.sharedMesh = headMesh;
            renderer.sharedMaterial = skin;
        }

        var key = new GameObject("Key light");
        var light = key.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.3f;
        key.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

        var cameraObject = new GameObject("Supporting art camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.fieldOfView = 37f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.backgroundColor = new Color(0.62f, 0.72f, 0.78f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.transform.position = new Vector3(3.8f, 3.0f, 6.3f);
        cameraObject.transform.LookAt(new Vector3(0f, 1.0f, 0.25f));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("Built " + ScenePath + ". Horse bones=" + horse.Bones.Length
            + ", head bounds=" + firstHead.bounds
            + ", cannon barrel=" + cannonMeshes.BarrelLength + " m, wheels=" + cannonMeshes.WheelDiameter + " m.");
    }

    static void AddCannonPart(Transform parent, string partName, Mesh mesh, Material material)
    {
        SaveMesh(mesh, "Cannon" + partName + ".asset");
        var part = new GameObject(partName);
        part.transform.SetParent(parent, false);
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    static void SaveMesh(Mesh mesh, string fileName)
    {
        ValidateMesh(mesh);
        string path = GeneratedPath + "/" + fileName;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
    }

    static void ValidateMesh(Mesh mesh)
    {
        if (!mesh || mesh.vertexCount == 0) throw new System.InvalidOperationException("Empty supporting-art mesh");
        Bounds bounds = mesh.bounds;
        if (!Finite(bounds.min) || !Finite(bounds.max)) throw new System.InvalidOperationException(mesh.name + " has nonfinite bounds " + bounds);
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        if (normals.Length != vertices.Length) throw new System.InvalidOperationException(mesh.name + " is missing vertex normals");
        for (int i = 0; i < vertices.Length; i++)
            if (!Finite(vertices[i]) || !Finite(normals[i])) throw new System.InvalidOperationException(mesh.name + " has a nonfinite vertex or normal at " + i);
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
            foreach (int index in mesh.GetIndices(sub))
                if (index < 0 || index >= vertices.Length) throw new System.InvalidOperationException(mesh.name + " has an invalid triangle index");
        Debug.Log(mesh.name + ": " + mesh.vertexCount + " vertices, " + mesh.triangles.Length / 3 + " triangles, bounds " + bounds);
    }

    static void ValidateHorseGaits(HorseRig rig)
    {
        Vector3 fixedRoot = rig.Root.transform.position;
        var baked = new Mesh { name = "Temporary horse gait validation" };
        HorseGait[] gaits = { HorseGait.Idle, HorseGait.Walk, HorseGait.Trot, HorseGait.Gallop };
        foreach (HorseGait gait in gaits)
        {
            Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            for (int frame = 0; frame < 8; frame++)
            {
                ProceduralSupportingArt.SampleGait(rig, gait, frame / 8f);
                rig.Renderer.BakeMesh(baked, true);
                foreach (Vector3 vertex in baked.vertices)
                {
                    if (!Finite(vertex)) throw new System.InvalidOperationException(gait + " produced a nonfinite horse vertex");
                    min = Vector3.Min(min, vertex);
                    max = Vector3.Max(max, vertex);
                }
                if ((rig.Root.transform.position - fixedRoot).sqrMagnitude > 0.0000001f)
                    throw new System.InvalidOperationException(gait + " moved the horse root");
            }
            Bounds gaitBounds = new Bounds((min + max) * 0.5f, max - min);
            if (!Finite(gaitBounds.min) || !Finite(gaitBounds.max) || gaitBounds.size.y < 1f || gaitBounds.size.y > 4f || gaitBounds.size.z > 5f)
                throw new System.InvalidOperationException(gait + " produced implausible skinned bounds " + gaitBounds);
            Debug.Log("Horse " + gait + " sampled bounds " + gaitBounds);
        }
        Object.DestroyImmediate(baked);
        ProceduralSupportingArt.SampleGait(rig, HorseGait.Idle, 0f);
    }

    static bool Finite(Vector3 value)
    {
        return !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
            && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }

    static Material CreateMaterial(string name, Color color, float smoothness)
    {
        string path = MaterialPath + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }
}
