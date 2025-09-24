﻿using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[HarmonyPatch(typeof(JobDriver_ClearPollution), nameof(JobDriver_ClearPollution.MakeNewToils))]
	static class JobDriver_ClearPollution_ClearPollutionAt_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_ClearPollution __instance)
		{
			foreach (var toil in toils)
			{
				GridUtility_Unpollute_Patch.subject = __instance.pawn;
				yield return toil;
			}
			GridUtility_Unpollute_Patch.subject = null;
		}
	}

	[HarmonyPatch(typeof(GridsUtility), nameof(GridsUtility.Unpollute))]
	static class GridUtility_Unpollute_Patch
	{
		public static Thing subject = null;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(IntVec3 c, Map map)
		{
			var contamination = map.GetContamination(c);
			subject?.AddContamination(contamination, null, ZombieSettings.Values.contamination.pollutionAdd);
		}
	}

	[HarmonyPatch]
	static class JobDriver_ClearSnow_MakeNewToils_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var m_SetDepth = SymbolExtensions.GetMethodInfo((SnowGrid grid) => grid.SetDepth(default, default));
			var type = AccessTools.FirstInner(typeof(JobDriver_ClearPollution), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_SetDepth));
		}

		static void SetDepth(SnowGrid self, IntVec3 c, float newDepth, Toil toil)
		{
			var contamination = self.map.GetContamination(c);
			var pawn = toil.actor;
			pawn.AddContamination(contamination, null, ZombieSettings.Values.contamination.snowAdd);
			self.SetDepth(c, newDepth);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(SnowGrid), () => SetDepth(default, default, default, default), default, 1, true);
	}
}