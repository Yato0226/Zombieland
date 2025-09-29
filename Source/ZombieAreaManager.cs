﻿using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ZombieAreaManager
	{
		public static Map lastMap = null;
		public static Dictionary<Pawn, Area> pawnsInDanger = new();
		public static bool warningShowing = false;
		public static IEnumerator stateUpdater = StateUpdater();

		static Zombie[] AllZombies(Map map)
		{
			try
			{
				return map.GetComponent<TickManager>().allZombiesCached.ToArray();
			}
			catch (Exception)
			{
				return new Zombie[0];
			}
		}

		static IEnumerator StateUpdater()
		{
			while (true)
			{
				yield return null;
				var map = Find.CurrentMap;
				if (map == null)
					continue;

				if (lastMap != map)
				{
					lastMap = map;
					pawnsInDanger.Clear();
				}

				var areas = ZombieSettings.Values.dangerousAreas.Where(pair => pair.Key.Map == map && pair.Value != AreaRiskMode.Ignore).ToArray();
				if (areas.Length == 0)
				{
					pawnsInDanger.Clear();
					continue;
				}

				var pawns = map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn.InfectionState() < InfectionState.Infecting).ToArray();
				yield return null;
				for (int pIdx = 0; pIdx < pawns.Length; pIdx++)
				{
					var pawn = pawns[pIdx];
					var found = false;
					if (pawn.Spawned && pawn.Map == map)
					{
						for (var aIdx = 0; aIdx < areas.Length; aIdx++)
						{
							var (area, mode) = (areas[aIdx].Key, areas[aIdx].Value);
							var inside = area[pawn.Position];
							if (inside && mode == AreaRiskMode.ColonistInside || inside == false && mode == AreaRiskMode.ColonistOutside)
							{
								if (pawnsInDanger.ContainsKey(pawn) == false)
									pawnsInDanger.Add(pawn, area);
								found = true;
							}
							yield return null;
						}
					}
					if (found == false)
						_ = pawnsInDanger.Remove(pawn);
				}

				if (map == null)
					continue;

				var zombies = AllZombies(map);
				yield return null;

				for (int zIdx = 0; zIdx < zombies.Length; zIdx++)
				{
					var zombie = zombies[zIdx];
					var found = false;
					if (zombie.Spawned)
					{
						for (var aIdx = 0; aIdx < areas.Length; aIdx++)
						{
							var (area, mode) = (areas[aIdx].Key, areas[aIdx].Value);
							var inside = area[zombie.Position];
							if (inside && mode == AreaRiskMode.ZombieInside || inside == false && mode == AreaRiskMode.ZombieOutside)
							{
								if (pawnsInDanger.ContainsKey(zombie) == false)
									pawnsInDanger.Add(zombie, area);
								found = true;
							}
							yield return null;
						}
					}
					if (found == false)
						_ = pawnsInDanger.Remove(zombie);
				}
			}
		}

		public static void DangerAlertsOnGUI()
		{
			var map = Find.CurrentMap;
			if (map == null)
				return;

			try
			{
				_ = stateUpdater.MoveNext();
			}
			catch (Exception ex)
			{
				Log.Error($"ZombieAreaManager threw an exception in the state updater: {ex}");
				stateUpdater = StateUpdater();
			}

			if (Find.World.renderer.wantedMode == WorldRenderMode.None)
				DrawDangerous();
		}

		public static void ShowCentered(IntVec3 minCell, IntVec3 maxCell)
		{
			var center = new IntVec3((minCell.x + maxCell.x) / 2, 0, (minCell.z + maxCell.z) / 2);
			CameraJumper.TryJump(new GlobalTargetInfo(center, Find.CurrentMap));
		}

		static readonly Dictionary<Color, Texture2D> areaColorTextures = new();
		static readonly CountingCache<Pawn, Texture2D> pawnHeadTextures = new(120);

		public static void DrawDangerous()
		{
			Area foundArea = null;
			Texture2D colorTexture = null;
			var headsToDraw = new List<(Pawn, Texture)>();
			var highlightDangerousAreas = ZombieSettings.Values.highlightDangerousAreas;
			foreach (var (pawn, area) in pawnsInDanger)
			{
				if (foundArea != null && foundArea != area)
					break;
				if (foundArea == null)
				{
					var c = area.Color;
					if (areaColorTextures.TryGetValue(c, out colorTexture) == false)
					{
						colorTexture = SolidColorMaterials.NewSolidColorTexture(c.r, c.g, c.b, 0.75f);
						areaColorTextures[c] = colorTexture;
					}
					Graphics.DrawTexture(new Rect(0, 0, UI.screenWidth, 2), colorTexture);

					if (highlightDangerousAreas)
						area.MarkForDraw();
				}
				foundArea = area;

				if (pawn is not Zombie)
				{
					var texture = pawnHeadTextures.Get(pawn, p =>
					{
						var renderTexture = RenderTexture.GetTemporary(44, 44, 32, RenderTextureFormat.ARGB32);
						Find.PawnCacheRenderer.RenderPawn(pawn, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South);
						var tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false) { name = "DangerousInfoPawn" };
						RenderTexture.active = renderTexture;
						tex.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
						tex.Apply();
						RenderTexture.active = null;
						RenderTexture.ReleaseTemporary(renderTexture);
						return tex;
					}, UnityEngine.Object.Destroy);
					headsToDraw.Add((pawn, texture));
				}
				else
					headsToDraw.Add((pawn, null));
			}

			warningShowing = colorTexture != null;
			if (warningShowing)
			{
				var zombiesInArea = headsToDraw.Where(pair => pair.Item2 == null).Select(pair => pair.Item1).ToArray();

				var n = headsToDraw.Where(pair => pair.Item2 != null).Count();
				if (zombiesInArea.Length > 0)
					n++;
				var width = 5 + n * 2 + (n + 1) * 18 + 5;
				var rect = new Rect(118, 2, width, 29);
				Graphics.DrawTexture(rect, colorTexture);
				var showPositions = Mouse.IsOver(rect.ExpandedBy(4));

				rect = new Rect(123, 7, 18, 18);
				Graphics.DrawTexture(rect, Constants.Danger);

				var pos = 0;
				if (zombiesInArea.Length > 0)
				{
					rect = new Rect(141 + pos++ * 22, 5, 22, 22);
					Graphics.DrawTexture(rect, Constants.zoneZombie);
					if (Widgets.ButtonInvisible(rect))
					{
						var minX = 100000;
						var minZ = 100000;
						var maxX = -100000;
						var maxZ = -100000;
						zombiesInArea.Select(z => z.Position).Do(p =>
						{
							minX = Mathf.Min(minX, p.x);
							minZ = Mathf.Min(minZ, p.z);
							maxX = Mathf.Max(maxX, p.x);
							maxZ = Mathf.Max(maxZ, p.z);
						});
						ShowCentered(new IntVec3(minX, 0, minZ), new IntVec3(maxX, 0, maxZ));
					};
					if (showPositions)
						zombiesInArea.Do(zombie => TargetHighlighter.Highlight(new GlobalTargetInfo(zombie), true, false, false));
				}
				for (var i = 0; i < n; i++)
				{
					var (pawn, texture) = headsToDraw[i];
					if (texture != null)
					{
						rect = new Rect(141 + pos++ * 22, 5, 22, 22);
						Graphics.DrawTexture(rect, texture);
						if (Widgets.ButtonInvisible(rect))
							ShowCentered(pawn.Position, pawn.Position);
					}
					if (showPositions)
						TargetHighlighter.Highlight(new GlobalTargetInfo(pawn), true, false, false);
				}
			}
		}
	}

	[HarmonyPatch(typeof(AreaManager))]
	public static class AreaManager_Patches
	{
		[HarmonyPrefix]
		[HarmonyPatch(nameof(AreaManager.CanMakeNewAllowed))]
		public static bool CanMakeNewAllowed(ref bool __result)
		{
			__result = true;
			return false;
		}

	}

	[HarmonyPatch(typeof(Dialog_ManageAreas))]
	public static class Dialog_ManageAreas_Patches
	{
		public static readonly Color listBackground = new(32 / 255f, 36 / 255f, 40 / 255f);
		public static readonly Color highlightedBackground = new(74 / 255f, 74 / 255f, 74 / 255f, 0.5f);
		public static readonly Color background = new(74 / 255f, 74 / 255f, 74 / 255f);
		public static readonly Color inactiveTextColor = new(145 / 255f, 125 / 255f, 98 / 255f);
		public static readonly Color areaNameColonistInside = new(1f, 0.2f, 0.2f);
		public static readonly Color areaNameColonistOutside = new(0.2f, 1f, 0.2f);
		public static readonly Color areaNameZombieInside = new(1f, 0.5f, 0f);
		public static readonly Color areaNameZombieOutside = new(1f, 26f / 255f, 140f / 255f);
		public static readonly GUIStyle textFieldStyle = new()
		{
			alignment = TextAnchor.MiddleLeft,
			clipping = TextClipping.Clip,
			font = Text.fontStyles[1].font,
			normal = new GUIStyleState() { textColor = Color.white },
			padding = new RectOffset(7, 0, 0, 0)
		};
		public static Area selected = null;
		public static int selectedIndex = -1;
		public static Vector2 scrollPosition = Vector2.zero;
		public static AreaManager areaManager;

		[HarmonyPostfix]
		[HarmonyPatch(MethodType.Constructor, new[] { typeof(Map) })]
		public static void Constructor()
		{
			selected = null;
			selectedIndex = -1;
			scrollPosition = Vector2.zero;
		}

		[HarmonyPrefix]
		[HarmonyPriority(Priority.High)]
		[HarmonyPatch(nameof(Dialog_ManageAreas.DoWindowContents))]
		public static bool Prefix(Dialog_ManageAreas __instance)
		{
			Text.Font = GameFont.Small;

			RenderList(Find.CurrentMap);
			if (selected != null)
			{
				RenderSelectedRowContent(selected);
				selected.MarkForDraw();
			}
			return false;
		}

		public static void RenderList(Map map)
		{
			areaManager = map.areaManager;
			var allAreas = areaManager.AllAreas;
			var rowHeight = 24;

			var rect = new Rect(0, 0, 198, 283);
			Widgets.DrawBoxSolid(rect, listBackground);

			var innerWidth = rect.width - (allAreas.Count > 11 ? 16 : 0);
			var innerRect = new Rect(0f, 0f, innerWidth, allAreas.Count * rowHeight);
			Widgets.BeginScrollView(rect, ref scrollPosition, innerRect, true);
			var list = new Listing_Standard();
			list.Begin(innerRect);

			var y = 0f;
			var i = 0;
			foreach (var area in allAreas)
			{
				RenderListRow(new Rect(0, y, innerRect.width, rowHeight), area, i++);
				y += rowHeight;
			}

			list.End();
			Widgets.EndScrollView();

			y = 283 + 8;
			var bRect = new Rect(0, y, 24, 24);
			if (Widgets.ButtonImage(bRect, Constants.ButtonAdd[1]))
			{
				Event.current.Use();
				if (areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
				{
					selected = newArea;
					selectedIndex = areaManager.AllAreas.IndexOf(selected);
					GUI.FocusControl("area-name");
				}
			}
			bRect.x += 32;
			var deleteable = selected?.Mutable ?? false;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDel[deleteable ? 1 : 0]) && deleteable)
			{
				Event.current.Use();
				selected.Delete();
				_ = ZombieAreaManager.pawnsInDanger.RemoveAll(pair => pair.Value == selected);
				_ = ZombieSettings.Values.dangerousAreas.Remove(selected);
				var newCount = areaManager.AllAreas.Count;
				if (newCount == 0)
				{
					selectedIndex = -1;
					selected = null;
				}
				else
				{
					while (newCount > 0 && selectedIndex >= newCount)
						selectedIndex--;
					selected = areaManager.AllAreas[selectedIndex];
					GUI.FocusControl("area-name");
				}
			}
			bRect.x += 32;
			var dupable = selected != null;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDup[dupable ? 1 : 0]) && dupable)
			{
				Event.current.Use();
				var labelPrefix = Regex.Replace(selected.Label, @" \d+$", "");
				var existingLabels = areaManager.AllAreas.Select(a => a.Label).ToHashSet();
				for (var n = 1; true; n++)
				{
					var newLabel = $"{labelPrefix} {n}";
					if (existingLabels.Contains(newLabel) == false)
					{
																if (areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
																{
																	newArea.RenamableLabel = newLabel;							foreach (IntVec3 cell in selected.ActiveCells)
								newArea[cell] = true;
							selected = newArea;
							selectedIndex = areaManager.AllAreas.IndexOf(selected);
							GUI.FocusControl("area-name");
						}
						break;
					}
				}
			}
			bRect.x += 78;
			var upable = selectedIndex > 0;
			if (Widgets.ButtonImage(bRect, Constants.ButtonUp[upable ? 1 : 0]) && upable)
			{
				Event.current.Use();
				allAreas.Insert(selectedIndex - 1, selected);
				allAreas.RemoveAt(selectedIndex + 1);
				selectedIndex--;
			}
			bRect.x += 32;
			var downable = selectedIndex >= 0 && selectedIndex < allAreas.Count - 1;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDown[downable ? 1 : 0]) && downable)
			{
				Event.current.Use();
				allAreas.Insert(selectedIndex + 2, selected);
				allAreas.RemoveAt(selectedIndex);
				selectedIndex++;
			}

			var backgroundRect = innerRect;
			backgroundRect.height = rect.height;
			if (Widgets.ButtonInvisible(backgroundRect, false))
			{
				Event.current.Use();
				selectedIndex = -1;
				selected = null;
			}
		}

		public static Color AreaLabelColor(Area area)
		{
			return GetMode(area) switch
			{
				AreaRiskMode.ColonistInside => areaNameColonistInside,
				AreaRiskMode.ColonistOutside => areaNameColonistOutside,
				AreaRiskMode.ZombieInside => areaNameZombieInside,
				AreaRiskMode.ZombieOutside => areaNameZombieOutside,
				_ => Color.white,
			};
		}

		public static void RenderListRow(Rect rect, Area area, int idx)
		{
			if (area == selected)
				Widgets.DrawBoxSolid(rect, background);
			else if (Mouse.IsOver(rect))
				Widgets.DrawBoxSolid(rect, highlightedBackground);

			var innerRect = rect.ExpandedBy(-3);
			innerRect.xMax += 3;
			var cRect = innerRect;
			cRect.width = cRect.height;
			Widgets.DrawBoxSolid(cRect, area.Color);

			var tRect = rect;
			tRect.xMin += 24;
			tRect.yMin += 1;
			GUI.color = AreaLabelColor(area);
			_ = Widgets.LabelFit(tRect, area.Label);
			GUI.color = Color.white;

			if (area.Mutable == false)
			{
				var lRect = rect.RightPartPixels(13).LeftPartPixels(10);
				lRect.yMin += 5;
				lRect.height = 13;
				GUI.DrawTexture(lRect, Constants.Lock);
			}

			if (Widgets.ButtonInvisible(rect))
			{
				selected = area;
				selectedIndex = idx;
				GUI.FocusControl("area-name");
			}
		}

		public static void Label(Rect rect, string key)
		{
			var lRect = rect;
			lRect.xMin -= 1;
			lRect.yMin -= 5;
			lRect.height += 5;
			Text.Anchor = TextAnchor.UpperLeft;
			_ = Widgets.LabelFit(lRect, GenText.CapitalizeAsTitle(key.Translate()));
		}

		public static string ToStringHuman(this AreaRiskMode mode)
		{
			return mode switch
			{
				AreaRiskMode.Ignore => "Ignore".Translate(),
				AreaRiskMode.ColonistInside => "ColonistInside".Translate(),
				AreaRiskMode.ColonistOutside => "ColonistOutside".Translate(),
				AreaRiskMode.ZombieInside => "ZombieInside".Translate(),
				AreaRiskMode.ZombieOutside => "ZombieOutside".Translate(),
				_ => null,
			};
		}

		public static AreaRiskMode GetMode(Area area) => ZombieSettings.Values.dangerousAreas.TryGetValue(area, AreaRiskMode.Ignore);

		public static void ZombieMode(Rect rect)
		{
			var currentMode = GetMode(selected);
			if (Widgets.ButtonText(rect, currentMode.ToStringHuman()))
			{
				var options = new List<FloatMenuOption>();
				foreach (var choice in Enum.GetValues(typeof(AreaRiskMode)))
				{
					var newMode = (AreaRiskMode)choice;
					options.Add(new FloatMenuOption(newMode.ToStringHuman(), delegate ()
					{
						if (newMode != currentMode)
						{
							if (newMode == AreaRiskMode.Ignore)
								_ = ZombieSettings.Values.dangerousAreas.Remove(selected);
							else
								ZombieSettings.Values.dangerousAreas[selected] = newMode;
						}
					},
					MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		public static void RenderSelectedRowContent(Area area)
		{
			var left = 198 + 18;
			var width = 197;

			var lRect = new Rect(left, 0, width, 17);
			Label(lRect, "Title");
			var tRect = new Rect(left, 17, width, 27);
			Widgets.DrawBoxSolid(tRect, background);

			Text.Anchor = TextAnchor.MiddleLeft;
			if (area.Mutable)
			{
				GUI.SetNextControlName("area-name");
				var newLabel = GUI.TextField(tRect, area.Label, textFieldStyle);
				if (newLabel.Length > 28)
					newLabel = newLabel.Substring(0, 28);
				if (newLabel != area.Label)
					area.RenamableLabel = newLabel;
			}
			else
			{
				lRect = tRect;
				lRect.xMin += 7;
				lRect.yMin += 1;
				Widgets.Label(lRect, area.Label);
			}

			lRect = new Rect(left, 59, width, 17);
			Label(lRect, "AreaLower");
			var cRect = new Rect(left, 76, width, 27);
			Widgets.DrawBoxSolid(cRect, area.Color);

			cRect = new Rect(left, 109, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.red);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newRed = Tools.HorizontalSlider(cRect, area.Color.r, 0f, 1f);
			if (area is Area_Allowed allowed1)
			{
				var color = allowed1.Color;
				color.r = newRed;
				allowed1.SetColor(color);
			}

			cRect = new Rect(left, 129, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.green);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newGreen = Tools.HorizontalSlider(cRect, area.Color.g, 0f, 1f);
			if (area is Area_Allowed allowed2)
			{
				var color = allowed2.Color;
				color.g = newGreen;
				allowed2.SetColor(color);
			}

			cRect = new Rect(left, 149, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.blue);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newBlue = Tools.HorizontalSlider(cRect, area.Color.b, 0f, 1f);
			if (area is Area_Allowed allowed3)
			{
				var color = allowed3.Color;
				color.b = newBlue;
				allowed3.SetColor(color);
			}

			lRect = new Rect(left, 178, width, 17);
			Label(lRect, "Contents");
			var bRect = new Rect(left, 196, width, 27);
			if (Tools.ButtonText(bRect, "InvertArea".Translate(), area.Mutable, Color.white, inactiveTextColor))
				area.Invert();

			lRect = new Rect(left, 238, width, 17);
			Label(lRect, "ShowZombieRisk");
			bRect = new Rect(left, 256, width, 27);
			ZombieMode(bRect);
		}
	}
}
