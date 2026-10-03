using System;
using System.Collections.Generic;
using UnityEngine;

namespace EmpireBattle.SupportingArt
{
    /// <summary>Small procedural meshes for reusable battlefield props and low-detail soldiers.</summary>
    public static class ProceduralSupportingArt
    {
        static readonly float[] WalkOffsets = { 0f, 0.50f, 0.75f, 0.25f };
        static readonly float[] TrotOffsets = { 0f, 0.50f, 0.50f, 0f };
        static readonly float[] GallopOffsets = { 0f, 0.58f, 0.16f, 0.42f };

        /// <summary>
        /// Builds a low-poly head centered at the origin. Y is up and the face looks toward +Z.
        /// Each seed changes a few facial proportions while retaining an approximately 0.18 x 0.24 m silhouette.
        /// The returned mesh is one draw-ready material region; assign a skin material when rendering.
        /// </summary>
        public static Mesh CreateHeadMesh(int variantSeed = 0)
        {
            var shape = new GeometryBuffer();
            var random = new System.Random(variantSeed);
            float width = 0.177f + (float)random.NextDouble() * 0.006f;
            float depth = 0.157f + (float)random.NextDouble() * 0.008f;
            float jawScale = 0.96f + (float)random.NextDouble() * 0.08f;
            float noseLength = 0.027f + (float)random.NextDouble() * 0.006f;

            // Skull and tapered lower face overlap slightly to avoid visible seams.
            shape.AddEllipsoid(new Vector3(0, 0.018f, -0.006f), new Vector3(width * 0.46f, 0.100f, depth * 0.47f), 8, 4);
            shape.AddEllipsoid(new Vector3(0, -0.049f, 0.006f), new Vector3(width * 0.36f * jawScale, 0.069f, depth * 0.405f), 8, 2);

            // Ears are shallow ellipsoids at temple height.
            shape.AddEllipsoid(new Vector3(-width * 0.45f, -0.001f, -0.001f), new Vector3(0.009f, 0.025f, 0.018f), 6, 2);
            shape.AddEllipsoid(new Vector3(width * 0.45f, -0.001f, -0.001f), new Vector3(0.009f, 0.025f, 0.018f), 6, 2);

            // Nose bridge and tip are a small faceted wedge on the front (+Z) plane.
            float noseHalf = 0.010f + (float)random.NextDouble() * 0.002f;
            Vector3 bridgeTop = new Vector3(0, 0.016f, depth * 0.47f + 0.004f);
            Vector3 bridgeLeft = new Vector3(-noseHalf, -0.014f, depth * 0.47f + 0.012f);
            Vector3 bridgeRight = new Vector3(noseHalf, -0.014f, depth * 0.47f + 0.012f);
            Vector3 noseTip = new Vector3(0, -0.031f, depth * 0.47f + noseLength);
            shape.AddTetrahedron(bridgeTop, bridgeLeft, bridgeRight, noseTip);

            // Subtle brow planes catch light and give the face a readable front at crowd scale.
            float browZ = depth * 0.455f;
            shape.AddBox(new Vector3(-0.022f, 0.037f, browZ), new Vector3(0.034f, 0.008f, 0.012f), Quaternion.Euler(0, -7f, -8f));
            shape.AddBox(new Vector3(0.022f, 0.037f, browZ), new Vector3(0.034f, 0.008f, 0.012f), Quaternion.Euler(0, 7f, 8f));

            Mesh mesh = shape.ToMesh("Procedural infantry head " + variantSeed);
            return mesh;
        }

        /// <summary>Builds the carriage, wheels and barrel as three combined material-region meshes.</summary>
        public static FieldCannonMeshSet CreateFieldCannonMeshes()
        {
            var wood = new GeometryBuffer();
            var iron = new GeometryBuffer();
            var bronze = new GeometryBuffer();

            // Barrel: 2 m from breech to muzzle, axis +Z, with a stepped period muzzle and reinforce bands.
            float axleY = 0.61f;
            float barrelY = 0.90f;
            float barrelBack = -1.15f;
            float barrelFront = 0.85f;
            AddBarrel(bronze, barrelBack, barrelFront, barrelY);

            // Two 1.2 m wheels: radial construction allows the spokes and tire to share batched meshes.
            float wheelRadius = 0.60f;
            float wheelZ = -0.18f;
            float wheelX = 0.60f;
            AddWheel(wood, iron, new Vector3(-wheelX, axleY, wheelZ), wheelRadius, false);
            AddWheel(wood, iron, new Vector3(wheelX, axleY, wheelZ), wheelRadius, true);

            // Axle, cheeks, trail and cross braces form a compact wooden field carriage.
            wood.AddCylinder(new Vector3(-wheelX, axleY, wheelZ), new Vector3(wheelX, axleY, wheelZ), 0.075f, 10);
            wood.AddBox(new Vector3(-0.30f, 0.63f, -0.37f), new Vector3(0.17f, 0.17f, 1.40f), Quaternion.Euler(-7f, 0, 0));
            wood.AddBox(new Vector3(0.30f, 0.63f, -0.37f), new Vector3(0.17f, 0.17f, 1.40f), Quaternion.Euler(-7f, 0, 0));
            wood.AddBox(new Vector3(0, 0.55f, -0.48f), new Vector3(0.70f, 0.13f, 0.20f), Quaternion.identity);
            wood.AddBox(new Vector3(0, 0.45f, -1.12f), new Vector3(0.16f, 0.15f, 1.26f), Quaternion.Euler(-9f, 0, 0));
            wood.AddBox(new Vector3(0, 0.83f, -0.26f), new Vector3(0.55f, 0.09f, 0.31f), Quaternion.identity);

            // Iron trunnion caps and wheel hubs add restrained fittings without a per-cannon hierarchy.
            iron.AddCylinder(new Vector3(-0.40f, barrelY, -0.23f), new Vector3(-0.19f, barrelY, -0.23f), 0.075f, 10);
            iron.AddCylinder(new Vector3(0.19f, barrelY, -0.23f), new Vector3(0.40f, barrelY, -0.23f), 0.075f, 10);

            return new FieldCannonMeshSet(
                wood.ToMesh("Field cannon carriage wood"),
                iron.ToMesh("Field cannon iron fittings"),
                bronze.ToMesh("Field cannon bronze barrel"),
                wheelRadius * 2f, barrelFront - barrelBack, barrelY);
        }

        /// <summary>
        /// Creates a compact, skinned prototype horse facing +Z. The coat is one combined mesh with one
        /// vertex influence per anatomical region, so the model uses one renderer instead of mesh objects
        /// for individual body pieces. The returned rig caches its 18-bone map and rest pose.
        /// </summary>
        public static HorseRig CreateHorseRig(Transform parent, Material coatMaterial)
        {
            var root = new GameObject("Procedural Horse");
            if (parent) root.transform.SetParent(parent, false);

            var rig = new HorseRig(root);
            rig.Pelvis = CreateBone(rig, "Pelvis", root.transform, new Vector3(0, 1.15f, -0.33f));
            rig.Torso = CreateBone(rig, "Torso", rig.Pelvis, new Vector3(0, 1.32f, 0.02f));
            rig.Neck = CreateBone(rig, "Neck", rig.Torso, new Vector3(0, 1.55f, 0.42f));
            rig.Head = CreateBone(rig, "Head", rig.Neck, new Vector3(0, 1.87f, 0.78f));

            string[] legNames = { "ForeLeft", "ForeRight", "HindLeft", "HindRight" };
            for (int i = 0; i < 4; i++)
            {
                bool fore = i < 2;
                float side = (i % 2 == 0) ? -1f : 1f;
                float z = fore ? 0.43f : -0.63f;
                Transform parentBone = fore ? rig.Torso : rig.Pelvis;
                rig.LegUpper[i] = CreateBone(rig, legNames[i] + "Upper", parentBone, new Vector3(side * 0.22f, 1.00f, z));
                rig.LegLower[i] = CreateBone(rig, legNames[i] + "Lower", rig.LegUpper[i], new Vector3(side * 0.22f, 0.55f, z));
                rig.Hoof[i] = CreateBone(rig, legNames[i] + "Hoof", rig.LegLower[i], new Vector3(side * 0.22f, 0.12f, z + (fore ? 0.04f : -0.04f)));
            }

            rig.TailBase = CreateBone(rig, "TailBase", rig.Pelvis, new Vector3(0, 1.29f, -0.78f));
            rig.TailTip = CreateBone(rig, "TailTip", rig.TailBase, new Vector3(0, 1.12f, -1.05f));

            var riderMount = new GameObject("RiderMount").transform;
            riderMount.SetParent(rig.Torso, false);
            riderMount.localPosition = new Vector3(0, 0.31f, -0.12f);
            rig.RiderMount = riderMount;

            rig.CacheRestPose();
            Mesh mesh = CreateHorseMesh(rig);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterial = coatMaterial;
            renderer.bones = rig.Bones;
            renderer.rootBone = rig.Pelvis;
            renderer.localBounds = mesh.bounds;
            renderer.updateWhenOffscreen = true;
            rig.Renderer = renderer;
            return rig;
        }

        /// <summary>
        /// Samples one loop cycle, where phase is in [0,1). Root motion is intentionally zero; move the
        /// horse root separately to place it in a formation. The cached rest pose is restored each sample.
        /// </summary>
        public static void SampleGait(HorseRig rig, HorseGait gait, float phase)
        {
            if (rig == null) throw new ArgumentNullException("rig");
            phase = Mathf.Repeat(phase, 1f);
            rig.RestorePose();

            float amplitude = gait == HorseGait.Walk ? 19f : gait == HorseGait.Trot ? 31f : gait == HorseGait.Gallop ? 43f : 0f;
            float bob = gait == HorseGait.Walk ? 0.008f : gait == HorseGait.Trot ? 0.018f : gait == HorseGait.Gallop ? 0.027f : 0f;
            float cycle = phase * Mathf.PI * 2f;
            if (gait != HorseGait.Idle)
            {
                rig.Torso.localPosition += Vector3.up * (Mathf.Sin(cycle * 2f) * bob);
                float[] offsets = gait == HorseGait.Walk ? WalkOffsets : gait == HorseGait.Trot ? TrotOffsets : GallopOffsets;

                for (int i = 0; i < 4; i++)
                {
                    float stride = cycle + offsets[i] * Mathf.PI * 2f;
                    float swing = Mathf.Sin(stride) * amplitude;
                    float lift = Mathf.Max(0f, Mathf.Sin(stride + 0.35f)) * (gait == HorseGait.Gallop ? 0.28f : 0.18f);
                    rig.LegUpper[i].localRotation = Quaternion.AngleAxis(swing, Vector3.right) * rig.LegUpper[i].localRotation;
                    rig.LegLower[i].localRotation = Quaternion.AngleAxis(-lift * 105f, Vector3.right) * rig.LegLower[i].localRotation;
                    rig.Hoof[i].localRotation = Quaternion.AngleAxis(-swing * 0.25f + lift * 40f, Vector3.right) * rig.Hoof[i].localRotation;
                }
                rig.Neck.localRotation = Quaternion.AngleAxis(Mathf.Sin(cycle * 2f) * (gait == HorseGait.Gallop ? 5f : 2.5f), Vector3.right) * rig.Neck.localRotation;
                rig.Head.localRotation = Quaternion.AngleAxis(Mathf.Sin(cycle * 2f + 0.4f) * 2f, Vector3.right) * rig.Head.localRotation;
                rig.TailBase.localRotation = Quaternion.AngleAxis(Mathf.Sin(cycle + 0.8f) * 11f, Vector3.up) * rig.TailBase.localRotation;
                rig.TailTip.localRotation = Quaternion.AngleAxis(Mathf.Sin(cycle + 1.4f) * 15f, Vector3.up) * rig.TailTip.localRotation;
            }
            else
            {
                // A small breath and tail sway keep the standing cycle alive without translating the root.
                rig.Torso.localPosition += Vector3.up * (Mathf.Sin(cycle + Mathf.PI * 0.5f) * 0.004f);
                rig.TailBase.localRotation = Quaternion.AngleAxis(Mathf.Sin(cycle) * 3f, Vector3.up) * rig.TailBase.localRotation;
            }
        }

        static Transform CreateBone(HorseRig rig, string name, Transform parent, Vector3 rootSpacePosition)
        {
            var bone = new GameObject(name).transform;
            bone.SetParent(parent, false);
            bone.localPosition = parent.InverseTransformPoint(rig.Root.transform.TransformPoint(rootSpacePosition));
            rig.RegisterBone(bone);
            return bone;
        }

        static Mesh CreateHorseMesh(HorseRig rig)
        {
            var geo = new HorseGeometryBuffer(rig.Bones.Length);
            Func<Transform, int> bone = rig.IndexOf;
            int pelvis = bone(rig.Pelvis), torso = bone(rig.Torso), neck = bone(rig.Neck), head = bone(rig.Head);

            // Barrel, hindquarters, shoulder and top line overlap into a broad, rounded equine torso.
            geo.AddEllipsoid(new Vector3(0, 1.25f, -0.04f), new Vector3(0.275f, 0.345f, 0.66f), pelvis, Quaternion.identity, 10, 6);
            geo.AddEllipsoid(new Vector3(0, 1.24f, -0.47f), new Vector3(0.29f, 0.36f, 0.39f), pelvis, Quaternion.identity, 8, 5);
            geo.AddEllipsoid(new Vector3(0, 1.30f, 0.40f), new Vector3(0.30f, 0.35f, 0.34f), torso, Quaternion.identity, 8, 5);
            geo.AddEllipsoid(new Vector3(0, 1.54f, -0.01f), new Vector3(0.205f, 0.105f, 0.55f), torso, Quaternion.identity, 8, 3);

            // Neck curves upward and forward from the withers; the rounded head and muzzle extend the nose.
            geo.AddEllipsoid(new Vector3(0, 1.63f, 0.56f), new Vector3(0.195f, 0.48f, 0.23f), neck, Quaternion.Euler(35f, 0, 0), 9, 6);
            geo.AddEllipsoid(new Vector3(0, 1.93f, 0.83f), new Vector3(0.145f, 0.20f, 0.19f), head, Quaternion.Euler(16f, 0, 0), 8, 5);
            geo.AddEllipsoid(new Vector3(0, 1.85f, 1.025f), new Vector3(0.112f, 0.105f, 0.18f), head, Quaternion.Euler(10f, 0, 0), 8, 4);
            geo.AddEllipsoid(new Vector3(0, 2.11f, 0.81f), new Vector3(0.09f, 0.065f, 0.13f), head, Quaternion.Euler(24f, 0, 0), 8, 3);
            geo.AddEllipsoid(new Vector3(-0.085f, 2.13f, 0.80f), new Vector3(0.043f, 0.13f, 0.048f), head, Quaternion.Euler(-10f, 0, -12f), 6, 3);
            geo.AddEllipsoid(new Vector3(0.085f, 2.13f, 0.80f), new Vector3(0.043f, 0.13f, 0.048f), head, Quaternion.Euler(-10f, 0, 12f), 6, 3);

            for (int i = 0; i < 4; i++)
            {
                bool fore = i < 2;
                float side = i % 2 == 0 ? -1f : 1f;
                float z = fore ? 0.43f : -0.63f;
                int upper = bone(rig.LegUpper[i]), lower = bone(rig.LegLower[i]), hoof = bone(rig.Hoof[i]);
                Vector3 upperCenter = new Vector3(side * 0.22f, 0.78f, z);
                Vector3 lowerCenter = new Vector3(side * 0.22f, 0.34f, z + (fore ? 0.015f : -0.015f));
                geo.AddEllipsoid(upperCenter, fore ? new Vector3(0.105f, 0.29f, 0.12f) : new Vector3(0.135f, 0.31f, 0.15f), upper, Quaternion.identity, 7, 4);
                geo.AddEllipsoid(lowerCenter, new Vector3(0.066f, 0.265f, 0.075f), lower, Quaternion.identity, 7, 4);
                geo.AddEllipsoid(new Vector3(side * 0.22f, 0.105f, z + (fore ? 0.05f : -0.05f)), new Vector3(0.095f, 0.095f, 0.145f), hoof, Quaternion.identity, 7, 4);
            }

            int tailBase = bone(rig.TailBase), tailTip = bone(rig.TailTip);
            geo.AddEllipsoid(new Vector3(0, 1.20f, -0.94f), new Vector3(0.078f, 0.25f, 0.11f), tailBase, Quaternion.Euler(34f, 0, 0), 7, 4);
            geo.AddEllipsoid(new Vector3(0, 1.02f, -1.12f), new Vector3(0.10f, 0.20f, 0.10f), tailTip, Quaternion.Euler(35f, 0, 0), 7, 4);

            Mesh mesh = geo.ToMesh("Procedural low-poly horse coat");
            Transform[] bones = rig.Bones;
            var bindposes = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                bindposes[i] = bones[i].worldToLocalMatrix * rig.Root.transform.localToWorldMatrix;
            mesh.bindposes = bindposes;
            return mesh;
        }

        public enum HorseGait { Idle, Walk, Trot, Gallop }

        /// <summary>Cached 18-bone prototype. Future cavalry can attach a rider to RiderMount.</summary>
        public sealed class HorseRig
        {
            readonly List<Transform> boneList = new List<Transform>(18);
            readonly Dictionary<Transform, int> boneIndices = new Dictionary<Transform, int>();
            Transform[] bonesCache;
            Vector3[] restPositions;
            Quaternion[] restRotations;
            public readonly GameObject Root;
            public SkinnedMeshRenderer Renderer;
            public Transform Pelvis, Torso, Neck, Head, TailBase, TailTip, RiderMount;
            public readonly Transform[] LegUpper = new Transform[4];
            public readonly Transform[] LegLower = new Transform[4];
            public readonly Transform[] Hoof = new Transform[4];
            public Transform[] Bones { get { return bonesCache; } }

            internal HorseRig(GameObject root) { Root = root; }
            internal void RegisterBone(Transform bone) { boneIndices[bone] = boneList.Count; boneList.Add(bone); }
            internal int IndexOf(Transform bone) { return boneIndices[bone]; }
            internal void CacheRestPose()
            {
                restPositions = new Vector3[boneList.Count];
                restRotations = new Quaternion[boneList.Count];
                for (int i = 0; i < boneList.Count; i++)
                {
                    restPositions[i] = boneList[i].localPosition;
                    restRotations[i] = boneList[i].localRotation;
                }
                bonesCache = boneList.ToArray();
            }
            internal void RestorePose()
            {
                for (int i = 0; i < boneList.Count; i++)
                {
                    boneList[i].localPosition = restPositions[i];
                    boneList[i].localRotation = restRotations[i];
                }
            }
        }

        sealed class HorseGeometryBuffer
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();
            readonly List<BoneWeight> weights = new List<BoneWeight>();
            readonly int boneCount;

            public HorseGeometryBuffer(int boneCount) { this.boneCount = boneCount; }

            public void AddEllipsoid(Vector3 center, Vector3 radii, int boneIndex, Quaternion rotation, int segments, int latitudeSegments)
            {
                int start = vertices.Count;
                for (int lat = 0; lat <= latitudeSegments; lat++)
                {
                    float phi = Mathf.PI * lat / latitudeSegments;
                    for (int lon = 0; lon < segments; lon++)
                    {
                        float theta = Mathf.PI * 2f * lon / segments;
                        Vector3 p = new Vector3(radii.x * Mathf.Sin(phi) * Mathf.Cos(theta), radii.y * Mathf.Cos(phi), radii.z * Mathf.Sin(phi) * Mathf.Sin(theta));
                        AddVertex(center + rotation * p, boneIndex);
                    }
                }
                for (int lat = 0; lat < latitudeSegments; lat++)
                    for (int lon = 0; lon < segments; lon++)
                    {
                        int a = start + lat * segments + lon;
                        int b = start + lat * segments + (lon + 1) % segments;
                        int c = a + segments;
                        int d = start + (lat + 1) * segments + (lon + 1) % segments;
                        AddTriangle(a, b, c); AddTriangle(b, d, c);
                    }
            }

            void AddVertex(Vector3 position, int boneIndex)
            {
                vertices.Add(position);
                weights.Add(new BoneWeight { boneIndex0 = Mathf.Clamp(boneIndex, 0, boneCount - 1), weight0 = 1f });
            }
            void AddTriangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }

            public Mesh ToMesh(string meshName)
            {
                var mesh = new Mesh { name = meshName };
                if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.boneWeights = weights.ToArray();
                mesh.SetTriangles(triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static void AddBarrel(GeometryBuffer target, float back, float front, float y)
        {
            // Profile stations create a cast barrel with breech swell, reinforce rings and a flared muzzle.
            float[] z = { back, back + 0.08f, back + 0.19f, back + 0.28f, back + 0.45f, front - 0.38f, front - 0.29f, front - 0.21f, front - 0.10f, front - 0.055f, front };
            float[] r = { 0.122f, 0.139f, 0.139f, 0.117f, 0.103f, 0.103f, 0.120f, 0.121f, 0.111f, 0.128f, 0.131f };
            target.AddLatheZ(z, r, y, 16);
            // Closed bore: a dark inset face gives the muzzle readable depth from the front.
            target.AddDiscZ(front - 0.015f, y, 0.073f, 16, true);
            target.AddCylinder(new Vector3(0, y, back - 0.035f), new Vector3(0, y, back + 0.015f), 0.104f, 16);
        }

        static void AddWheel(GeometryBuffer wood, GeometryBuffer iron, Vector3 center, float radius, bool reverse)
        {
            const int spokes = 10;
            float sign = reverse ? -1f : 1f;
            // Wooden spoke disk: hub and tapered-ish rectangular spokes share a single combined mesh.
            wood.AddCylinder(new Vector3(center.x - 0.10f, center.y, center.z), new Vector3(center.x + 0.10f, center.y, center.z), 0.13f, 10);
            for (int i = 0; i < spokes; i++)
            {
                float angle = i * Mathf.PI * 2f / spokes;
                float midRadius = radius * 0.51f;
                var spokeCenter = center + new Vector3(0, Mathf.Sin(angle) * midRadius, Mathf.Cos(angle) * midRadius);
                Quaternion rotation = Quaternion.Euler(Mathf.Rad2Deg * angle, 0, 0);
                wood.AddBox(spokeCenter, new Vector3(0.105f, radius * 0.57f, 0.11f), rotation);
            }
            // Tire is an annular faceted band. Slightly offset the two wheels for the outside iron wear strip.
            float outerX = center.x + sign * 0.075f;
            iron.AddWheelRim(new Vector3(outerX, center.y, center.z), radius, radius - 0.085f, 0.055f, 16);
            iron.AddCylinder(new Vector3(center.x - 0.125f, center.y, center.z), new Vector3(center.x + 0.125f, center.y, center.z), 0.050f, 10);
        }

        /// <summary>Meshes grouped by material so renderers can batch every cannon of a type.</summary>
        public sealed class FieldCannonMeshSet
        {
            public readonly Mesh Wood;
            public readonly Mesh Iron;
            public readonly Mesh Bronze;
            public readonly float WheelDiameter;
            public readonly float BarrelLength;
            public readonly float BarrelCenterHeight;

            public FieldCannonMeshSet(Mesh wood, Mesh iron, Mesh bronze, float wheelDiameter, float barrelLength, float barrelCenterHeight)
            {
                Wood = wood; Iron = iron; Bronze = bronze;
                WheelDiameter = wheelDiameter; BarrelLength = barrelLength; BarrelCenterHeight = barrelCenterHeight;
            }
        }

        sealed class GeometryBuffer
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public void AddEllipsoid(Vector3 center, Vector3 radii, int segments, int latitudeSegments)
            {
                int start = vertices.Count;
                for (int lat = 0; lat <= latitudeSegments; lat++)
                {
                    float phi = Mathf.PI * lat / latitudeSegments;
                    for (int lon = 0; lon < segments; lon++)
                    {
                        float theta = Mathf.PI * 2f * lon / segments;
                        vertices.Add(center + new Vector3(radii.x * Mathf.Sin(phi) * Mathf.Cos(theta), radii.y * Mathf.Cos(phi), radii.z * Mathf.Sin(phi) * Mathf.Sin(theta)));
                    }
                }
                for (int lat = 0; lat < latitudeSegments; lat++)
                    for (int lon = 0; lon < segments; lon++)
                    {
                        int a = start + lat * segments + lon;
                        int b = start + lat * segments + (lon + 1) % segments;
                        int c = a + segments;
                        int d = start + (lat + 1) * segments + (lon + 1) % segments;
                        AddTriangle(a, b, c); AddTriangle(b, d, c);
                    }
            }

            public void AddTetrahedron(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                AddTriangle(start, start + 2, start + 1);
                AddTriangle(start, start + 1, start + 3);
                AddTriangle(start + 1, start + 2, start + 3);
                AddTriangle(start + 2, start, start + 3);
            }

            public void AddBox(Vector3 center, Vector3 size, Quaternion rotation)
            {
                int start = vertices.Count;
                Vector3 h = size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    vertices.Add(center + rotation * p);
                }
                int[] faces = { 0,2,1, 1,2,3, 4,5,6, 5,7,6, 0,1,4, 1,5,4, 2,6,3, 3,6,7, 0,4,2, 2,4,6, 1,3,5, 3,7,5 };
                for (int i = 0; i < faces.Length; i += 3) AddTriangle(start + faces[i], start + faces[i + 1], start + faces[i + 2]);
            }

            public void AddCylinder(Vector3 a, Vector3 b, float radius, int segments)
            {
                Vector3 axis = (b - a).normalized;
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
                Vector3 v = Vector3.Cross(axis, u).normalized;
                int start = vertices.Count;
                for (int end = 0; end < 2; end++)
                {
                    Vector3 center = end == 0 ? a : b;
                    for (int i = 0; i < segments; i++)
                    {
                        float angle = Mathf.PI * 2f * i / segments;
                        vertices.Add(center + radius * (u * Mathf.Cos(angle) + v * Mathf.Sin(angle)));
                    }
                }
                int capA = vertices.Count; vertices.Add(a);
                int capB = vertices.Count; vertices.Add(b);
                for (int i = 0; i < segments; i++)
                {
                    int n = (i + 1) % segments;
                    AddTriangle(start + i, start + n, start + segments + i);
                    AddTriangle(start + n, start + segments + n, start + segments + i);
                    AddTriangle(capA, start + n, start + i);
                    AddTriangle(capB, start + segments + i, start + segments + n);
                }
            }

            public void AddLatheZ(float[] z, float[] radius, float centerY, int segments)
            {
                int start = vertices.Count;
                for (int row = 0; row < z.Length; row++)
                    for (int i = 0; i < segments; i++)
                    {
                        float a = Mathf.PI * 2f * i / segments;
                        vertices.Add(new Vector3(radius[row] * Mathf.Cos(a), centerY + radius[row] * Mathf.Sin(a), z[row]));
                    }
                for (int row = 0; row < z.Length - 1; row++)
                    for (int i = 0; i < segments; i++)
                    {
                        int n = (i + 1) % segments;
                        int a = start + row * segments + i, b = start + row * segments + n;
                        int c = a + segments, d = start + (row + 1) * segments + n;
                        AddTriangle(a, b, c); AddTriangle(b, d, c);
                    }
            }

            public void AddDiscZ(float z, float centerY, float radius, int segments, bool towardPositiveZ)
            {
                int center = vertices.Count; vertices.Add(new Vector3(0, centerY, z));
                int ring = vertices.Count;
                for (int i = 0; i < segments; i++)
                {
                    float a = Mathf.PI * 2f * i / segments;
                    vertices.Add(new Vector3(radius * Mathf.Cos(a), centerY + radius * Mathf.Sin(a), z));
                }
                for (int i = 0; i < segments; i++)
                {
                    int next = ring + (i + 1) % segments;
                    if (towardPositiveZ) AddTriangle(center, ring + i, next);
                    else AddTriangle(center, next, ring + i);
                }
            }

            public void AddWheelRim(Vector3 center, float outerRadius, float innerRadius, float width, int segments)
            {
                int start = vertices.Count;
                // Axial layers x inner/outer radii form a solid annular tire with a low-poly bevel.
                float[] x = { -width * 0.5f, width * 0.5f };
                float[] r = { innerRadius, outerRadius };
                for (int layer = 0; layer < 2; layer++)
                    for (int radiusIndex = 0; radiusIndex < 2; radiusIndex++)
                        for (int i = 0; i < segments; i++)
                        {
                            float a = Mathf.PI * 2f * i / segments;
                            vertices.Add(center + new Vector3(x[layer], r[radiusIndex] * Mathf.Sin(a), r[radiusIndex] * Mathf.Cos(a)));
                        }
                int stride = segments;
                for (int i = 0; i < segments; i++)
                {
                    int n = (i + 1) % segments;
                    int inner0 = start + i, outer0 = start + stride + i;
                    int inner0N = start + n, outer0N = start + stride + n;
                    int inner1 = start + 2 * stride + i, outer1 = start + 3 * stride + i;
                    int inner1N = start + 2 * stride + n, outer1N = start + 3 * stride + n;
                    // Side annuli, outer tire and inner lip close the ring without duplicate objects.
                    AddTriangle(inner0, outer0, inner0N); AddTriangle(inner0N, outer0, outer0N);
                    AddTriangle(inner1, inner1N, outer1); AddTriangle(inner1N, outer1N, outer1);
                    AddTriangle(outer0, outer1, outer0N); AddTriangle(outer0N, outer1, outer1N);
                    AddTriangle(inner0, inner0N, inner1); AddTriangle(inner0N, inner1N, inner1);
                }
            }

            void AddTriangle(int a, int b, int c) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }

            public Mesh ToMesh(string meshName)
            {
                var mesh = new Mesh { name = meshName };
                if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, true);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
