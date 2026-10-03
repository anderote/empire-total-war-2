using EmpireBattle;

namespace Empire.UI
{
    public interface IRegimentProcurementSource
    {
        bool HasHostileProcurement {get;}
        RegimentAction RaidEquipment(string[] ids,EquipmentItem item);
    }
}
