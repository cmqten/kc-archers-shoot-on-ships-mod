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
        
        // Returns false if archer can shoot, true otherwise.
        private static bool CanArcherNotShoot(UnitSystem.Army army)
        {
            bool onShip = true;
            if (!onShip)
            {
                return army.moving;
            }
            else
            {
                return false;
            }
        }

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
                    // evaluates the UnitSystem.Army already on the stack, then returns false if the unit can shoot, 
                    // true otherwise.
                    if (opcode == OpCodes.Ldfld && operand != null && operand.ToString() == "System.Boolean moving")
                    {
                        MethodInfo method_ReturnFalse = AccessTools.Method(typeof(ArchersShootOnShipsMod), 
                            nameof(CanArcherNotShoot));
                        CodeInstruction newInstruction = new CodeInstruction(OpCodes.Call, method_ReturnFalse);
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