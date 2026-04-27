using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public enum SpawnWhenType
	{
		AllTheTime,
		WhenDark,
		InEventsOnly
	}

	public enum SpawnHowType
	{
		AllOverTheMap,
		FromTheEdges
	}

	public enum AttackMode
	{
		Everything,
		OnlyHumans,
		OnlyColonists
	}

	public enum SmashMode
	{
		Nothing,
		DoorsOnly,
		AnyBuilding
	}

	public enum ZombieInstinct
	{
		Dull,
		Normal,
		Sensitive
	}

	public enum WanderingStyle
	{
		Random,
		Simple,
		Smart
	}

	public enum SmartWanderingTime
	{
		Night,
		AllDay
	}

	public enum AreaRiskMode : byte
	{
		Ignore,
		ColonistInside,
		ColonistOutside,
		ZombieInside,
		ZombieOutside,
	}

	internal class NoteDialog : Dialog_MessageBox
	{
		internal NoteDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new(320, 240);
	}

	public class ZombieRiskArea : IExposable
	{
		public int area;
		public int map;
		public AreaRiskMode mode;

		public static List<ZombieRiskArea> temp = new();

		public void ExposeData()
		{
			Scribe_Values.Look(ref area, nameof(area));
			Scribe_Values.Look(ref map, nameof(map));
			Scribe_Values.Look(ref mode, nameof(mode));
		}
	}

	public class SettingsKeyFrame : IExposable
	{
		static readonly Dictionary<string, char> firstLetters;
		static SettingsKeyFrame()
		{
			firstLetters = Enum.GetNames(typeof(Unit))
				.Select(u => (u, u.Translate().CapitalizeFirst().ToString()[0]))
				.ToDictionary(pair => pair.u, pair => pair.Item2);
		}

		public enum Unit
		{
			Days,
			Seasons,
			Years
		}

		public int amount = 0;
		public Unit unit = Unit.Days;
		public SettingsGroup values;

		public int Ticks => unit switch
		{
			Unit.Days => amount * GenDate.TicksPerDay,
			Unit.Seasons => amount * GenDate.TicksPerSeason,
			Unit.Years => amount * GenDate.TicksPerYear,
			_ => throw new NotImplementedException()
		};

		public void ExposeData()
		{
			Scribe_Values.Look(ref amount, nameof(amount), 0);
			Scribe_Values.Look(ref unit, nameof(unit), Unit.Days);
			Scribe_Deep.Look(ref values, nameof(values));
		}

		public override string ToString()
		{
			if (amount == 0)
				return "0";
			return $"{amount}{firstLetters[unit.ToString()]}";
		}

		public SettingsKeyFrame Copy() => new()
		{
			amount = amount,
			unit = unit,
			values = values.MakeCopy()
		};
	}

	public static class CopyPasteSettings
	{
		public class Holder
		{
			public SettingsKeyFrame[] settings;
		}

		public static void ToClipboard(this List<SettingsKeyFrame> settingsOverTime)
		{
			var holder = new Holder() { settings = settingsOverTime.ToArray() };
			var hex = Tools.SerializeToHex(holder);
			GUIUtility.systemCopyBuffer = $"[{hex}]";
		}

		public static void FromClipboard(this List<SettingsKeyFrame> settingsOverTime)
		{
			var chars = GUIUtility.systemCopyBuffer.ToLower().ToCharArray();
			var hex = chars.Where(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).Join(null, "");
			if (hex.NullOrEmpty() == false)
			{
				try
				{
					var holder = Tools.DeserializeFromHex<Holder>(hex);
					if (holder?.settings == null)
						return;
					DialogTimeHeader.Reset();
					settingsOverTime.Clear();
					settingsOverTime.AddRange(holder.settings
						.Select(setting => setting.Copy()));
				}
				catch (Exception ex)
				{
					Log.Error($"Cannot restore ZombieLand settings from {hex}: {ex}");
				}
			}
		}
	}

	public class SettingsGroup : IExposable, ICloneable
	{
		public float threatScale = 1f;
		public SpawnWhenType spawnWhenType = SpawnWhenType.AllTheTime;
		public SpawnHowType spawnHowType = SpawnHowType.FromTheEdges;
		public AttackMode attackMode = AttackMode.OnlyHumans;
		public bool enemiesAttackZombies = true;
		public bool animalsAttackZombies = false;
		public SmashMode smashMode = SmashMode.DoorsOnly;
		public bool smashOnlyWhenAgitated = true;
		public bool doubleTapRequired = true;
		public bool zombiesDieVeryEasily = false;
		public float healthFactor = 1f;
		public int daysBeforeZombiesCome = 3;
		public int maximumNumberOfZombies = 500;
		public bool useDynamicThreatLevel = true;
		public bool zombiesDieOnZeroThreat = true;
		public float dynamicThreatSmoothness = 2.5f;
		public float dynamicThreatStretch = 20f;
		public float infectedRaidsChance = 0.1f;
		public float colonyMultiplier = 1f;
		public int baseNumberOfZombiesinEvent = 20;
		internal int extraDaysBetweenEvents = 0;
		public float suicideBomberChance = 0.0025f;
		public float toxicSplasherChance = 0.0025f;
		public float tankyOperatorChance = 0.0025f;
		public float minerChance = 0.0025f;
		public float electrifierChance = 0.0025f;
		public float albinoChance = 0.0025f;
		public float darkSlimerChance = 0.0025f;
		public float healerChance = 0.0025f;
		public float moveSpeedIdle = 0.1f;
		public float moveSpeedTracking = 0.5f;
		public bool moveSpeedUpgraded = false;
		public float damageFactor = 1.0f;
		public ZombieInstinct zombieInstinct = ZombieInstinct.Normal;
		public bool useCustomTextures = true;
		public bool playCreepyAmbientSound = true;
		public bool zombiesEatDowned = true;
		public bool zombiesEatCorpses = true;
		public float zombieBiteInfectionChance = 0.5f;
		public int hoursInfectionIsUnknown = 8;
		public int hoursInfectionIsTreatable = 24;
		public int hoursInfectionPersists = 6 * 24;
		public bool anyTreatmentStopsInfection;
		public int hoursAfterDeathToBecomeZombie = 8;
		public bool deadBecomesZombieMessage = true;
		public bool dangerousSituationMessage = true;
		public float corpsesExtractAmount = 1f;
		public float lootExtractAmount = 0.1f;
		public string extractZombieArea = "";
		public int corpsesHoursToDessicated = 2;
		public bool betterZombieAvoidance = true;
		public bool ragingZombies = true;
		public bool zombieHivemind = false;
		public int zombieRageLevel = 3;
		public float minAttackDistanceSquared = 2.25f;
		public int canAttackSearchRadius = 1;
		public bool replaceTwinkie = true;
		public bool zombiesDropBlood = true;
		public bool zombiesBurnLonger = true;
		public float reducedTurretConsumption = 0f;
		public bool zombiesCauseManhuntingResponse = true;
		public int safeMeleeLimit = 1;
		public WanderingStyle wanderingStyle = WanderingStyle.Smart;
		public SmartWanderingTime smartWanderingTime = SmartWanderingTime.Night;
		public bool showHealthBar = true;
		public HashSet<string> biomesWithoutZombies = new();
		public bool showZombieStats = true;
		public Dictionary<Area, AreaRiskMode> dangerousAreas = new();
		public bool highlightDangerousAreas = false;
		public float zombieDodgeChanceFactor = 1f;
		public bool disableRandomApparel = false;
		public bool floatingZombies = true;
		public float childChance = 0.02f;
		public float spitterThreat = 1f;
		public int minimumZombiesForWallPushing = 18;
		public List<string> blacklistedApparel = new();
		public float contaminationBaseFactor = 1f;
		public bool disableCleanContamination = true;
		public ContaminationFactors contamination = new();

        public List<string> allowedOdysseyLayers = new List<string>();

		public float minContaminationToCleanCell = 0.1f;
		public float minContaminationToCleanItem = 0.1f;

		// unused
		public int suicideBomberIntChance = 1;
		public int toxicSplasherIntChance = 1;
		public int tankyOperatorIntChance = 1;
		public int minerIntChance = 1;
		public int electrifierIntChance = 1;

		public object Clone() => MemberwiseClone();
		public SettingsGroup MakeCopy() => Clone() as SettingsGroup;

		public void ExposeData()
		{
			//Log.Message($"[Zombieland] SettingsGroup.ExposeData - Mode: {Scribe.mode}, ThreatScale (before): {threatScale}");
			Scribe_Values.Look(ref threatScale, "threatScale", 1f);
			Scribe_Values.Look(ref spawnWhenType, "spawnWhenType", SpawnWhenType.AllTheTime);
			Scribe_Values.Look(ref spawnHowType, "spawnHowType", SpawnHowType.FromTheEdges);
			Scribe_Values.Look(ref attackMode, "attackMode", AttackMode.OnlyHumans);
			Scribe_Values.Look(ref enemiesAttackZombies, "enemiesAttackZombies", true);
			Scribe_Values.Look(ref animalsAttackZombies, "animalsAttackZombies", false);
			Scribe_Values.Look(ref smashMode, "smashMode", SmashMode.DoorsOnly);
			Scribe_Values.Look(ref smashOnlyWhenAgitated, "smashOnlyWhenAgitated", true);
			Scribe_Values.Look(ref doubleTapRequired, "doubleTapRequired", true);
			Scribe_Values.Look(ref zombiesDieVeryEasily, "zombiesDieVeryEasily", false);
			Scribe_Values.Look(ref healthFactor, "healthFactor", 1f);
			Scribe_Values.Look(ref daysBeforeZombiesCome, "daysBeforeZombiesCome", 3);
			Scribe_Values.Look(ref maximumNumberOfZombies, "maximumNumberOfZombies", 500);
			Scribe_Values.Look(ref useDynamicThreatLevel, "useDynamicThreatLevel", true);
			Scribe_Values.Look(ref zombiesDieOnZeroThreat, "zombiesDieOnZeroThreat", true);
			Scribe_Values.Look(ref dynamicThreatSmoothness, "dynamicThreatSmoothness", 2.5f);
			Scribe_Values.Look(ref dynamicThreatStretch, "dynamicThreatStretch", 20f);
			Scribe_Values.Look(ref infectedRaidsChance, "infectedRaidsChance", 0.1f);
			Scribe_Values.Look(ref colonyMultiplier, "colonyMultiplier", 1f);
			Scribe_Values.Look(ref baseNumberOfZombiesinEvent, "baseNumberOfZombiesinEvent", 20);
			Scribe_Values.Look(ref extraDaysBetweenEvents, "extraDaysBetweenEvents", 0);
			Scribe_Values.Look(ref suicideBomberChance, "suicideBomberChance", 0.0025f);
			Scribe_Values.Look(ref toxicSplasherChance, "toxicSplasherChance", 0.0025f);
			Scribe_Values.Look(ref tankyOperatorChance, "tankyOperatorChance", 0.0025f);
			Scribe_Values.Look(ref minerChance, "minerChance", 0.0025f);
			Scribe_Values.Look(ref electrifierChance, "electrifierChance", 0.0025f);
			Scribe_Values.Look(ref albinoChance, "albinoChance", 0.0025f);
			Scribe_Values.Look(ref darkSlimerChance, "darkSlimerChance", 0.0025f);
			Scribe_Values.Look(ref healerChance, "healerChance", 0.0025f);
			Scribe_Values.Look(ref moveSpeedIdle, "moveSpeedIdle", 0.1f);
			Scribe_Values.Look(ref moveSpeedTracking, "moveSpeedTracking", 0.5f);
			Scribe_Values.Look(ref moveSpeedUpgraded, "moveSpeedUpgraded", false);
			Scribe_Values.Look(ref damageFactor, "damageFactor", 1.0f);
			Scribe_Values.Look(ref zombieInstinct, "zombieInstinct", ZombieInstinct.Normal);
			Scribe_Values.Look(ref useCustomTextures, "useCustomTextures", true);
			Scribe_Values.Look(ref playCreepyAmbientSound, "playCreepyAmbientSound", true);
			Scribe_Values.Look(ref zombiesEatDowned, "zombiesEatDowned", true);
			Scribe_Values.Look(ref zombiesEatCorpses, "zombiesEatCorpses", true);
			Scribe_Values.Look(ref zombieBiteInfectionChance, "zombieBiteInfectionChance", 0.5f);
			Scribe_Values.Look(ref hoursInfectionIsUnknown, "hoursInfectionIsUnknown", 8);
			Scribe_Values.Look(ref hoursInfectionIsTreatable, "hoursInfectionIsTreatable", 24);
			Scribe_Values.Look(ref hoursInfectionPersists, "hoursInfectionPersists", 144);
			Scribe_Values.Look(ref anyTreatmentStopsInfection, "anyTreatmentStopsInfection", false);
			Scribe_Values.Look(ref hoursAfterDeathToBecomeZombie, "hoursAfterDeathToBecomeZombie", 8);
			Scribe_Values.Look(ref deadBecomesZombieMessage, "deadBecomesZombieMessage", true);
			Scribe_Values.Look(ref dangerousSituationMessage, "dangerousSituationMessage", true);
			Scribe_Values.Look(ref corpsesExtractAmount, "corpsesExtractAmount", 1f);
			Scribe_Values.Look(ref lootExtractAmount, "lootExtractAmount", 0.1f);
			Scribe_Values.Look(ref extractZombieArea, "extractZombieArea", "");
			Scribe_Values.Look(ref corpsesHoursToDessicated, "corpsesHoursToDessicated", 2);
			Scribe_Values.Look(ref betterZombieAvoidance, "betterZombieAvoidance", true);
			Scribe_Values.Look(ref ragingZombies, "ragingZombies", true);
			Scribe_Values.Look(ref zombieHivemind, "zombieHivemind", false);
			Scribe_Values.Look(ref zombieRageLevel, "zombieRageLevel", 3);
			Scribe_Values.Look(ref minAttackDistanceSquared, "minAttackDistanceSquared", 2.25f);
			Scribe_Values.Look(ref canAttackSearchRadius, "canAttackSearchRadius", 1);
			Scribe_Values.Look(ref replaceTwinkie, "replaceTwinkie", true);
			Scribe_Values.Look(ref zombiesDropBlood, "zombiesDropBlood", true);
			Scribe_Values.Look(ref zombiesBurnLonger, "zombiesBurnLonger", true);
			Scribe_Values.Look(ref reducedTurretConsumption, "reducedTurretConsumption", 0f);
			Scribe_Values.Look(ref zombiesCauseManhuntingResponse, "zombiesCauseManhuntingResponse", true);
			Scribe_Values.Look(ref safeMeleeLimit, "safeMeleeLimit", 1);
			Scribe_Values.Look(ref wanderingStyle, "wanderingStyle", WanderingStyle.Smart);
			Scribe_Values.Look(ref smartWanderingTime, "smartWanderingTime", SmartWanderingTime.Night);
			Scribe_Values.Look(ref showHealthBar, "showHealthBar", true);
			try
			{
				Scribe_Collections.Look(ref biomesWithoutZombies, "biomesWithoutZombies", LookMode.Value);
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland: Failed to load biomesWithoutZombies, using defaults. Exception: {ex.Message}");
				biomesWithoutZombies = new HashSet<string>();
			}
			Scribe_Values.Look(ref showZombieStats, "showZombieStats", true);
			Scribe_Values.Look(ref highlightDangerousAreas, "highlightDangerousAreas", false);
			Scribe_Values.Look(ref disableRandomApparel, "disableRandomApparel", false);
			Scribe_Values.Look(ref zombieDodgeChanceFactor, "zombieDodgeChanceFactor", 1f);
			Scribe_Values.Look(ref floatingZombies, "floatingZombies", true);
			Scribe_Values.Look(ref childChance, "childChance", 0.02f);
			Scribe_Values.Look(ref spitterThreat, "spitterThreat", 1f);
			Scribe_Values.Look(ref minimumZombiesForWallPushing, "minimumZombiesForWallPushing", 18);
			try
			{
				Scribe_Collections.Look(ref blacklistedApparel, "blacklistedApparel", LookMode.Value);
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland: Failed to load blacklistedApparel, using defaults. Exception: {ex.Message}");
				blacklistedApparel = new List<string>();
			}
			Scribe_Values.Look(ref contaminationBaseFactor, "contaminationBaseFactor", 1f);
			Scribe_Values.Look(ref disableCleanContamination, "disableCleanContamination", true);
			Scribe_Values.Look(ref minContaminationToCleanCell, "minContaminationToCleanCell", 0.1f);
			Scribe_Values.Look(ref minContaminationToCleanItem, "minContaminationToCleanItem", 0.1f);
			Scribe_Values.Look(ref minContaminationToCleanCell, "minContaminationToCleanCell", 0.1f);
			Scribe_Values.Look(ref minContaminationToCleanItem, "minContaminationToCleanItem", 0.1f);

			Scribe_Deep.Look(ref contamination, "contamination");
			try
			{
				Scribe_Collections.Look(ref allowedOdysseyLayers, "allowedOdysseyLayers", LookMode.Value);
			}
			catch (Exception ex)
			{
				Log.Warning($"Zombieland: Failed to load allowedOdysseyLayers, using defaults. Exception: {ex.Message}");
				allowedOdysseyLayers = new List<string>();
			}
			Scribe_Values.Look(ref suicideBomberIntChance, "suicideBomberIntChance", 1);
			Scribe_Values.Look(ref toxicSplasherIntChance, "toxicSplasherIntChance", 1);
			Scribe_Values.Look(ref tankyOperatorIntChance, "tankyOperatorIntChance", 1);
			Scribe_Values.Look(ref minerIntChance, "minerIntChance", 1);
			Scribe_Values.Look(ref electrifierIntChance, "electrifierIntChance", 1);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				Tools.UpdateBiomeBlacklist(biomesWithoutZombies);
			}
			//Log.Message($"[Zombieland] SettingsGroup.ExposeData - Mode: {Scribe.mode}, ThreatScale (after): {threatScale}");
		}
	}

	class ZombieSettingsDefaults : ModSettings
	{
		public static SettingsGroup group;
		public static List<SettingsKeyFrame> groupOverTime;

		public static void Defaults()
		{
			group = (new SettingsGroup()).MakeCopy();
			groupOverTime = new() { new SettingsKeyFrame() { values = group.MakeCopy() } };
		}

		public static void DoWindowContents(Rect inRect)
		{
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
			{
				if (idx >= groupOverTime.Count)
				{
					DialogTimeHeader.selectedKeyframe = 0;
					idx = 0;
				}
				SettingsDialog.DoWindowContentsInternal(ref groupOverTime[idx].values, ref groupOverTime, inRect);
			}
			else
			{
				var settings = ZombieSettings.CalculateInterpolation(groupOverTime, ticks);
				SettingsDialog.DoWindowContentsInternal(ref settings, ref groupOverTime, inRect);
			}
		}

		public static void WriteSettings()
		{
		}

		public override void ExposeData()
		{
			//Log.Message($"[Zombieland] ZombieSettingsDefaults.ExposeData - Mode: {Scribe.mode}, GroupThreatScale (before): {group?.threatScale}");
			base.ExposeData();
			group ??= new SettingsGroup();
			groupOverTime ??= new() { new SettingsKeyFrame() { values = group.MakeCopy() } };
			Scribe_Deep.Look(ref group, "defaults", Array.Empty<object>());
			Scribe_Collections.Look(ref groupOverTime, "defaultsOverTime", LookMode.Deep, Array.Empty<object>());
			//Log.Message($"[Zombieland] ZombieSettingsDefaults.ExposeData - Mode: {Scribe.mode}, GroupThreatScale (after): {group?.threatScale}");
		}
	}

	class ZombieSettings : WorldComponent
	{
		public static SettingsGroup Values;
		public static List<SettingsKeyFrame> ValuesOverTime;

		static ZombieSettings()
		{
			Values = ZombieSettingsDefaults.group;
			ValuesOverTime = ZombieSettingsDefaults.groupOverTime;
		}

		public ZombieSettings(World world) : base(world)
		{
		}

		public static void ApplyDefaults()
		{
			ValuesOverTime = new(ZombieSettingsDefaults.groupOverTime);
			Values = CalculateInterpolation(ValuesOverTime, 0);
			SettingsDialog.scrollPosition = Vector2.zero;
		}

		static readonly Dictionary<string, FieldInfo> fieldInfos = new();
		public static SettingsGroup CalculateInterpolation(List<SettingsKeyFrame> settingsOverTime, int ticks)
		{
			var n = settingsOverTime.Count;
			if (n == 1)
				return settingsOverTime[0].values.MakeCopy();
			var upperIndex = settingsOverTime.FirstIndexOf(key => key.Ticks > ticks);
			if (upperIndex == -1)
				return settingsOverTime.Last().values.MakeCopy();
			var lowerFrame = settingsOverTime[upperIndex - 1];
			var upperFrame = settingsOverTime[upperIndex];
			var lowerTicks = lowerFrame.Ticks;
			var upperTicks = upperFrame.Ticks;
			var lowerValues = lowerFrame.values;
			var upperValues = upperFrame.values;
			var result = new SettingsGroup();
			AccessTools.GetFieldNames(result).Do(name =>
			{
				if (fieldInfos.TryGetValue(name, out var field) == false)
					fieldInfos.Add(name, field = AccessTools.Field(typeof(SettingsGroup), name));
				var type = field.FieldType;
				var lowerValue = field.GetValue(lowerValues);
				var upperValue = field.GetValue(upperValues);
				if (type == typeof(int))
				{
					var val = (int)GenMath.LerpDoubleClamped(lowerTicks, upperTicks, (int)lowerValue, (int)upperValue, ticks);
					field.SetValue(result, val);
				}
				else if (type == typeof(float))
				{
					var val = GenMath.LerpDoubleClamped(lowerTicks, upperTicks, (float)lowerValue, (float)upperValue, ticks);
					field.SetValue(result, val);
				}
				else
					field.SetValue(result, lowerValue);
			});
			return result;
		}

		public static ZombieSettings GetGameSettings()
		{
			ZombieSettings settings = null;
			var world = Find.World;
			if (world != null && world.components != null)
				settings = world.components.OfType<ZombieSettings>().FirstOrDefault();
			return settings;
		}

		public void DoWindowContents(Rect inRect)
		{
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
				SettingsDialog.DoWindowContentsInternal(ref ValuesOverTime[idx].values, ref ValuesOverTime, inRect);
			else
			{
				var settings = CalculateInterpolation(ValuesOverTime, ticks);
				SettingsDialog.DoWindowContentsInternal(ref settings, ref ValuesOverTime, inRect);
			}
		}

		public void WriteSettings()
		{
			Tools.EnableTwinkie(Values.replaceTwinkie);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref Values, "values", Array.Empty<object>());
			Scribe_Collections.Look(ref ValuesOverTime, "valuesOverTime", LookMode.Deep);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (ValuesOverTime == null || ValuesOverTime.Count == 0)
					ValuesOverTime = new List<SettingsKeyFrame> { new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = Values } };

				var ticks = Mathf.Clamp(GenTicks.TicksGame, 0, ValuesOverTime.Last().Ticks);
				var settings = CalculateInterpolation(ValuesOverTime, ticks);
				ContaminationFactors.ApplyBaseFactor(settings.contamination, settings.contaminationBaseFactor);
			}
		}
	}
}