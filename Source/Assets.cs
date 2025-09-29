﻿using HarmonyLib;
using System.IO;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	[StaticConstructorOnStartup]
	public static class Assets
	{
		public static bool initialized = false;

		private static GameObject dust;
		private static Shader metaballShader;

		[HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
		[HarmonyPostfix]
		public static void LoadAssetBundle()
		{
			if (initialized)
				return;

			var arch = "Win64";
			var platform = Application.platform;
			if (platform == RuntimePlatform.LinuxEditor || platform == RuntimePlatform.LinuxPlayer)
				arch = "Linux";
			if (platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.OSXPlayer)
				arch = "MacOS";

			var me = LoadedModManager.GetMod<ZombielandMod>();
			var path = Path.Combine(me.Content.RootDir, "Resources", arch, "zombieland");
			var assets = UnityEngine.AssetBundle.LoadFromFile(path);

			dust = assets.LoadAsset<GameObject>("Dust");
			metaballShader = assets.LoadAsset<Shader>("Metaballs");

			initialized = true;
		}

		public static GameObject NewDust() => Object.Instantiate(dust);
		public static Shader MetaballShader => metaballShader;
	}
}