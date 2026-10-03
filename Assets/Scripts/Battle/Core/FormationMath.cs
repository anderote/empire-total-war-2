using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Allocation-free formation geometry. Facing zero points along world +Z.</summary>
    public static class FormationMath
    {
        // Lines cycle through at most four ranks; columns retain two firing ranks.
        public static int FiringSoldierCount(BattalionState unit)
        {
            if (unit.Formation == FormationKind.Skirmish) return unit.AliveCount;
            if (unit.Formation == FormationKind.Square)
            {
                int count=0;for(int slot=0;slot<unit.AliveCount;slot++)if(IsFiringSlot(unit,slot))count++;
                return count;
            }
            return Mathf.Min(unit.AliveCount, EffectiveFiles(unit) * (unit.Formation == FormationKind.Line ? 4 : 2));
        }

        public static bool CanResizeFrontage(BattalionState unit) => unit.Formation != FormationKind.Square && unit.Formation != FormationKind.Diamond;
        public static int ClampFrontage(BattalionState unit, int files)
        {
            int minimum = Mathf.Min(4, unit.AliveCount);
            int maximum = Mathf.Max(minimum, Mathf.CeilToInt(unit.AliveCount / 2f));
            return Mathf.Clamp(files, minimum, maximum);
        }
        public static int DiamondRadius(int soldiers) => Mathf.Max(0,Mathf.CeilToInt((Mathf.Sqrt(2*Mathf.Max(1,soldiers)-1)-1)*.5f));
        // Centered rows widen from the leading point, then narrow toward the rear.
        public static Vector2 DiamondSlotOffset(int slot,int soldiers,float spacing,float facing)
        {
            int radius=DiamondRadius(soldiers),capacity=1+2*radius*(radius+1),before=0;
            for(int row=0;row<=2*radius;row++)
            {
                int width=1+2*(radius-Mathf.Abs(radius-row));
                int start=Mathf.RoundToInt(before*soldiers/(float)capacity);before+=width;
                int end=Mathf.RoundToInt(before*soldiers/(float)capacity);
                if(slot<end)return Right(facing)*((slot-start-(end-start-1)*.5f)*spacing)
                    +Forward(facing)*((radius-row)*RankSpacing(spacing));
            }
            return Vector2.zero;
        }

        public static int EffectiveFiles(BattalionState unit) => Mathf.Max(1, Mathf.Min(unit.Files, unit.AliveCount));
        public static float AngleDifference(float current, float target)
        {
            float delta=Mathf.Repeat(target-current+Mathf.PI,Mathf.PI*2f)-Mathf.PI;
            // An exact about-face always takes the same side, including Burst's tiny
            // rounding differences around the +/- pi wrap boundary.
            return Mathf.Abs(Mathf.Abs(delta)-Mathf.PI)<.00001f ? -Mathf.PI : delta;
        }
        // Match the root-relative foot travel / planted fraction in ProceduralInfantryRig.
        // This changes animation cadence only, never a soldier's movement speed.
        public static float SoldierStride(int soldier,bool running,float speed=float.PositiveInfinity,bool charging=false,bool command=false,bool expanded=true)
        {
            if(command)return .65f/.62f; // Command roles carry equipment in their march cycle.
            int variant=expanded?Mathf.Min(2,(int)(RifleFireTiming.Variation(soldier,71)*3)):0;
            float travel=charging?.78f:running?(expanded&&speed<1.65f?.70f:.78f):expanded&&speed<.85f?.55f:.65f;
            float stance=charging||running?.48f:.62f;
            return travel/stance*(1+(variant-1)*.06f);
        }

        public static bool ExpandedInfantryGaits(BattalionState unit)
        {
            string faction=UnitRoster.ArtFaction(unit.Faction);
            return (faction=="france"||faction=="britain")&&!UnitRoster.Light(unit.Role)
                &&unit.Role!=UnitRole.Swordsman&&!EmpireBattle.Art.CartridgeRifleLibrary.UsesCartridges(unit.Weapon);
        }

        public static float RankSpacing(float fileSpacing) => fileSpacing>1.4f&&fileSpacing<2f?3.2f:Mathf.Max(.9f,fileSpacing);

        public static Vector2 Forward(float facing) => new Vector2(Mathf.Sin(facing), Mathf.Cos(facing));
        public static Vector2 Right(float facing) => new Vector2(Mathf.Cos(facing), -Mathf.Sin(facing));
        public static int FileCount(int soldiers, FormationKind formation)
        {
            if (formation == FormationKind.Diamond) return DiamondRadius(soldiers)*2+1;
            if (formation == FormationKind.Line) return Mathf.Max(1, Mathf.CeilToInt(soldiers / 4f));
            if (formation == FormationKind.Skirmish) return Mathf.Max(1, Mathf.CeilToInt(soldiers / 3f));
            if (formation == FormationKind.Square) return Mathf.CeilToInt(Mathf.CeilToInt(soldiers / 4f) / (float)SquareRanks(soldiers)) + SquareRanks(soldiers)-1;
            return Mathf.Min(8, Mathf.Max(1, soldiers));
        }
        public static Vector2 SlotOffset(int slot, int soldiers, int files, float spacing, float facing)
        {
            return SlotOffset(slot, soldiers, files, spacing, Right(facing), Forward(facing));
        }
        // Callers updating a whole formation can reuse its basis rather than repeat trig per soldier.
        public static Vector2 SlotOffset(int slot, int soldiers, int files, float spacing, Vector2 right, Vector2 forward)
        {
            files = Mathf.Max(1, files);
            int rows = Mathf.CeilToInt(soldiers / (float)files);
            int rowFiles = Mathf.Min(files,soldiers-(slot/files)*files);
            float x = (slot % files - (rowFiles - 1) * .5f) * spacing;
            float z = ((rows - 1) * .5f - slot / files) * RankSpacing(spacing);
            return right * x + forward * z;
        }
        public static int SquareSide(int slot, int soldiers) => Mathf.Min(3, slot * 4 / Mathf.Max(1, soldiers));
        static int SideStart(int side, int soldiers) => (side * soldiers + 3) / 4;
        public static int SquareRanks(int soldiers) => soldiers >= 96 ? 3 : soldiers >= 32 ? 2 : 1;
        public static int MaximumSquareRanks(int soldiers) => Mathf.Max(1,Mathf.FloorToInt(Mathf.Sqrt(Mathf.Max(1,soldiers/4))));
        public static int SquareRanks(BattalionState unit) => Mathf.Clamp(unit.SquareDepth > 0 ? unit.SquareDepth : SquareRanks(unit.AliveCount),1,MaximumSquareRanks(unit.AliveCount));
        public static int RankCount(BattalionState unit) => unit.Formation == FormationKind.Diamond ? DiamondRadius(unit.AliveCount)*2+1 : unit.Formation == FormationKind.Square ? SquareRanks(unit)
            : Mathf.CeilToInt(unit.AliveCount/(float)EffectiveFiles(unit));
        static int SquareFiles(int sideCount, int ranks, int rank)
        {
            int outer = Mathf.CeilToInt(sideCount / (float)ranks) + ranks - 1;
            int available = outer - 2 * rank;
            int before = rank * outer - rank * (rank - 1);
            return Mathf.Max(0,Mathf.Min(available,sideCount-before));
        }
        public static int SquareRank(BattalionState unit, int slot)
        {
            int side=SquareSide(slot,unit.AliveCount), local=slot-SideStart(side,unit.AliveCount);
            int sideCount=SideStart(side+1,unit.AliveCount)-SideStart(side,unit.AliveCount), ranks=SquareRanks(unit);
            for(int rank=0;rank<ranks;rank++)
            { int files=SquareFiles(sideCount,ranks,rank);if(local<files)return rank;local-=files; }
            return ranks-1;
        }
        public static bool IsBayonetWall(BattalionState unit, int slot) => unit.Formation == FormationKind.Square && SquareRank(unit,slot) == 0;
        public static bool IsFiringSlot(BattalionState unit, int slot) => unit.Formation == FormationKind.Square
            ? SquareRank(unit,slot) <= 1
            : unit.Formation == FormationKind.Skirmish || slot < FiringSoldierCount(unit);
        public static float SlotFacing(BattalionState unit, int slot) =>
            unit.Facing + (unit.Formation == FormationKind.Square ? SquareSide(slot, unit.AliveCount) * Mathf.PI * .5f : 0f);
        public static Vector2 SlotOffset(BattalionState unit, int slot) => SlotOffset(unit, slot, unit.Files, unit.Facing);
        public static Vector2 SlotOffset(BattalionState unit, int slot, int files, float facing)
        {
            if(unit.ReviewRankSpacing>0)
            {
                int rows=Mathf.CeilToInt(unit.SoldierCount/(float)Mathf.Max(1,files));
                return Right(facing)*((slot%files-(files-1)*.5f)*unit.Spacing)
                    +Forward(facing)*(((rows-1)*.5f-slot/files)*unit.ReviewRankSpacing);
            }
            if (unit.Formation == FormationKind.Diamond) return DiamondSlotOffset(slot,unit.AliveCount,unit.Spacing,facing);
            if (unit.Formation != FormationKind.Square)
                return SlotOffset(slot, unit.AliveCount, files, unit.Spacing, facing);
            return SquareSlotOffset(slot, unit.AliveCount, SquareRanks(unit), unit.Spacing, facing);
        }
        public static Vector2 SquareSlotOffset(int slot, int soldiers, int ranks, float spacing, float facing)
        {
            if (soldiers == 1) return Vector2.zero;
            int side = SquareSide(slot, soldiers), local = slot - SideStart(side, soldiers);
            int sideCount = SideStart(side + 1, soldiers) - SideStart(side, soldiers);
            int rank=0;
            while(rank<ranks-1 && local>=SquareFiles(sideCount,ranks,rank))
            { local-=SquareFiles(sideCount,ranks,rank);rank++; }
            int sideFiles=SquareFiles(sideCount,ranks,rank);
            int maxFiles=SquareFiles(Mathf.CeilToInt(soldiers/4f),ranks,0);
            float half=(maxFiles+1)*spacing*.5f;
            float angle=facing+side*Mathf.PI*.5f;
            // Each inner perimeter is shorter, keeping adjoining sides out of each
            // other's corner cells instead of stacking soldiers at the intersections.
            return Right(angle)*((local-(sideFiles-1)*.5f)*spacing)
                + Forward(angle)*(half-rank*spacing);
        }
        // Balance values for the prototype; cavalry is represented by UnitKind in combat.
        public static float FireExposure(BattalionState unit) => unit.Formation == FormationKind.Skirmish ? .55f : 1f;
        public static float CavalryExposure(BattalionState unit) => unit.Formation == FormationKind.Skirmish ? 1.8f :
            unit.Formation == FormationKind.Square && !unit.Moving && !unit.Reforming ? .2f : 1f;
        public static Vector2 HalfExtents(BattalionState battalion)
        {
            if (battalion.Formation == FormationKind.Diamond)
                return new Vector2(Mathf.Max(1,(DiamondRadius(battalion.AliveCount)+.5f)*battalion.Spacing),Mathf.Max(1,(DiamondRadius(battalion.AliveCount)+.5f)*RankSpacing(battalion.Spacing)));
            if (battalion.Formation == FormationKind.Square)
            {
                int ranks = SquareRanks(battalion);
                int sideFiles = SquareFiles(Mathf.CeilToInt(battalion.AliveCount / 4f),ranks,0);
                return Vector2.one * Mathf.Max(1f, (sideFiles + 2) * battalion.Spacing * .5f);
            }
            int files = EffectiveFiles(battalion);
            int rows = Mathf.CeilToInt(battalion.AliveCount / (float)files);
            return new Vector2(Mathf.Max(1f, files * battalion.Spacing * .5f), Mathf.Max(1f, rows * RankSpacing(battalion.Spacing) * .5f));
        }
        public static float TurnTowards(float current, float desired, float maxRadians)
        {
            float delta = AngleDifference(current,desired);
            return current + Mathf.Clamp(delta, -maxRadians, maxRadians);
        }
        public static float FacingOf(Vector2 direction) => Mathf.Atan2(direction.x, direction.y);
    }
}
