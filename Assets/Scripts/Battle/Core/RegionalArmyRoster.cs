namespace EmpireBattle
{
    public static class RegionalArmyRoster
    {
        public static bool Supports(string faction)=>faction=="ming"||faction=="india";
        public static UnitRole Role(int slot)=>slot<2?UnitRole.Line:slot<4?UnitRole.Skirmisher:slot<6?UnitRole.Militia:slot==6?UnitRole.Swordsman:UnitRole.Cavalry;
        public static string Label(string faction,int slot)
        {
            bool ming=faction=="ming";
            return slot<2?(ming?"Ming musketeers":"Indian musketeers"):slot<4?(ming?"Ming light musketeers":"Indian light musketeers"):slot<6?(ming?"Ming militia":"Indian levies"):slot==6?(ming?"Ming armored swordsmen":"Indian mailed swordsmen"):(ming?"Ming cavalry":"Indian cavalry");
        }
    }
}
