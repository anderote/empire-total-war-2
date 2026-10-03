using UnityEngine;

namespace EmpireBattle
{
    public sealed partial class BattleView
    {
        public enum CommandCursorKind { Default, Move, Attack, Melee }
        Texture2D moveCursor, attackCursor, meleeCursor;
        CommandCursorKind activeCommandCursor;
        internal Vector2? ReviewCommandPointer;

        int CommandAttackTarget(Vector2 mouse,bool frontage)
            => frontage?-1:PickBattalion(mouse,ArmySide.British,true);

        public CommandCursorKind AttackCursorFor(int target)
        {
            bool force=Input.GetKey(KeyCode.LeftAlt)||Input.GetKey(KeyCode.RightAlt);
            foreach(var unit in world.Battalions)
                if(selected[unit.Id]&&!unit.Routing&&unit.AliveCount>0&&UsesMeleeAttack(unit,target,force))return CommandCursorKind.Melee;
            return CommandCursorKind.Attack;
        }

        // Use the same picking and frontage rules as releasing a right-click order.
        public CommandCursorKind CommandCursorAt(Vector2 mouse)
        {
            if(world==null||!EnableInput||!HasCommandSelection||selecting||Input.GetMouseButton(2))
                return CommandCursorKind.Default;
            if(mouse.x<0||mouse.y<0||mouse.x>=Screen.width||mouse.y>=Screen.height||
               PointerOverUI(new Vector2(mouse.x,Screen.height-mouse.y)))return CommandCursorKind.Default;
            bool onGround=GroundPoint(mouse,out var ground);
            bool frontage=ordering&&Vector2.Distance(mouse,rightStart)>10&&onGround;
            int target=CommandAttackTarget(mouse,frontage);
            if(target>=0)return AttackCursorFor(target);
            if(!onGround)return CommandCursorKind.Default;
            return SelectedGunCount>0&&(GunBombardModifier||commandArtillery.BatteryAt(ArmySide.French,ground)>=0)
                ?CommandCursorKind.Attack:CommandCursorKind.Move;
        }

        void UpdateCommandCursor()
        {
            var kind=Application.isFocused||ReviewCommandPointer.HasValue?CommandCursorAt(ReviewCommandPointer??(Vector2)Input.mousePosition):CommandCursorKind.Default;
            if(kind==activeCommandCursor)return;
            if(kind!=CommandCursorKind.Default&&!moveCursor)
            {
                moveCursor=CreateCommandCursor(CommandCursorKind.Move);attackCursor=CreateCommandCursor(CommandCursorKind.Attack);meleeCursor=CreateCommandCursor(CommandCursorKind.Melee);
                owned.Add(moveCursor);owned.Add(attackCursor);owned.Add(meleeCursor);
            }
            Cursor.SetCursor(kind==CommandCursorKind.Move?moveCursor:kind==CommandCursorKind.Attack?attackCursor:kind==CommandCursorKind.Melee?meleeCursor:null,
                CursorHotspot(kind),CursorMode.Auto);
            activeCommandCursor=kind;
        }

        void DrawReviewCommandCursor()
        {
            if(!ReviewCommandPointer.HasValue||activeCommandCursor==CommandCursorKind.Default)return;
            Vector2 point=ReviewCommandPointer.Value;
            Vector2 hotspot=CursorHotspot(activeCommandCursor);
            GUI.DrawTexture(new Rect(point.x-hotspot.x,Screen.height-point.y-hotspot.y,32,32),
                activeCommandCursor==CommandCursorKind.Move?moveCursor:activeCommandCursor==CommandCursorKind.Melee?meleeCursor:attackCursor);
        }

        void RestoreCommandCursor()
        {
            if(activeCommandCursor==CommandCursorKind.Default)return;
            Cursor.SetCursor(null,Vector2.zero,CursorMode.Auto);
            activeCommandCursor=CommandCursorKind.Default;
        }
        void OnDisable() => RestoreCommandCursor();

        static Vector2 CursorHotspot(CommandCursorKind kind) => kind==CommandCursorKind.Move?new Vector2(6,4):
            kind==CommandCursorKind.Melee?new Vector2(25,4):kind==CommandCursorKind.Attack?new Vector2(16,16):Vector2.zero;

        static Texture2D CreateCommandCursor(CommandCursorKind kind)
        {
            bool attack=kind!=CommandCursorKind.Move;
            const int size=32;
            var shape=new bool[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                if(kind==CommandCursorKind.Melee)
                {
                    // Diagonal sword: blade tip is the hotspot, with a distinct guard and grip.
                    int diagonal=x+y;
                    shape[y*size+x]=(x>=11&&x<=25&&y>=4&&y<=19&&Mathf.Abs(diagonal-29)<=2&&y>=25-x+4)||
                        (x>=7&&x<=17&&Mathf.Abs(y-x-7)<=1)||
                        (x>=4&&x<=10&&Mathf.Abs(diagonal-29)<=1)||
                        (x>=3&&x<=6&&y>=24&&y<=27);
                }
                else if(attack)
                {
                    float radius=Vector2.Distance(new Vector2(x,y),new Vector2(16,16));
                    shape[y*size+x]=(radius>=8&&radius<=10)||
                        (Mathf.Abs(x-16)<=1&&((y>=2&&y<=10)||(y>=22&&y<=30)))||
                        (Mathf.Abs(y-16)<=1&&((x>=2&&x<=10)||(x>=22&&x<=30)))||
                        (Mathf.Abs(x-16)<=1&&Mathf.Abs(y-16)<=1);
                }
                else
                {
                    // Arrow tip is the click hotspot; the shaft points toward the destination.
                    shape[y*size+x]=y>=4&&y<=24&&x>=6&&x<=6+(y-4)*.7f||
                        y>=17&&y<=29&&x>=13+(y-17)*.4f&&x<=17+(y-17)*.4f;
                }
            }
            var pixels=new Color32[size*size];
            Color32 color=attack?new Color32(255,92,65,255):new Color32(94,215,255,255);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                bool edge=false;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    if(x+dx>=0&&x+dx<size&&y+dy>=0&&y+dy<size&&shape[(y+dy)*size+x+dx])edge=true;
                pixels[(size-1-y)*size+x]=shape[y*size+x]?color:edge?new Color32(15,21,29,255):new Color32(0,0,0,0);
            }
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false){name=kind+" command cursor",filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave};
            texture.SetPixels32(pixels);texture.Apply(false,false);
            return texture;
        }
    }
}
