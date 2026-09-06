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
	public class ParcelUnboxTracker : MonoBehaviour
	{
		public string BoxName;
		public string PartName;
		public int ItemIndex = -1;
		public Vector3 LastPosition;
		public bool WasTriggered;
		public static bool isApplicationQuitting;

		private void Awake()
		{
			LastPosition = transform.position;
		}

		private void OnApplicationQuit()
		{
			isApplicationQuitting = true;
		}

		private void Update()
		{
			if (transform.position.sqrMagnitude > 0.1f)
			{
				LastPosition = transform.position;
			}
		}

		public void TriggerUnbox()
		{
			if (WasTriggered) return;
			if (gameObject != null && gameObject.name.IndexOf("__HandVisual", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				WasTriggered = true;
				return;
			}
			WasTriggered = true;

			if (NetPartsDeliverySync.Instance != null &&
				!NetPartsDeliverySync.Instance.IsNetworkApplying &&
				!NetPartsDeliverySync.Instance.isSceneResetting &&
				!NetPartsDeliverySync.Instance.IsParcelSuppressed(gameObject.GetInstanceID()))
			{
				Vector3 pos = (LastPosition.sqrMagnitude > 0.1f) ? LastPosition : transform.position;
				NetPartsDeliverySync.Instance.StartCoroutine(NetPartsDeliverySync.Instance.InitiatorUnboxCoroutine(pos, gameObject, BoxName ?? gameObject.name, ItemIndex));
			}
		}

		private void OnDestroy()
		{
			if (isApplicationQuitting || Application.isLoadingLevel) return;
			if (Application.loadedLevelName != "GAME") return;
			if (NetPartsDeliverySync.Instance == null || NetPartsDeliverySync.Instance.isSceneResetting) return;

			if (!WasTriggered)
			{
				TriggerUnbox();
			}
		}
	}

	public static class AvatarBoneHelper
	{
		public static int GetHashFNV_1a(this string s)
		{
			if (string.IsNullOrEmpty(s)) return 0;
			uint num = 2166136261u;
			for (int i = 0; i < s.Length; i++)
			{
				num ^= (uint)s[i];
				num *= 16777619u;
			}
			return (int)num;
		}

		public static Transform FindPlayerPelvis(Player player)
		{
			if (player == null || player.player == null) return null;
			Transform t = player.player.transform;
			return t.Find("char/skeleton/pelvis")
				?? t.Find("pelvis")
				?? FindChildRecursive(t, "pelvis")
				?? t;
		}

		public static Transform FindPlayerHandRight(Player player)
		{
			if (player == null || player.player == null) return null;
			Transform t = player.player.transform;
			return t.Find("char/skeleton/pelvis/RotationBendPivot/spine_middle/spine_upper/collar_right/shoulder_right/arm_right/hand_right")
				?? t.Find("pelvis/RotationBendPivot/spine_middle/spine_upper/collar_right/shoulder_right/arm_right/hand_right")
				?? FindChildRecursive(t, "hand_right")
				?? t;
		}

		public static Transform FindChildRecursive(Transform parent, string name)
		{
			if (parent == null) return null;
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform child = parent.GetChild(i);
				if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase)) return child;
				Transform found = FindChildRecursive(child, name);
				if (found != null) return found;
			}
			return null;
		}
	}

	public class CheatSpawnedItemSync : MonoBehaviour
	{
		public static CheatSpawnedItemSync Instance;
		public static bool isNetworkApplying;
		public static readonly Dictionary<string, CheatSpawnedItemSync> RegisteredItems = new Dictionary<string, CheatSpawnedItemSync>(StringComparer.OrdinalIgnoreCase);

		private static readonly PropertyInfo GrabbedRbProp = typeof(GameScene).Assembly.GetType("WreckMP.PlayerGrabbingManager")?.GetProperty("GrabbedRigidbody", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
		private static readonly FieldInfo AnimMgrField = typeof(Player).GetField("playerAnimationManager", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo AnimGrabMethod = typeof(GameScene).Assembly.GetType("WreckMP.PlayerAnimationManager")?.GetMethod("GrabItem", BindingFlags.Instance | BindingFlags.Public);

		public bool IsManager = false;
		public string ItemId;
		public Rigidbody rb;
		public bool isHeldByRemote;
		public ulong heldByRemoteSteamId;
		public Transform followTarget;
		public Vector3 followOffsetPos;
		public Quaternion followOffsetRot;
		private bool wasHeldLocally;
		private float nextHeldCheckTime;

		private GameEvent itemPickedUpEvent;
		private GameEvent itemDroppedEvent;

		private void Awake()
		{
			if (IsManager)
			{
				Instance = this;
			}
		}

		private void Start()
		{
			if (IsManager)
			{
				itemPickedUpEvent = new GameEvent("Cheat_ItemPickedUp", OnReceiveItemPickedUp);
				itemDroppedEvent = new GameEvent("Cheat_ItemDropped", OnReceiveItemDropped);
				WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Combine(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			}
			else
			{
				if (rb == null)
				{
					rb = GetComponent<Rigidbody>() ?? GetComponentInChildren<Rigidbody>();
				}
				if (!string.IsNullOrEmpty(ItemId))
				{
					RegisteredItems[ItemId] = this;
				}
			}
		}

		private void OnDestroy()
		{
			if (IsManager)
			{
				WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Remove(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			}
			if (!string.IsNullOrEmpty(ItemId) && RegisteredItems.ContainsKey(ItemId) && RegisteredItems[ItemId] == this)
			{
				RegisteredItems.Remove(ItemId);
			}
		}

		private void OnMemberExit(ulong steamId)
		{
			try
			{
				List<CheatSpawnedItemSync> toRelease = new List<CheatSpawnedItemSync>();
				foreach (var kvp in RegisteredItems)
				{
					var item = kvp.Value;
					if (item == null || item.gameObject == null) continue;
					if (item.isHeldByRemote && item.heldByRemoteSteamId == steamId)
					{
						toRelease.Add(item);
					}
				}

				for (int i = 0; i < toRelease.Count; i++)
				{
					var item = toRelease[i];
					item.isHeldByRemote = false;
					item.heldByRemoteSteamId = 0;
					item.followTarget = null;
					item.transform.parent = null;
					if (item.rb != null)
					{
						item.rb.isKinematic = false;
						item.rb.WakeUp();
					}
					foreach (var c in item.GetComponentsInChildren<Collider>(true)) c.enabled = true;
					foreach (var r in item.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
					BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(item.gameObject);
					ExtendedSyncDebugHUD.Log("<color=yellow>[DISCONNECT]: Предмет " + item.ItemId + " спасён от удаления</color>");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[CheatSpawnedItemSync] OnMemberExit error: " + ex.Message);
			}
		}

		public void OnSceneReset()
		{
			RegisteredItems.Clear();
		}

		private static readonly Func<Rigidbody> GrabbedRbGetter = BuildGrabbedGetter();
		private static Func<Rigidbody> BuildGrabbedGetter()
		{
			try
			{
				MethodInfo m = (GrabbedRbProp != null) ? GrabbedRbProp.GetGetMethod(true) : null;
				if (m != null)
				{
					return (Func<Rigidbody>)Delegate.CreateDelegate(typeof(Func<Rigidbody>), m);
				}
			}
			catch { }
			return null;
		}

		public static Rigidbody GetGrabbedRigidbody()
		{
			Func<Rigidbody> getter = GrabbedRbGetter;
			if (getter != null)
			{
				try { return getter(); } catch { return null; }
			}
			try
			{
				return (Rigidbody)GrabbedRbProp?.GetValue(null, null);
			}
			catch
			{
				return null;
			}
		}

		public static void SetPartnerGrabbedItem(Player partner, Rigidbody targetRb)
		{
			if (partner == null) return;
			try
			{
				object animMgr = AnimMgrField?.GetValue(partner);
				if (animMgr != null && AnimGrabMethod != null)
				{
					AnimGrabMethod.Invoke(animMgr, new object[] { targetRb });
				}
			}
			catch
			{
			}
		}

		public static CheatSpawnedItemSync AttachToSpawned(GameObject go, string id)
		{
			if (go == null) return null;
			CheatSpawnedItemSync sync = go.GetComponent<CheatSpawnedItemSync>() ?? go.AddComponent<CheatSpawnedItemSync>();
			sync.IsManager = false;
			sync.ItemId = id;
			sync.followTarget = null;
			sync.isHeldByRemote = false;
			sync.heldByRemoteSteamId = 0;
			sync.rb = go.GetComponent<Rigidbody>() ?? go.GetComponentInChildren<Rigidbody>();
			if (sync.rb == null)
			{
				sync.rb = go.AddComponent<Rigidbody>();
				sync.rb.mass = 0.2f;
			}
			Collider col = go.GetComponent<Collider>() ?? go.GetComponentInChildren<Collider>();
			if (col == null)
			{
				BoxCollider box = go.AddComponent<BoxCollider>();
				box.size = new Vector3(0.25f, 0.02f, 0.15f);
			}
			RegisteredItems[id] = sync;

			if (sync.rb != null)
			{
				try
				{
					int hash = id.GetHashFNV_1a();
					if (NetRigidbodyManager.GetRigidbodyHash(sync.rb) == 0)
					{
						NetRigidbodyManager.AddRigidbody(sync.rb, hash);
					}
				}
				catch (Exception ex)
				{
					ModConsole.Error("[CheatSpawnedItemSync] AddRigidbody error: " + ex.Message);
				}
			}
			return sync;
		}

		public static CheatSpawnedItemSync FindItem(string id)
		{
			if (string.IsNullOrEmpty(id)) return null;
			if (RegisteredItems.TryGetValue(id, out var item) && item != null)
			{
				return item;
			}
			CheatSpawnedItemSync[] all = UnityEngine.Object.FindObjectsOfType<CheatSpawnedItemSync>();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] != null && string.Equals(all[i].ItemId, id, StringComparison.OrdinalIgnoreCase))
				{
					RegisteredItems[id] = all[i];
					return all[i];
				}
			}
			return null;
		}

		private void Update()
		{
			if (string.IsNullOrEmpty(ItemId) || isNetworkApplying) return;

			if (isHeldByRemote)
			{
				if (followTarget != null)
				{
					transform.position = followTarget.TransformPoint(followOffsetPos);
					transform.rotation = followTarget.rotation * followOffsetRot;
				}
				else
				{
					// Рука/игрок уничтожился ДО OnMemberExit — автоспасение!
					isHeldByRemote = false;
					heldByRemoteSteamId = 0;
					followTarget = null;
					if (rb != null)
					{
						rb.isKinematic = false;
						rb.WakeUp();
					}
					foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
					foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = true;
					BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(gameObject);
					ExtendedSyncDebugHUD.Log("<color=yellow>[AUTO-RESCUE]: Предмет " + ItemId + " спасён от удаления</color>");
				}
				return;
			}

			if (Time.time < nextHeldCheckTime) return;
			nextHeldCheckTime = Time.time + 0.1f;

			bool isHeld = CheckIsHeldLocally();
			if (isHeld && !wasHeldLocally)
			{
				wasHeldLocally = true;
				isHeldByRemote = false;
				followTarget = null;
				BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(gameObject);
				BroadcastItemPickedUp(ItemId, WreckMPGlobals.UserID);
			}
			else if (!isHeld && wasHeldLocally)
			{
				wasHeldLocally = false;
				Vector3 pos = transform.position;
				Quaternion rot = transform.rotation;
				Vector3 vel = (rb != null) ? rb.velocity : Vector3.zero;
				BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(gameObject);
				BroadcastItemDropped(ItemId, pos, rot, vel);
			}
			else if (!isHeld && !wasHeldLocally && transform.parent == null)
			{
				if (rb != null && rb.isKinematic)
				{
					rb.isKinematic = false;
					rb.WakeUp();
				}
			}
		}

		private bool CheckIsHeldLocally()
		{
			if (isHeldByRemote) return false;

			Rigidbody grabbed = GetGrabbedRigidbody();
			if (grabbed != null && rb != null && grabbed == rb)
			{
				return true;
			}

			Transform p = transform.parent;
			if (p != null)
			{
				Transform root = p.root;
				if (root != null && root.name.StartsWith("FPSPlayer", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				while (p != null)
				{
					string n = p.name;
					if (n.IndexOf("PickUpSlot", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("ItemPivot", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return true;
					}
					p = p.parent;
				}
			}
			return false;
		}

		public static void BroadcastItemPickedUp(string id, ulong steamId)
		{
			if (isNetworkApplying || Instance == null || Instance.itemPickedUpEvent == null) return;
			using (GameEventWriter writer = Instance.itemPickedUpEvent.Writer())
			{
				writer.Write(id ?? "");
				writer.Write(steamId);
				Instance.itemPickedUpEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [SPAWN]: Поднят предмет " + id + "</color>");
		}

		public static void BroadcastItemDropped(string id, Vector3 pos, Quaternion rot, Vector3 vel)
		{
			if (isNetworkApplying || Instance == null || Instance.itemDroppedEvent == null) return;
			using (GameEventWriter writer = Instance.itemDroppedEvent.Writer())
			{
				writer.Write(id ?? "");
				writer.Write(pos.x);
				writer.Write(pos.y);
				writer.Write(pos.z);
				writer.Write(rot.x);
				writer.Write(rot.y);
				writer.Write(rot.z);
				writer.Write(rot.w);
				writer.Write(vel.x);
				writer.Write(vel.y);
				writer.Write(vel.z);
				Instance.itemDroppedEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [SPAWN]: Брошен предмет " + id + "</color>");
		}

		private void OnReceiveItemPickedUp(GameEventReader reader)
		{
			string id = reader.ReadString();
			ulong steamId = reader.ReadUInt64();
			if (steamId == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				CheatSpawnedItemSync item = FindItem(id);
				if (item == null)
				{
					ExtendedSyncDebugHUD.Log("<color=#ffaa00>IN [SPAWN]: Предмет " + id + " не найден локально — событие пропущено</color>");
					return;
				}

				// 1) Резолв игрока: словарь + fallback по сцене (как в JonnezPassengerSystem).
				Player partner = WreckMPGlobals.Players.ContainsKey(steamId) ? WreckMPGlobals.Players[steamId] : null;
				if (partner == null)
				{
					Player[] allPlayers = UnityEngine.Object.FindObjectsOfType<Player>();
					if (allPlayers != null)
					{
						for (int p = 0; p < allPlayers.Length; p++)
						{
							if (allPlayers[p] != null && allPlayers[p].SteamID == steamId)
							{
								partner = allPlayers[p];
								break;
							}
						}
					}
				}
				if (partner == null)
				{
					// Race: игрок не резолвлен. НЕ помечаем предмет — иначе первый же
					// Update() сделает AUTO-RESCUE и захватит физику при живом держателе.
					// Деградация: предмет лежит на месте до drop-события. Приемлемо.
					ExtendedSyncDebugHUD.Log("<color=#ffaa00>IN [SPAWN]: Игрок " + steamId + " не резолвлен — поднятие " + id + " пропущено без пометки</color>");
					return;
				}

				Transform hand = AvatarBoneHelper.FindPlayerHandRight(partner);
				if (hand == null)
				{
					ExtendedSyncDebugHUD.Log("<color=#ffaa00>IN [SPAWN]: Рука игрока " + steamId + " не найдена — поднятие " + id + " пропущено</color>");
					return;
				}

				// 2) Все проверки пройдены — только теперь помечаем.
				// followTarget гарантированно валиден, AUTO-RESCUE сработает только при
				// реальной потере держателя.
				item.isHeldByRemote = true;
				item.heldByRemoteSteamId = steamId;
				item.wasHeldLocally = false;
				item.followTarget = hand;

				bool isEnvelope = id.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0;
				item.followOffsetPos = isEnvelope ? new Vector3(0.05f, 0.04f, 0.08f) : new Vector3(0f, 0.05f, 0.1f);
				item.followOffsetRot = isEnvelope ? Quaternion.Euler(0f, 90f, 15f) : Quaternion.identity;

				// 3) Kinematic ДО телепорта позиции.
				if (item.rb != null)
				{
					item.rb.isKinematic = true;
					item.rb.velocity = Vector3.zero;
					item.rb.angularVelocity = Vector3.zero;
				}
				item.transform.position = hand.TransformPoint(item.followOffsetPos);
				item.transform.rotation = hand.rotation * item.followOffsetRot;

				// 4) Коллайдеры off — follow-предмет не сталкивается с миром.
				Collider[] cols = item.GetComponentsInChildren<Collider>(true);
				for (int c = 0; c < cols.Length; c++)
				{
					if (cols[c] != null) cols[c].enabled = false;
				}

				// 5) Убираем визуальный дубликат, если UniversalHandItemSync его уже создал
				//    (порядок прихода Cheat_ItemPickedUp и Sync_PlayerHandItem не гарантирован).
				if (UniversalHandItemSync.Instance != null)
				{
					UniversalHandItemSync.Instance.ClearHandVisualFor(steamId);
				}

				SetPartnerGrabbedItem(partner, item.rb);
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [SPAWN]: Игрок " + partner.PlayerName + " поднял " + id + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[CheatSpawnedItemSync] Ошибка OnReceiveItemPickedUp: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceiveItemDropped(GameEventReader reader)
		{
			string id = reader.ReadString();
			Vector3 pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Quaternion rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Vector3 vel = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			if (reader.sender == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				CheatSpawnedItemSync item = FindItem(id);
				if (item != null)
				{
					item.isHeldByRemote = false;
					item.heldByRemoteSteamId = 0;
					item.followTarget = null;
					item.transform.parent = null;
					item.transform.position = pos;
					item.transform.rotation = rot;
					if (item.rb != null)
					{
						item.rb.isKinematic = false;
						item.rb.velocity = vel;
						item.rb.WakeUp();
					}
					Collider[] cols = item.GetComponentsInChildren<Collider>(true);
					for (int c = 0; c < cols.Length; c++)
					{
						if (cols[c] != null) cols[c].enabled = true;
					}
					BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(item.gameObject);
					BetterCheatBoxSyncManager.UpdateNetRigidbodyCache(item.gameObject, pos, rot);
					if (WreckMPGlobals.Players.TryGetValue(reader.sender, out var dropper) && dropper != null)
					{
						SetPartnerGrabbedItem(dropper, null);
					}
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [SPAWN]: Предмет " + id + " сброшен на позиции</color>");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[CheatSpawnedItemSync] Ошибка OnReceiveItemDropped: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}

	public class UniversalHandItemSync : MonoBehaviour
	{
		public static UniversalHandItemSync Instance;
		public static bool isNetworkApplying;

		private GameEvent handItemEvent;

		private string lastHeldItemName = "";
		private bool wasHolding = false;
		private float nextScanTime = 0f;

		private static readonly Dictionary<ulong, GameObject> RemoteHandVisuals = new Dictionary<ulong, GameObject>();
		private static readonly Dictionary<string, GameObject> TemplateCache = new Dictionary<string, GameObject>();
		private static readonly HashSet<string> templateMissCache = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private static Transform cachedPickUpSlot;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			try
			{
				handItemEvent = new GameEvent("Sync_PlayerHandItem", OnReceivePlayerHandItem);
				WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Combine(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			}
			catch (Exception ex)
			{
				ModConsole.Error("[UniversalHandItemSync] Start error: " + ex.Message);
			}
		}

		private void OnDestroy()
		{
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Remove(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
		}

		private void OnMemberExit(ulong steamId)
		{
			try
			{
				if (RemoteHandVisuals.TryGetValue(steamId, out GameObject visual) && visual != null)
				{
					UnityEngine.Object.Destroy(visual);
					RemoteHandVisuals.Remove(steamId);
				}
			}
			catch { }
		}

		public void OnSceneReset()
		{
			lastHeldItemName = "";
			wasHolding = false;
			cachedPickUpSlot = null;
			TemplateCache.Clear();
			templateMissCache.Clear();
			ClearAllRemoteVisuals();
		}

		private void ClearAllRemoteVisuals()
		{
			try
			{
				foreach (var kvp in RemoteHandVisuals)
				{
					if (kvp.Value != null)
					{
						UnityEngine.Object.Destroy(kvp.Value);
					}
				}
				RemoteHandVisuals.Clear();
			}
			catch { }
		}

		public void ClearHandVisualByName(string partName)
		{
			try
			{
				List<ulong> toRemove = new List<ulong>();
				foreach (var kvp in RemoteHandVisuals)
				{
					if (kvp.Value != null && kvp.Value.name.IndexOf(partName, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						UnityEngine.Object.Destroy(kvp.Value);
						toRemove.Add(kvp.Key);
					}
				}
				for (int i = 0; i < toRemove.Count; i++)
				{
					RemoteHandVisuals.Remove(toRemove[i]);
				}
			}
			catch { }
		}

		public void ClearHandVisualFor(ulong steamId)
		{
			try
			{
				if (RemoteHandVisuals.TryGetValue(steamId, out GameObject visual) && visual != null)
				{
					UnityEngine.Object.Destroy(visual);
				}
				RemoteHandVisuals.Remove(steamId);
			}
			catch { }
		}

		public static Transform GetPickUpSlot()
		{
			if (cachedPickUpSlot != null && cachedPickUpSlot.gameObject != null)
			{
				return cachedPickUpSlot;
			}
			try
			{
				GameObject player = GameObject.Find("FPSPlayer") ?? GameObject.Find("PLAYER");
				if (player != null)
				{
					Transform[] all = player.GetComponentsInChildren<Transform>(true);
					for (int i = 0; i < all.Length; i++)
					{
						if (all[i] == null) continue;
						string n = all[i].name;
						if (string.Equals(n, "PickUpSlot", StringComparison.OrdinalIgnoreCase) ||
							string.Equals(n, "1Holder", StringComparison.OrdinalIgnoreCase) ||
							string.Equals(n, "PickUp", StringComparison.OrdinalIgnoreCase) ||
							string.Equals(n, "ItemPivot", StringComparison.OrdinalIgnoreCase))
						{
							cachedPickUpSlot = all[i];
							return cachedPickUpSlot;
						}
					}
				}
			}
			catch { }
			return null;
		}

		public GameObject GetLocallyHeldItem()
		{
			try
			{
				Transform slot = GetPickUpSlot();
				if (slot != null && slot.childCount > 0)
				{
					for (int i = 0; i < slot.childCount; i++)
					{
						Transform child = slot.GetChild(i);
						if (child != null && child.gameObject != null && child.gameObject.activeInHierarchy)
						{
							string n = child.name;
							if (n.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) < 0 &&
								n.IndexOf("PickUp", StringComparison.OrdinalIgnoreCase) < 0 &&
								n.IndexOf("Pivot", StringComparison.OrdinalIgnoreCase) < 0 &&
								n.IndexOf("GUI", StringComparison.OrdinalIgnoreCase) < 0)
							{
								return child.gameObject;
							}
						}
					}
				}

				Rigidbody grabbedRb = CheatSpawnedItemSync.GetGrabbedRigidbody();
				if (grabbedRb != null && grabbedRb.gameObject != null && grabbedRb.gameObject.activeInHierarchy)
				{
					return grabbedRb.gameObject;
				}
			}
			catch { }
			return null;
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME" || isNetworkApplying)
			{
				return;
			}

			if (Time.time < nextScanTime)
			{
				return;
			}
			nextScanTime = Time.time + 0.1f;

			try
			{
				GameObject held = GetLocallyHeldItem();
				bool isHolding = (held != null);
				string currentItemName = isHolding ? GetCleanItemName(held.name) : "";

				if (isHolding != wasHolding || (isHolding && currentItemName != lastHeldItemName))
				{
					wasHolding = isHolding;
					lastHeldItemName = currentItemName;

					Vector3 localPos = isHolding ? held.transform.localPosition : Vector3.zero;
					Quaternion localRot = isHolding ? held.transform.localRotation : Quaternion.identity;

					BroadcastHandItem(WreckMPGlobals.UserID, currentItemName, isHolding, localPos, localRot);
				}
			}
			catch { }
		}

		public static string GetCleanItemName(string rawName)
		{
			if (string.IsNullOrEmpty(rawName)) return "";
			return rawName.Replace("(Clone)", "").Replace("(itemx)", "").Replace("(item)", "").Trim();
		}

		public void BroadcastHandItem(ulong steamId, string itemName, bool isHolding, Vector3 localPos, Quaternion localRot)
		{
			if (isNetworkApplying || handItemEvent == null) return;
			try
			{
				using (GameEventWriter writer = handItemEvent.Writer())
				{
					writer.Write(steamId);
					writer.Write(isHolding);
					writer.Write(itemName ?? "");
					writer.Write(localPos.x);
					writer.Write(localPos.y);
					writer.Write(localPos.z);
					writer.Write(localRot.x);
					writer.Write(localRot.y);
					writer.Write(localRot.z);
					writer.Write(localRot.w);
					handItemEvent.Send(writer, 0uL, safe: true);
				}
				ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [HAND]: " + (isHolding ? ("В руке: " + itemName) : "Рука пуста") + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[UniversalHandItemSync] Broadcast error: " + ex.Message);
			}
		}

		private void OnReceivePlayerHandItem(GameEventReader reader)
		{
			ulong steamId = reader.ReadUInt64();
			bool isHolding = reader.ReadBoolean();
			string itemName = reader.ReadString();
			Vector3 localPos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Quaternion localRot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			if (steamId == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				Player partner = null;
				if (WreckMPGlobals.Players.ContainsKey(steamId))
				{
					partner = WreckMPGlobals.Players[steamId];
				}
				if (partner == null)
				{
					Player[] allPlayers = UnityEngine.Object.FindObjectsOfType<Player>();
					if (allPlayers != null)
					{
						for (int pIdx = 0; pIdx < allPlayers.Length; pIdx++)
						{
							if (allPlayers[pIdx] != null && allPlayers[pIdx].SteamID == steamId)
							{
								partner = allPlayers[pIdx];
								break;
							}
						}
					}
				}
				if (partner == null) return;

				Transform handRight = AvatarBoneHelper.FindPlayerHandRight(partner);
				if (handRight == null) return;

				if (RemoteHandVisuals.TryGetValue(steamId, out GameObject existingVisual) && existingVisual != null)
				{
					UnityEngine.Object.Destroy(existingVisual);
					RemoteHandVisuals.Remove(steamId);
				}

				if (isHolding && !string.IsNullOrEmpty(itemName))
				{
					foreach (var kvp in CheatSpawnedItemSync.RegisteredItems)
					{
						var it = kvp.Value;
						if (it == null || it.gameObject == null) continue;
						if (it.isHeldByRemote && it.heldByRemoteSteamId == steamId)
						{
							ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [HAND]: Напарник держит реальный предмет " + it.ItemId + " (визуал не дублируется)</color>");
							return;
						}
					}
					GameObject template = FindVisualTemplate(itemName);
					if (template != null)
					{
						GameObject visual = UnityEngine.Object.Instantiate(template);
						visual.name = "__HandVisual_" + itemName;

						Component[] allComponents = visual.GetComponentsInChildren<Component>(true);
						for (int i = 0; i < allComponents.Length; i++)
						{
							Component c = allComponents[i];
							if (c == null) continue;
							if (c is Transform || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)
							{
								continue;
							}
							UnityEngine.Object.Destroy(c);
						}

						visual.layer = 2;
						visual.tag = "Untagged";
						Transform[] allChilds = visual.GetComponentsInChildren<Transform>(true);
						for (int j = 0; j < allChilds.Length; j++)
						{
							if (allChilds[j] != null)
							{
								allChilds[j].gameObject.layer = 2;
								allChilds[j].gameObject.tag = "Untagged";
							}
						}

						visual.transform.parent = handRight;
						// Пересчёт world-scale: при неединичном scale руки локальный scale шаблона искажается
						Vector3 templateWorldScale = template.transform.lossyScale;
						Vector3 parentWorldScale = handRight.lossyScale;
						visual.transform.localScale = new Vector3(
							templateWorldScale.x / Mathf.Max(Mathf.Abs(parentWorldScale.x), 0.0001f),
							templateWorldScale.y / Mathf.Max(Mathf.Abs(parentWorldScale.y), 0.0001f),
							templateWorldScale.z / Mathf.Max(Mathf.Abs(parentWorldScale.z), 0.0001f));

						if (itemName.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							visual.transform.localPosition = new Vector3(0.05f, 0.04f, 0.08f);
							visual.transform.localRotation = Quaternion.Euler(0f, 90f, 15f);
						}
						else if (localPos.sqrMagnitude > 0.0001f)
						{
							visual.transform.localPosition = localPos;
							visual.transform.localRotation = localRot;
						}
						else
						{
							visual.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
							visual.transform.localRotation = Quaternion.identity;
						}

						visual.SetActive(true);
						RemoteHandVisuals[steamId] = visual;
						ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [HAND]: Напарник держит " + itemName + "</color>");
					}
					else
					{
						ExtendedSyncDebugHUD.Log("<color=#ffaa00>IN [HAND]: Напарник взял " + itemName + " (визуал в поиске)</color>");
					}
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [HAND]: Напарник убрал предмет из рук</color>");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[UniversalHandItemSync] OnReceive error: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private GameObject FindVisualTemplate(string itemName)
		{
			if (string.IsNullOrEmpty(itemName)) return null;
			if (templateMissCache.Contains(itemName)) return null;
			if (TemplateCache.TryGetValue(itemName, out GameObject cached) && cached != null)
			{
				return cached;
			}

			try
			{
				string clean = GetCleanItemName(itemName);
				GameObject found = GameObject.Find(itemName) ?? 
				                   (!string.IsNullOrEmpty(clean) ? GameObject.Find(clean) : null) ?? 
				                   GameObject.Find(clean + "(itemx)") ?? 
				                   GameObject.Find(clean + "(Clone)");
				if (found != null)
				{
					TemplateCache[itemName] = found;
					return found;
				}

				GameObject[] sceneObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
				if (sceneObjects != null)
				{
					for (int i = 0; i < sceneObjects.Length; i++)
					{
						if (sceneObjects[i] == null) continue;
						string sName = sceneObjects[i].name;
						if (sName.StartsWith(itemName, StringComparison.OrdinalIgnoreCase) || 
						    (!string.IsNullOrEmpty(clean) && sName.StartsWith(clean, StringComparison.OrdinalIgnoreCase)))
						{
							TemplateCache[itemName] = sceneObjects[i];
							return sceneObjects[i];
						}
					}
				}

				GameObject[] allResources = Resources.FindObjectsOfTypeAll<GameObject>();
				if (allResources != null)
				{
					for (int j = 0; j < allResources.Length; j++)
					{
						if (allResources[j] == null) continue;
						string rName = allResources[j].name;
						if (rName.StartsWith(itemName, StringComparison.OrdinalIgnoreCase) || 
						    (!string.IsNullOrEmpty(clean) && rName.StartsWith(clean, StringComparison.OrdinalIgnoreCase)))
						{
							TemplateCache[itemName] = allResources[j];
							return allResources[j];
						}
					}
				}

				templateMissCache.Add(itemName);
				return null;
			}
			catch { }

			return null;
		}
	}

}
