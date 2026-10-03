using System.Collections.Generic;
using EmpireBattle.AnimationAuthoring;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Four reusable source rigs per gun. Poses and tool motion follow the simulation clock.</summary>
    public sealed class ArtilleryCrewView : MonoBehaviour
    {
        sealed class Crew
        {
            public Transform Root, Tool, Tip;
            public ProceduralInfantryRig Rig;
            public Dictionary<string, Transform> Bones = new Dictionary<string, Transform>();
            public Vector2 Local;
            public ArtilleryGun Gun;
            public int Role;
            public float GaitPhase, ServiceBlend, PushBlend;
            public bool Walking;
            public Vector3 ServiceLeft, ServiceRight;
        }
        readonly List<Crew> members = new List<Crew>(32);
        readonly List<Object> owned = new List<Object>();
        BattleWorld world;
        ArtillerySystem artillery;
        Camera cameraView;
        Material wood, sponge, shotMaterial;
        float previousTime;
        public int CrewCount => members.Count;
        public int ServiceSamples { get; private set; }

        public void Initialize(BattleWorld value, ArtillerySystem guns, Camera camera)
        {
            world = value; artillery = guns; cameraView = camera; previousTime = world.Time;
            wood = MakeMaterial(new Color(.33f, .23f, .12f));
            sponge = MakeMaterial(new Color(.19f, .16f, .12f));
            shotMaterial = MakeMaterial(new Color(.08f, .085f, .09f));
            foreach (var gun in guns.Guns)
            {
                var prefab = Resources.Load<GameObject>("Artillery/" + gun.Side + "Crew");
                if (!prefab) { Debug.LogWarning("Artillery crew prefab missing: run ArtilleryBuild.PrepareCrewAssets."); continue; }
                for (int role = 0; role < 4; role++)
                {
                    var root = Instantiate(prefab, transform);
                    root.name = gun.Side + " gun " + gun.Id + " / " + new[] { "Sponge and rammer", "Loader", "Run-up assistant", "Gunner" }[role];
                    var crew = new Crew { Root = root.transform, Gun = gun, Role = role, Local = gun.Crew[role].Local, Rig = new ProceduralInfantryRig(root) };
                    crew.Root.position=world.Terrain.SurfacePoint(gun.Position+FormationMath.Right(gun.Facing)*crew.Local.x+FormationMath.Forward(gun.Facing)*crew.Local.y);
                    crew.Root.rotation=Quaternion.Euler(0,gun.Facing*Mathf.Rad2Deg+180,0);
                    foreach (var bone in crew.Rig.Bones) crew.Bones[bone.name] = bone;
                    if (role == 0)
                    {
                        crew.Tool = Prop(PrimitiveType.Cylinder, root.transform, wood, "Sponge rammer staff");
                        crew.Tip = Prop(PrimitiveType.Cylinder, root.transform, sponge, "Sponge head");
                    }
                    if (role == 1) crew.Tool = Prop(PrimitiveType.Sphere, root.transform, shotMaterial, "Carried round");
                    if (role == 3) crew.Tool = Prop(PrimitiveType.Cylinder, root.transform, wood, "Linstock");
                    members.Add(crew);
                }
            }
        }
        Material MakeMaterial(Color color)
        {
            var material = new Material(Resources.Load<Material>("CannonLit"));
            material.SetColor("_BaseColor", color); owned.Add(material); return material;
        }
        static Transform Prop(PrimitiveType kind, Transform parent, Material material, string name)
        {
            var go = GameObject.CreatePrimitive(kind); go.name = name;
            go.transform.SetParent(parent, false);
            if(Application.isPlaying)Destroy(go.GetComponent<Collider>());else DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material; return go.transform;
        }
        void LateUpdate()
        { Sample(); }

        // Also sampled by the offline animation review, using the same simulation clock.
        public void Sample()
        {
            if (world == null) return;
            float dt = Mathf.Clamp(world.Time - previousTime, 0, .25f); previousTime = world.Time;
            foreach (var c in members)
            {
                var gun = c.Gun;
                var member=gun.Crew[c.Role];
                if(!member.Alive)
                {
                    c.Root.position=member.DeathPosition;
                    c.Root.rotation=Quaternion.Euler(0,member.DeathFacing*Mathf.Rad2Deg+180,0);
                    bool corpseVisible=!cameraView||(cameraView.transform.position-c.Root.position).sqrMagnitude<350f*350f;
                    c.Root.gameObject.SetActive(corpseVisible);
                    if(c.Tool)c.Tool.gameObject.SetActive(false);
                    if(c.Tip)c.Tip.gameObject.SetActive(false);
                    if(corpseVisible)c.Rig.Sample(InfantryMotionKind.Death,Mathf.Clamp01((world.Time-member.DeathTime)/1.25f),(gun.Id*4+c.Role)%10);
                    continue;
                }
                Quaternion facing = Quaternion.Euler(0, gun.Facing * Mathf.Rad2Deg, 0);
                bool work = gun.Operational&&!gun.Suppressed&&!gun.Relocating&&(c.Role == 0 && (gun.Phase == ArtilleryPhase.Sponging || gun.Phase == ArtilleryPhase.Ramming)
                    || c.Role == 1 && gun.Phase == ArtilleryPhase.Loading);
                bool pushing = gun.Operational&&!gun.Suppressed&&!gun.Relocating&&gun.Phase == ArtilleryPhase.RunUp && c.Role >= 2;
                Vector2 station = ArtillerySystem.CrewStation(gun,c.Role);
                Vector2 before = c.Local;
                c.Local = member.Local;
                Vector3 previousPosition=c.Root.position;
                c.Root.position = artillery.CrewPosition(gun,c.Role);
                Vector3 movement=c.Root.position-previousPosition;movement.y=0;
                float travel=movement.magnitude;
                c.GaitPhase=Mathf.Repeat(c.GaitPhase+travel/(.55f/.62f),1);
                if(dt>0)c.Walking=(c.Local-station).sqrMagnitude>.0025f||travel>.001f;
                bool walking=c.Walking;
                Vector3 look = walking ? (travel>.001f?movement:facing*new Vector3(station.x-before.x,0,station.y-before.y))
                    : work ? artillery.Muzzle(gun) - c.Root.position : facing * Vector3.forward;
                look.y = 0;
                // Source rig's face points toward -Z.
                if (look.sqrMagnitude > .000001f) c.Root.rotation = Quaternion.RotateTowards(c.Root.rotation,Quaternion.LookRotation(-look.normalized),dt*360);
                bool visible = !cameraView || (cameraView.transform.position - c.Root.position).sqrMagnitude < 350f * 350f;
                c.Root.gameObject.SetActive(visible);
                if (!visible) continue;
                if (walking) c.Rig.Sample(InfantryMotionKind.Walk,c.GaitPhase,1);
                else c.Rig.Sample(InfantryMotionKind.Idle,world.Time/6+c.Role*.2f,0);
                if(gun.Relocating)
                {
                    if(c.Tool)c.Tool.gameObject.SetActive(false);
                    if(c.Tip)c.Tip.gameObject.SetActive(false);
                    continue;
                }
                if(c.Tool&&c.Role!=1)c.Tool.gameObject.SetActive(true);
                float p = gun.PhaseProgress;
                Vector3 muzzle = artillery.Muzzle(gun);
                Vector3 restLeft=c.Root.TransformPoint(new Vector3(.22f,.88f,-.12f));
                Vector3 restRight=c.Root.TransformPoint(new Vector3(-.22f,.88f,-.12f));
                c.ServiceBlend=Mathf.MoveTowards(c.ServiceBlend,work&&!walking?1:0,dt/.2f);
                c.PushBlend=Mathf.MoveTowards(c.PushBlend,pushing&&(c.Local-station).sqrMagnitude<.0225f?1:0,dt/.2f);
                c.Rig.LeanIntoWork(Mathf.Max(c.ServiceBlend,c.PushBlend));
                // Loading can start after the carried shot was hidden in Ready/Ramming.
                // Set visibility on every path, including arrival at the muzzle.
                if(c.Role==1)c.Tool.gameObject.SetActive(gun.Phase==ArtilleryPhase.Sponging||gun.Phase==ArtilleryPhase.Loading&&p<.82f);
                if (work && !walking)
                {
                    if (dt > 0) ServiceSamples++;
                    Vector3 direction = facing * (Quaternion.Euler(-gun.Elevation, 0, 0) * Vector3.forward);
                    float stroke = c.Role==1 ? Mathf.Lerp(.5f,.08f,Mathf.SmoothStep(0,1,p)) : .25f + .45f * (.5f - .5f * Mathf.Cos(p * Mathf.PI * 6));
                    Vector3 grip = muzzle + direction * stroke;
                    c.ServiceLeft=grip+facing*Vector3.left*.035f;
                    c.ServiceRight=grip+direction*.25f;
                    Hand(c, "l", Vector3.Lerp(restLeft,c.ServiceLeft,c.ServiceBlend));
                    Hand(c, "r", Vector3.Lerp(restRight,c.ServiceRight,c.ServiceBlend));
                    if (c.Role == 0)
                    {
                        PlaceRod(c.Tool, muzzle - direction * (.6f - stroke), muzzle + direction * (1f + stroke), .025f);
                        c.Tip.gameObject.SetActive(gun.Phase == ArtilleryPhase.Sponging);
                        PlaceRod(c.Tip, muzzle - direction * (.6f - stroke), muzzle - direction * (.43f - stroke), .085f);
                    }
                    else
                    {
                        // Deposit one round; the loader does not mimic the rammer's repeated strokes.
                        Vector3 carried=(c.Bones["l_hand"].position+c.Bones["r_hand"].position)*.5f;
                        c.Tool.position=carried;c.Tool.localScale=Vector3.one*.2f;
                    }
                }
                else
                {
                    // Relaxed hands instead of the infantry's musket-holding pose.
                    Hand(c, "l", Vector3.Lerp(restLeft,c.ServiceLeft,c.ServiceBlend));
                    Hand(c, "r", Vector3.Lerp(restRight,c.ServiceRight,c.ServiceBlend));
                    if (c.Role == 0)
                    {
                        Vector3 hand = c.Bones["r_hand"].position;
                        PlaceRod(c.Tool, hand - Vector3.up * .65f, hand + Vector3.up * .95f, .025f);
                        c.Tip.gameObject.SetActive(true);
                        PlaceRod(c.Tip, hand + Vector3.up * .8f, hand + Vector3.up * .97f, .085f);
                    }
                    if (c.Role == 1)
                    {
                        c.Tool.position = c.Bones["r_hand"].position; c.Tool.localScale = Vector3.one * .2f;
                    }
                    if (pushing)
                    {
                        Vector3 contact = artillery.GunCenter(gun) + facing * new Vector3(c.Role == 2 ? -.45f : .45f, .65f, -.8f);
                        Hand(c, "l", Vector3.Lerp(restLeft,contact-facing*Vector3.right*.12f,c.PushBlend));
                        Hand(c, "r", Vector3.Lerp(restRight,contact+facing*Vector3.right*.12f,c.PushBlend));
                    }
                }
                if (c.Role == 3)
                {
                    Vector3 hand = c.Bones["r_hand"].position;
                    Vector3 end = hand + facing * Vector3.forward * 1.2f;
                    if (gun.Phase == ArtilleryPhase.Aiming || gun.Phase == ArtilleryPhase.Firing)
                        end = artillery.GunCenter(gun) + facing * new Vector3(0, .98f, -.9f);
                    PlaceRod(c.Tool, hand, end, .018f);
                }
            }
        }
        static void PlaceRod(Transform rod, Vector3 a, Vector3 b, float radius)
        {
            rod.position = (a + b) * .5f; rod.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            rod.localScale = new Vector3(radius * 2, Vector3.Distance(a, b) * .5f, radius * 2);
        }
        static void Hand(Crew c, string side, Vector3 target)
        {
            Transform upper = c.Bones[side + "_upperarm_twist"], elbow = c.Bones[side + "_foretwist"], hand = c.Bones[side + "_hand"];
            Vector3 shoulder = upper.position;
            float a = Vector3.Distance(shoulder, elbow.position), b = Vector3.Distance(elbow.position, hand.position);
            Vector3 axis = (target - shoulder).normalized;
            float d = Mathf.Clamp(Vector3.Distance(shoulder, target), Mathf.Abs(a - b) + .001f, a + b - .001f);
            float along = (a * a + d * d - b * b) / (2 * d);
            Vector3 pole = c.Root.TransformDirection(new Vector3(side == "l" ? 1 : -1, -.35f, 0));
            Vector3 bend = Vector3.ProjectOnPlane(pole, axis).normalized;
            Vector3 goal = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(elbow.position - shoulder, goal - shoulder) * upper.rotation;
            elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position, shoulder + axis * d - elbow.position) * elbow.rotation;
        }
        void OnDestroy() { foreach (var item in owned) if (item) {if(Application.isPlaying)Destroy(item);else DestroyImmediate(item);} }
    }
}
