/*
Archers now shoot on ships for a more interesting naval warfare.

Author: cmjten10 (https://steamcommunity.com/id/cmjten10/)
Mod Version: 1
Target K&C Version: 117r5s-mods
Date: 2020-05-04
*/
using Harmony;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace ArchersShootOnShips
{
    public class ArchersShootOnShipsMod : MonoBehaviour 
    {
        public static KCModHelper helper;

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            var harmony = HarmonyInstance.Create("harmony");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
        
        // Returns true if the army squad is moving, false otherwise. Takes into account being on a ship. In this case, 
        // the army squad is not moving.
        private static bool IsArmyMoving(UnitSystem.Army army)
        {
            // army.locked currently being used only for Transport Ship. Locked if on ship, unlocked otherwise.
            return !army.locked && army.moving;
        }

        // ArcherGeneral::Update patch for replacing condition for shooting.
        [HarmonyPatch(typeof(ArcherGeneral))]
        [HarmonyPatch("Update")]
        public static class ShootOnShipsPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    OpCode opcode = instruction.opcode;
                    System.Object operand = instruction.operand;

                    // Looks for field access to UnitSystem.Army.moving and replaces it with a function call that 
                    // evaluates the UnitSystem.Army already on the stack. This function returns whether an army squad 
                    // is moving or not, but takes into account being on a ship. In this case, the army squad is not
                    // moving.
                    if (opcode == OpCodes.Ldfld && operand != null && operand.ToString() == "System.Boolean moving")
                    {
                        MethodInfo method_IsArmyMoving = AccessTools.Method(typeof(ArchersShootOnShipsMod),
                            nameof(IsArmyMoving));
                        CodeInstruction newInstruction = new CodeInstruction(OpCodes.Call, method_IsArmyMoving);
                        yield return newInstruction;
                    }
                    else 
                    {
                        yield return instruction;
                    }
                }
            }
        }
    }
}
