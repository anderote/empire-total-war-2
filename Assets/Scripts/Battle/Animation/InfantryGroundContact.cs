using UnityEngine;
namespace EmpireBattle
{
    public static class InfantryGroundContact
    {
        // Cache only fully settled poses. Movement, kicks and local craters invalidate
        // the result; animated falling bodies continue to fit the terrain each frame.
        public struct RestingPoseCache
        {
            bool ready;
            Vector3 position;
            Quaternion facing, result;
            int revision;
            public Quaternion Sample(BattlefieldTerrain terrain,Vector3 point,Quaternion direction,float amount)
            {
                if(!BattleOptimizationSettings.RenderWorkOptimizations||amount<1f)
                    return Rotation(terrain,point,direction,amount);
                bool same=ready&&point.x==position.x&&point.y==position.y&&point.z==position.z
                    &&direction.x==facing.x&&direction.y==facing.y&&direction.z==facing.z&&direction.w==facing.w;
                if(same&&terrain.DamageRevision!=revision)
                    same=!terrain.DamageChangedIn(new Bounds(point,new Vector3(.902f,0,.902f)),revision);
                if(!same){result=Rotation(terrain,point,direction);position=point;facing=direction;ready=true;}
                revision=terrain.DamageRevision;
                return result;
            }
        }
        // Fit the resting body to the local terrain tangent, including crater slopes.
        public static Quaternion Rotation(BattlefieldTerrain terrain,Vector3 position,Quaternion facing,float amount=1f)
        {
            if(amount<=0f)return facing;
            const float radius=.45f;
            var p=new Vector2(position.x,position.z);
            float dx=terrain.HeightAt(p+Vector2.right*radius)-terrain.HeightAt(p-Vector2.right*radius);
            float dz=terrain.HeightAt(p+Vector2.up*radius)-terrain.HeightAt(p-Vector2.up*radius);
            var normal=new Vector3(-dx,2f*radius,-dz).normalized;
            return Quaternion.Slerp(facing,Quaternion.FromToRotation(Vector3.up,normal)*facing,Mathf.Clamp01(amount));
        }
    }
}
