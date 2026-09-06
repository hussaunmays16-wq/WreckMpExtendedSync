using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using BetterCheatBoxNamespace;
using Harmony;
using HutongGames.PlayMaker;
using MSCLoader;
using Microsoft.CodeAnalysis;
using Steamworks;
using UnityEngine;
using WreckMP;

namespace WreckMPExtendedSync
{
	public static class PostalChainPatches
	{
		private static readonly HashSet<Transform> allowedRoots = new HashSet<Transform>();
		private static readonly HashSet<Transform> rejectedRoots = new HashSet<Transform>();

		public static void ClearRootCache()
		{
			allowedRoots.Clear();
			rejectedRoots.Clear();
		}

		public static bool IsMonitoredRoot(Transform root)
		{
			if (root == null) return false;
			if (allowedRoots.Contains(root)) return true;
			if (rejectedRoots.Contains(root)) return false;

			string name = root.name;
			if (string.IsNullOrEmpty(name))
			{
				rejectedRoots.Add(root);
				return false;
			}

			if (name.IndexOf("MailBox", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("YellowMailbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("STORE", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("Magazine", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("Sheets", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
				NetPartsDeliverySync.IsParcelBox(name))
			{
				if (allowedRoots.Count > 300)
				{
					allowedRoots.Clear();
				}
				allowedRoots.Add(root);
				return true;
			}

			if (rejectedRoots.Count > 600)
			{
				rejectedRoots.Clear();
			}
			rejectedRoots.Add(root);
			return false;
		}

		public static bool SendEvent_Prefix(PlayMakerFSM __instance, string eventName)
		{
			if (__instance == null || string.IsNullOrEmpty(eventName))
			{
				return true;
			}

			// 1. Оптимизированная проверка чит-бокса (пропуск погоды, магазина, заказа)
			if (BetterCheatBoxSyncManager.Instance != null && 
			    !BetterCheatBoxSyncManager.Instance.isNetworkApplying && 
			    !BetterCheatBoxSyncManager.Instance.suppressSkipPostOrder)
			{
				BetterCheatBox bcb = BetterCheatBoxSyncManager.cachedBcbInstance;
				if (bcb != null)
				{
					if (bcb.cloudFsm != null && __instance == bcb.cloudFsm && string.Equals(eventName, "RANDOMIZE", StringComparison.OrdinalIgnoreCase))
					{
						BetterCheatBoxSyncManager.Instance.BroadcastSkip("WEATHER");
					}
					else if (bcb.inventoryFsm != null && __instance == bcb.inventoryFsm && string.Equals(eventName, "DAY", StringComparison.OrdinalIgnoreCase))
					{
						BetterCheatBoxSyncManager.Instance.BroadcastSkip("RESTOCK");
					}
					else if (bcb.orderFsm != null && __instance == bcb.orderFsm && string.Equals(eventName, "FINISHED", StringComparison.OrdinalIgnoreCase))
					{
						if (Time.time - BetterCheatBoxSyncManager.Instance.lastPostOrderSkipTime >= 2f)
						{
							BetterCheatBoxSyncManager.Instance.BroadcastSkip("POST_ORDER");
						}
					}
				}
			}

			if (NetPartsDeliverySync.Instance == null || NetPartsDeliverySync.Instance.IsNetworkApplying || NetPartsDeliverySync.Instance.isSceneResetting)
			{
				return true;
			}

			Transform root = __instance.transform.root;
			bool isParcel = NetPartsDeliverySync.GetParcelBoxRoot(__instance.gameObject) != null;
			if ((root == null || !IsMonitoredRoot(root)) && !isParcel)
			{
				return true;
			}

			string goName = (__instance.gameObject != null) ? __instance.gameObject.name : "";
			string fsmName = __instance.FsmName ?? "";
			Transform parent = __instance.transform.parent;
			string parentName = (parent != null) ? parent.name : "";

			if (goName.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
				goName.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
				fsmName.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0 ||
				fsmName.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
				parentName.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (string.Equals(eventName, "MAIL", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "SEND", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "Sent", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "Post", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "Envelope", StringComparison.OrdinalIgnoreCase))
				{
					NetPartsDeliverySync.Instance.BroadcastEnvelopeMailed();
				}
			}
			else if (goName.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 goName.IndexOf("PostOrder", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 UniversalHandItemSync.GetCleanItemName(goName).Equals("Bill", StringComparison.OrdinalIgnoreCase) ||
					 fsmName.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 parentName.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (string.Equals(eventName, "PAID", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "BOUGHT", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "BUY", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "Pay", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "CreateItems", StringComparison.OrdinalIgnoreCase))
				{
					NetPartsDeliverySync.Instance?.BroadcastPostOrderPay();
				}
			}
			else if (goName.IndexOf("ButtonOrder", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 fsmName.IndexOf("ButtonOrder", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 parentName.IndexOf("ButtonOrder", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (string.Equals(eventName, "ORDER", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "State 3", StringComparison.OrdinalIgnoreCase))
				{
					NetPartsDeliverySync.Instance.BroadcastOrderPlaced();
				}
			}
			else
			{
				GameObject boxGo = NetPartsDeliverySync.GetParcelBoxRoot(__instance.gameObject);
				if (boxGo != null && NetPartsDeliverySync.IsParcelBox(boxGo.name) && !NetPartsDeliverySync.IsProtectedSceneObject(boxGo))
				{
					if (string.Equals(eventName, "OPEN", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "Assemble", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "Unbox", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "AssembleItems", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "ASSEMBLE", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "Spawn", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "Open", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "1", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "State 2", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "USE", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "ACTIVATE", StringComparison.OrdinalIgnoreCase) ||
						string.Equals(eventName, "Unpack", StringComparison.OrdinalIgnoreCase))
					{
						if (NetPartsDeliverySync.Instance != null &&
							!NetPartsDeliverySync.Instance.IsNetworkApplying &&
							!NetPartsDeliverySync.Instance.isSceneResetting)
						{
							if (!NetPartsDeliverySync.Instance.IsParcelSuppressed(boxGo.GetInstanceID()))
							{
								ParcelUnboxTracker tracker = boxGo.GetComponent<ParcelUnboxTracker>() ?? boxGo.AddComponent<ParcelUnboxTracker>();
								if (!tracker.WasTriggered)
								{
									tracker.WasTriggered = true;
									NetPartsDeliverySync.Instance.StartCoroutine(NetPartsDeliverySync.Instance.InitiatorUnboxCoroutine(boxGo.transform.position, boxGo, boxGo.name, tracker.ItemIndex));
								}
							}
						}
					}
				}
			}
			return true;
		}
	}
	public static class BetterCheatBoxPatches
	{
		private static readonly string[] KnownVehicleNames = new string[]
		{
			"SATSUMA(504kg, 330)",
			"SATSUMA(580kg, 240hp)",
			"HAYOSIKO(1500kg, 250)",
			"GIFU(750/450psi)",
			"FERNDALE(1630kg)",
			"KEKMET(350-400psi)",
			"RCO_RUSCKO12(270)",
			"JONNEZ ES(Clone)",
			"BOAT",
			"FLATBED"
		};

		public static GameObject FindVehicleDirect(string buttonOrVehName)
		{
			if (string.IsNullOrEmpty(buttonOrVehName)) return null;
			GameObject direct = GameObject.Find(buttonOrVehName);
			if (direct != null) return direct;
			for (int i = 0; i < KnownVehicleNames.Length; i++)
			{
				if (KnownVehicleNames[i].IndexOf(buttonOrVehName, StringComparison.OrdinalIgnoreCase) >= 0 ||
				    buttonOrVehName.IndexOf(KnownVehicleNames[i].Substring(0, Math.Min(6, KnownVehicleNames[i].Length)), StringComparison.OrdinalIgnoreCase) >= 0)
				{
					GameObject found = GameObject.Find(KnownVehicleNames[i]);
					if (found != null) return found;
				}
			}
			return null;
		}
		public static bool TPToPlayer_Prefix(BetterCheatBox __instance, TPMeToObject tpMeToObject)
		{
			if (tpMeToObject == null || __instance == null)
			{
				return false;
			}
			Transform p = __instance.player;
			if (p == null) return false;

			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
			if (tpMeToObject.transforms != null)
			{
				if (GUILayout.Button(text, __instance.buttonWidth))
				{
					Vector3 fwd = p.forward;
					for (int i = 0; i < tpMeToObject.transforms.Length; i++)
					{
						Transform transform = tpMeToObject.transforms[i];
						if (transform == null) continue;
						Vector3 targetPos = p.position + fwd * (1.2f + i * 0.35f) + Vector3.up * 0.2f;
						transform.gameObject.SetActive(value: true);
						transform.position = targetPos;
						BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(transform.gameObject);
						BetterCheatBoxSyncManager.Instance?.BroadcastTeleportObject(transform.gameObject, targetPos, transform.rotation, tpMeToObject.buttonName, i);
					}
				}
			}
			else
			{
				if (tpMeToObject.transform == null && tpMeToObject.buttonName != null)
				{
					if (BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.buttonName))
					{
						GameObject v = FindVehicleDirect(tpMeToObject.buttonName);
						if (v != null)
						{
							tpMeToObject.transform = v.transform;
						}
					}
				}

				if (tpMeToObject.transform == null)
				{
					GUILayout.Button($"<size={num}><color=grey>{tpMeToObject.buttonName}</color></size>", __instance.buttonWidth);
				}
				else if (GUILayout.Button(text, __instance.buttonWidth))
				{
					Vector3 fwd = p.forward;
					GameObject go = tpMeToObject.transform.gameObject;
					Vector3 targetPos;
					Quaternion targetRot;
					if (BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.buttonName) || BetterCheatBoxSyncManager.IsVehicleName(go.name))
					{
						targetPos = p.position + fwd * 3.5f + Vector3.up * 0.4f;
						targetRot = Quaternion.LookRotation(fwd);
						go.SetActive(value: true);
						tpMeToObject.transform.position = targetPos;
						tpMeToObject.transform.rotation = targetRot;
						// WreckMP сам отлично синхронизирует транспорт: никакого вмешательства мода в физику/владение!
						return false;
					}
					else
					{
						targetPos = p.position + fwd * 1.5f + Vector3.up * 0.2f;
						targetRot = tpMeToObject.transform.rotation;
						go.SetActive(value: true);
						tpMeToObject.transform.position = targetPos;
						tpMeToObject.transform.rotation = targetRot;
						BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(go);
						BetterCheatBoxSyncManager.Instance?.BroadcastTeleportObject(go, targetPos, targetRot, tpMeToObject.buttonName, -1);
					}
				}
			}
			return false;
		}

		public static bool SpawnAtPlayer_Prefix(BetterCheatBox __instance, TPMeToObject tpMeToObject)
		{
			if (tpMeToObject == null || __instance == null)
			{
				return false;
			}
			Transform p = __instance.player;
			if (p == null) return false;

			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
			if (tpMeToObject.transforms != null)
			{
				if (!GUILayout.Button(text, __instance.buttonWidth))
				{
					return false;
				}
				Vector3 fwd = p.forward;
				Vector3 right = p.right;
				foreach (Transform transform in tpMeToObject.transforms)
				{
					if (transform == null) continue;
					string batchId = WreckMPGlobals.UserID + "_" + Environment.TickCount + "_" + UnityEngine.Random.Range(100, 999);
					for (int j = 0; j < __instance.spawnAmmount; j++)
					{
						Vector3 spawnPos = p.position + fwd * (1.2f + (j / 3) * 0.6f) + right * ((j % 3 - 1) * 0.5f) + Vector3.up * 0.25f;
						GameObject spawned = (GameObject)UnityEngine.Object.Instantiate(transform.gameObject, spawnPos, Quaternion.identity);
						spawned.SetActive(value: true);
						string itemId = string.Format("bcb_{0}_{1}_{2}_{3}", WreckMPGlobals.UserID, tpMeToObject.buttonName, batchId, j);
						CheatSpawnedItemSync.AttachToSpawned(spawned, itemId);
						BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(spawned);
					}
					BetterCheatBoxSyncManager.Instance?.BroadcastSpawnObject(tpMeToObject.buttonName, transform.gameObject.name, __instance.spawnAmmount, p.position, Quaternion.identity, batchId, WreckMPGlobals.UserID);
				}
			}
			else if (tpMeToObject.transform == null)
			{
				GUILayout.Button($"<size={num}><color=grey>{tpMeToObject.buttonName}</color></size>", __instance.buttonWidth);
			}
			else if (GUILayout.Button(text, __instance.buttonWidth))
			{
				Vector3 fwd = p.forward;
				Vector3 right = p.right;
				string batchId = WreckMPGlobals.UserID + "_" + Environment.TickCount + "_" + UnityEngine.Random.Range(100, 999);
				for (int k = 0; k < __instance.spawnAmmount; k++)
				{
					Vector3 spawnPos = p.position + fwd * (1.2f + (k / 3) * 0.6f) + right * ((k % 3 - 1) * 0.5f) + Vector3.up * 0.25f;
					GameObject spawned = (GameObject)UnityEngine.Object.Instantiate(tpMeToObject.transform.gameObject, spawnPos, Quaternion.identity);
					spawned.SetActive(value: true);
					string itemId = string.Format("bcb_{0}_{1}_{2}_{3}", WreckMPGlobals.UserID, tpMeToObject.buttonName, batchId, k);
					CheatSpawnedItemSync.AttachToSpawned(spawned, itemId);
					BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(spawned);
				}
				BetterCheatBoxSyncManager.Instance?.BroadcastSpawnObject(tpMeToObject.buttonName, tpMeToObject.transform.gameObject.name, __instance.spawnAmmount, p.position, Quaternion.identity, batchId, WreckMPGlobals.UserID);
			}
			return false;
		}

		public static bool TPPlayerTo_Prefix(BetterCheatBox __instance, TPMeToObject tpMeToObject)
		{
			if (tpMeToObject == null || __instance == null)
			{
				return false;
			}
			Transform p = __instance.player;
			if (p == null) return false;

			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
			if (tpMeToObject.transform == null && tpMeToObject.buttonName != null && BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.buttonName))
			{
				GameObject v = FindVehicleDirect(tpMeToObject.buttonName);
				if (v != null)
				{
					tpMeToObject.transform = v.transform;
				}
			}
			if (tpMeToObject.transform == null)
			{
				if (tpMeToObject.vector == Vector3.zero)
				{
					GUILayout.Button($"<size={num}><color=grey>{tpMeToObject.buttonName}</color></size>", __instance.buttonWidth);
				}
				else if (GUILayout.Button(text, __instance.buttonWidth))
				{
					BetterCheatBoxSyncManager.Instance?.SafeTeleportLocalPlayer(tpMeToObject.vector, Quaternion.identity, tpMeToObject.buttonName);
				}
			}
			else if (GUILayout.Button(text, __instance.buttonWidth))
			{
				Vector3 targetPos = tpMeToObject.transform.position;
				Quaternion targetRot = tpMeToObject.transform.rotation;
				if (BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.buttonName) || BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.transform.name))
				{
					targetPos += tpMeToObject.transform.right * -1.5f + Vector3.up * 0.1f;
				}
				BetterCheatBoxSyncManager.Instance?.SafeTeleportLocalPlayer(targetPos, targetRot, tpMeToObject.buttonName);
			}
			return false;
		}
	}
}
