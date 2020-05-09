/*
Archers now shoot on ships for a more interesting naval warfare.

Author: cmjten10 (https://steamcommunity.com/id/cmjten10/)
Mod Version: 1.1
Target K&C Version: 117r6s
Date: 2020-05-08
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
    public class ModMain : MonoBehaviour 
    {
        private const string authorName = "cmjten10";
        private const string modName = "Archers Shoot On Ships";
        private const string modNameNoSpace = "ArchersShootOnShips";
        private const string version = "v1.1";
        private static string modId = $"{authorName}.{modNameNoSpace}";

        // Logging
        private static UInt64 logId = 0;
                            
        public static KCModHelper helper;

        void Preload(KCModHelper __helper) 
        {
            helper = __helper;
            var harmony = HarmonyInstance.Create(modId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        // Logger in the log box in game.
        private static void LogInGame(string message, KingdomLog.LogStatus status = KingdomLog.LogStatus.Neutral)
        {
            KingdomLog.TryLog($"{modId}-{logId}", message, status);
            logId++; 
        }
                            
        // Returns true if the army squad is moving, false otherwise. Takes into account being on a ship. In this case, 
        // the army squad is not moving.
        private static bool IsArmyMoving(UnitSystem.Army army)
        {
            // army.locked currently being used only for Transport Ship. Locked if on ship, unlocked otherwise.
            return !army.locked && army.moving;
        }

        // Takes into account if move target is a Transport Ship. Since Transport Ship is not IProjectileHitable, this
        // returns the ShipBase, which is IProjectileHitable.
        private static System.Object GetAttackTarget(UnitSystem.Army army, ArcherGeneral archerGeneral)
        {
            if (army.locked && army.moveTarget != null)
            {
                System.Object moveTarget = army.moveTarget;
                Vector3 targetPos;
                
                // Refer to ProjectileDefense::GetTarget for getting target position.
                if (moveTarget is TroopTransportShip)
                {
                    // Prevents friendly fire.
                    if (((TroopTransportShip)moveTarget).TeamID() != 0)
                    {
                        ShipBase shipBase = ((TroopTransportShip)moveTarget).shipBase;
                        moveTarget = shipBase;
                        targetPos = shipBase.GetPos();
                    }
                    else
                    {
                        return null;
                    }
                }
                else if (moveTarget is SiegeMonster)
                {
                    SiegeMonster ogre = (SiegeMonster)moveTarget;
                    if (ogre.IsInvalid())
                    {
                        // Ogre died or got on a ship, stop tracking.
                        army.moveTarget = null;
                        return null;
                    }
                    targetPos = ogre.GetPos();
                }
                else if (moveTarget is IProjectileHitable)
                {
                    if (moveTarget is UnitSystem.Army && ((UnitSystem.Army)moveTarget).IsInvalid())
                    {
                        // Army died or got on a ship, stop tracking.
                        army.moveTarget = null;
                        return null;
                    }
                    targetPos = ((IProjectileHitable)moveTarget).GetPosition();
                }
                else
                {
                    return null;
                }
                
                // If moveTarget is not in range, returning null will make archer seek another target until moveTarget 
                // is in range. This is because moveTarget will not change until it is dead, or the player clicked on a 
                // different target.
                // Refer to ArcherGeneral::Update and ProjectileDefense::GetTarget for range calculation.
                Vector3 archerPos = army.GetPos();
                float attackRange = archerGeneral.FullRange(archerGeneral.attackRange) + 1f;
                if (!ProjectileDefense.TestRange(targetPos, archerPos, attackRange))
                { 
                    return null;
                }
                return moveTarget;
            }
            else
            {
                // If not on ship, don't change the behavior.
                return army.moveTarget;
            }
        }

        // ArcherGeneral::Update patch for replacing condition for shooting and target selection.
        [HarmonyPatch(typeof(ArcherGeneral), "Update")]
        public static class ShootOnShipsPatch
        {
            static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    OpCode opcode = instruction.opcode;
                    string operandString = instruction.operand != null ? instruction.operand.ToString() : "";

                    if (opcode == OpCodes.Ldfld && operandString == "System.Boolean moving")
                    {
                        // Looks for field access to UnitSystem.Army.moving and replaces it with a function call to
                        // IsArmyMoving with UnitSystem.Army already on the stack as the argument.
                        MethodInfo method = AccessTools.Method(typeof(ModMain), nameof(IsArmyMoving));
                        CodeInstruction newInstruction = new CodeInstruction(OpCodes.Call, method);
                        yield return newInstruction;
                    }
                    else if (opcode == OpCodes.Ldfld && operandString == "IMoveTarget moveTarget")
                    {
                        // Looks for field access to UnitSystem.Army.moveTarget and replaces it with a function call to
                        // GetAttackTarget with UnitSystem.Army already on the stack as the argument, and the 
                        // ArcherGeneral instance which is inserted into the stack first.
                        CodeInstruction newInstruction = new CodeInstruction(OpCodes.Ldarg_0);
                        yield return newInstruction;

                        MethodInfo method = AccessTools.Method(typeof(ModMain), nameof(GetAttackTarget));
                        newInstruction = new CodeInstruction(OpCodes.Call, method);
                        yield return newInstruction;
                    }
                    else 
                    {
                        yield return instruction;
                    }
                }
            }
        }

        // TroopTransportShip::MoveTo patch for setting archers' move target if the move target is an enemy.
        [HarmonyPatch(typeof(TroopTransportShip), "IMoveableUnit.MoveTo")]
        public static class SetArchersMoveTargetPatch
        {
            static void Postfix(TroopTransportShip __instance, IMoveTarget target, List<IMoveableUnit> ___loadTarget)
            {
                if (__instance.TeamID() != 0 || target == null || target.TeamID() == 0 || target.TeamID() == -1)
                {
                    // Not an allied ship, no target, target is allied, or target is environment. 
                    // Refer to UnitSystem::UpdatePathing for determining if target is an enemy.
                    return;
                }

                // Check if ship has archers.
                bool shipHasArchers = false;

                // Refer to TroopTransportShip::UpdateArmyPosition.
                for (int i = 0; i < ___loadTarget.Count(); i++)
                {
                    UnitSystem.Army army = ___loadTarget[i] as UnitSystem.Army;
                    if (army != null)
                    {
                        bool isArcher = army.armyType == UnitSystem.ArmyType.Archer;
                        if (isArcher)
                        {
                            shipHasArchers = true;
                            break;
                        }
                    }
                }

                if (shipHasArchers)
                {
                    IMoveTarget archersTarget = null;

                    if (target is SiegeMonster)
                    {
                        SiegeMonster ogre = (SiegeMonster)target;
                        bool ogreTargettable = !ogre.IsInvalid();
                        bool ogreDead = ogre.IsDead();

                        if (ogreTargettable) 
                        {
                            // Ogre is on land.
                            archersTarget = target;
                        }
                        else if (!ogreTargettable && !ogreDead)
                        {
                            // Ogre is on a ship. Assumption is that the closest ship is the ship it's on.
                            Vector3 pos = ogre.GetPos();

                            // Refer to ProjectileDefense::GetTarget
                            IProjectileHitable closestShip = ShipSystem.inst.GetClosestShipToAttack(pos, 1, 1f);
                            ShipBase shipBase = closestShip as ShipBase;
                            if (shipBase != null)
                            {
                                archersTarget = shipBase.GetComponentInParent<TroopTransportShip>();
                            }
                        }
                    }
                    else if (target is TroopTransportShip || target is IProjectileHitable)
                    {
                        archersTarget = target;
                    }

                    // Set archer target.
                    if (archersTarget != null)
                    {
                        for (int i = 0; i < ___loadTarget.Count(); i++)
                        {
                            UnitSystem.Army army = ___loadTarget[i] as UnitSystem.Army;
                            if (army != null)
                            {
                                bool isArcher = army.armyType == UnitSystem.ArmyType.Archer;
                                if (isArcher)
                                {
                                    army.moveTarget = archersTarget;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
