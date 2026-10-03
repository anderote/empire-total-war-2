using UnityEngine;

namespace EmpireBattle.Art
{
    // A small Resources entry loads one generated national roster on demand.
    // The meshes stay in Generated and are shared, rather than copied or loaded as one global catalog.
    public sealed class UnitVisualReference : ScriptableObject
    {
        public UnitVisualLibrary Library;
    }
}
