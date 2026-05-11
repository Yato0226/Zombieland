using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Reflection;

namespace ZombieLand
{
	    [HarmonyPatch(typeof(Pawn_PathFollower))]
	    public static class Zombie_PathFollower_Patches
	    {
	        private static FieldInfo _pawnField;
	        private static MethodInfo _buildingBlockingNextPathCellMethod;
		
        static bool Prepare()
	        {
	            _pawnField = AccessTools.Field(typeof(Pawn_PathFollower), "pawn");
	            _buildingBlockingNextPathCellMethod = AccessTools.Method(typeof(Pawn_PathFollower), "BuildingBlockingNextPathCell");
	            return _pawnField != null && _buildingBlockingNextPathCellMethod != null;
	        }		  [HarmonyPatch("StartPath")]
  		  [HarmonyPrefix]

  public static bool StartPath_Prefix(Pawn_PathFollower __instance, LocalTargetInfo dest, PathEndMode peMode)
  {
      var pawn = (Pawn)_pawnField.GetValue(__instance);
      if (pawn == null)
          return true;

      if (pawn is Zombie)
      {
          if (pawn.CurJob != null)
              pawn.CurJob.locomotionUrgency = LocomotionUrgency.Sprint;
      }

      // If the pawn is a Zombie and is Downed, swallow the StartPath attempt,
      // end the current job and avoid the error.
      if (pawn is Zombie && pawn.health.Downed)
      {
          try
          {
              pawn.jobs?.EndCurrentJob(JobCondition.Incompletable);
          }
          catch (Exception ex)
          {
              Log.Warning($"[ZombieLand] Failed to end job in StartPath Prefix for {pawn}: {ex}");
          }
         
          return false;
      }


      return true;
  }


        [HarmonyPatch(typeof(Pawn_PathFollower), "PatherTick")]
        public static class PatherTick_Patch
        {
            
            static readonly AccessTools.FieldRef<Pawn_PathFollower, Pawn> GetPawn =
                AccessTools.FieldRefAccess<Pawn_PathFollower, Pawn>("pawn");

            static readonly Func<Pawn_PathFollower, Building> GetBlockingBuilding =
                AccessTools.MethodDelegate<Func<Pawn_PathFollower, Building>>(
                    AccessTools.Method(typeof(Pawn_PathFollower), "BuildingBlockingNextPathCell")
                );

            [HarmonyPrefix]
            public static bool Prefix(Pawn_PathFollower __instance)
            {
                var pawn = GetPawn(__instance);

                if (!(pawn is Zombie zombie))
                    return true;

                if (!zombie.IsHashIntervalTick(10))
                    return true;

                var curJob = pawn.CurJob;

                if (curJob?.def == JobDefOf.AttackMelee)
                    return true;

                var building = GetBlockingBuilding(__instance);
                if (building is Building_Door door && !door.FreePassage)
                {
                    if (curJob?.targetA.Thing != building)
                    {
                        pawn.jobs.StartJob(
                            JobMaker.MakeJob(JobDefOf.AttackMelee, building),
                            JobCondition.Incompletable
                        );
                    }
                    return true;
                }

                var blockingPawn = PawnUtility.PawnBlockingPathAt(__instance.nextCell, pawn);

                if (blockingPawn != null && blockingPawn != pawn)
                {
                    if (curJob?.targetA.Thing != blockingPawn)
                    {
                        pawn.jobs.StartJob(
                            JobMaker.MakeJob(JobDefOf.AttackMelee, blockingPawn),
                            JobCondition.Incompletable
                        );
                    }
                }

                return true;
            }
        }
    }
}
