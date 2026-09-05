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
	public class SafeFsmWatcher : MonoBehaviour
	{
		private PlayMakerFSM targetFsm;

		private string[] targetStateNames;

		private Action onEnterAction;

		private string previousStateName = "";

		public bool SuppressNextEnter;

		public static SafeFsmWatcher Attach(PlayMakerFSM fsm, string stateName, Action callback)
		{
			return Attach(fsm, new string[1] { stateName }, callback);
		}

		public static SafeFsmWatcher Attach(PlayMakerFSM fsm, string[] stateNames, Action callback)
		{
			if (fsm == null)
			{
				return null;
			}
			SafeFsmWatcher[] components = fsm.gameObject.GetComponents<SafeFsmWatcher>();
			if (components != null)
			{
				for (int i = 0; i < components.Length; i++)
				{
					if (components[i].targetFsm == fsm)
					{
						components[i].targetStateNames = stateNames;
						components[i].onEnterAction = callback;
						return components[i];
					}
				}
			}
			SafeFsmWatcher safeFsmWatcher = fsm.gameObject.AddComponent<SafeFsmWatcher>();
			safeFsmWatcher.targetFsm = fsm;
			safeFsmWatcher.targetStateNames = stateNames;
			safeFsmWatcher.onEnterAction = callback;
			safeFsmWatcher.previousStateName = ((fsm.Fsm != null && !string.IsNullOrEmpty(fsm.ActiveStateName)) ? fsm.ActiveStateName : "");
			return safeFsmWatcher;
		}

		private void Update()
		{
			if (targetFsm == null || targetFsm.Fsm == null)
			{
				return;
			}
			string activeStateName = targetFsm.ActiveStateName;
			if (!string.IsNullOrEmpty(activeStateName) && activeStateName != previousStateName)
			{
				bool flag = false;
				if (targetStateNames != null)
				{
					for (int i = 0; i < targetStateNames.Length; i++)
					{
						string text = targetStateNames[i];
						if (string.IsNullOrEmpty(text))
						{
							continue;
						}
						if (text.Length <= 2)
						{
							if (string.Equals(activeStateName, text, StringComparison.OrdinalIgnoreCase))
							{
								flag = true;
								break;
							}
						}
						else if (string.Equals(activeStateName, text, StringComparison.OrdinalIgnoreCase) || activeStateName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
						{
							flag = true;
							break;
						}
					}
				}
				if (flag)
				{
					if (SuppressNextEnter)
					{
						SuppressNextEnter = false;
					}
					else
					{
						try
						{
							onEnterAction?.Invoke();
						}
						catch (Exception ex)
						{
							ModConsole.Error("[SafeFsmWatcher] Ошибка в колбэке FSM: " + ex.Message);
						}
					}
				}
			}
			previousStateName = activeStateName;
		}
	}

	public class ParcelUnboxTracker : MonoBehaviour
	{
		public string BoxName;
		public string PartName;
		public int ItemIndex = -1;
		public Vector3 LastPosition;
		public bool WasTriggered;

		private void Awake()
		{
			LastPosition = transform.position;
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
			if (!WasTriggered && Application.loadedLevelName == "GAME" && NetPartsDeliverySync.Instance != null && !NetPartsDeliverySync.Instance.isSceneResetting)
			{
				TriggerUnbox();
			}
		}
	}

	public class WreckMPExtendedSync : Mod
	{
		public override string ID => "WreckMPExtendedSync";

		public override string Name => "WreckMP Extended Sync (True Co-op Engine)";

		public override string Author => "Jack & Bean Hacker Syndicate";

		public override string Version => "3.6.4";

		public override string Description => "Полноценная автоматическая синхронизация: пассажир Jonnez (клавиша U), капот Сацумы, чемодан Йоуко, Паятсо, килью, заказ, почта и коробки Теймо.";

		public override void ModSetup()
		{
			SetupFunction(Setup.OnLoad, Mod_OnLoad);
		}

		private void Mod_OnLoad()
		{
			Application.runInBackground = true;
			GameObject gameObject = new GameObject("WreckMP_ExtendedSync_Core");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			gameObject.AddComponent<SceneLifecycleCoordinator>();
			gameObject.AddComponent<ExtendedSyncDebugHUD>();
			gameObject.AddComponent<JonnezPassengerSystem>();
			gameObject.AddComponent<ExtendedVehiclesSync>();
			gameObject.AddComponent<ExtendedEconomySync>();
			gameObject.AddComponent<NetJoukoStorylineManager>();
			gameObject.AddComponent<NetMinigamesSlotManager>();
			gameObject.AddComponent<NetPartsDeliverySync>();
			gameObject.AddComponent<BetterCheatBoxSyncManager>();
			gameObject.AddComponent<CheatSpawnedItemSync>();
			gameObject.AddComponent<NetTelephoneHardwareSync>();
			gameObject.AddComponent<NetUrinationSync>();
			gameObject.AddComponent<NetFlashlightSync>();
			gameObject.AddComponent<UniversalHandItemSync>();
			gameObject.AddComponent<InGameDashboardGUI>();
			try
			{
				HarmonyInstance harmonyInstance = HarmonyInstance.Create("com.jack.wreckmp.extendedsync.lobbyguard");
				MethodInfo methodInfo = typeof(GameScene).Assembly.GetType("WreckMP.SteamNet")?.GetMethod("OnLobbyMemberStateUpdate", BindingFlags.Static | BindingFlags.NonPublic);
				MethodInfo method = typeof(LobbyDisconnectionGuard).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
				if (methodInfo != null && method != null)
				{
					harmonyInstance.Patch(methodInfo, new HarmonyMethod(method));
					ModConsole.Print("<color=green>[LobbyGuard]</color> Защита лобби от ложного самоотключения эмулятора активна!");
				}
			}
			catch
			{
			}
			try
			{
				HarmonyInstance postalHarmony = HarmonyInstance.Create("com.jack.wreckmp.extendedsync.postal");
				MethodInfo sendEventMethod = typeof(PlayMakerFSM).GetMethod("SendEvent", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
				MethodInfo postalPrefix = typeof(PostalChainPatches).GetMethod("SendEvent_Prefix", BindingFlags.Public | BindingFlags.Static);
				if (sendEventMethod != null && postalPrefix != null)
				{
					postalHarmony.Patch(sendEventMethod, new HarmonyMethod(postalPrefix), null, null);
					ModConsole.Print("<color=green>[PostalChain Harmony]</color> Перехватчик почтовой цепочки (MailBox, PostOrderBuy, ButtonOrder) успешно установлен!");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[PostalChain Harmony Error] " + ex.Message);
			}
			ModConsole.Print("<color=green>[WreckMP Extended Sync v3.6.4]</color> Ядро синхронизации успешно запущено (Режим честного P2P)!");
		}
	}
	public static class LobbyDisconnectionGuard
	{
		public static bool Prefix(LobbyChatUpdate_t param)
		{
			if (WreckMPGlobals.IsHost && param.m_ulSteamIDUserChanged == WreckMPGlobals.HostID)
			{
				ExtendedSyncDebugHUD.Log("<color=yellow>[LOBBY GUARD]</color> Заблокировано ложное отключение хоста эмулятором (Код: " + param.m_rgfChatMemberStateChange + ")!");
				return false;
			}
			return true;
		}
	}
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
				name.IndexOf("HAYOSIKO", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("SATSUMA", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("RCO_RUSCKO", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("GIFU", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("FERNDALE", StringComparison.OrdinalIgnoreCase) >= 0 ||
				name.IndexOf("KEKMET", StringComparison.OrdinalIgnoreCase) >= 0)
			{
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
			if (NetPartsDeliverySync.Instance == null || NetPartsDeliverySync.Instance.IsNetworkApplying || NetPartsDeliverySync.Instance.isSceneResetting)
			{
				return true;
			}

			Transform root = __instance.transform.root;
			if (root == null || !IsMonitoredRoot(root))
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
					 fsmName.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0 ||
					 parentName.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (string.Equals(eventName, "PAID", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "BOUGHT", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "BUY", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "Pay", StringComparison.OrdinalIgnoreCase) ||
					string.Equals(eventName, "CreateItems", StringComparison.OrdinalIgnoreCase))
				{
					NetPartsDeliverySync.Instance.BroadcastPostOrderPay();
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

			if (ExtendedVehiclesSync.Instance != null && !ExtendedVehiclesSync.Instance.IsNetworkApplying)
			{
				ExtendedVehiclesSync.Instance.CheckFsmEventForVehicleToggle(__instance, eventName);
			}
			return true;
		}
	}
	public class SceneLifecycleCoordinator : MonoBehaviour
	{
		public static SceneLifecycleCoordinator Instance;

		private string lastScene = "";

		private void Awake()
		{
			Instance = this;
		}

		private void Update()
		{
			string loadedLevelName = Application.loadedLevelName;
			if (loadedLevelName != lastScene)
			{
				lastScene = loadedLevelName;
				OnSceneChanged(loadedLevelName);
			}
		}

		private void OnLevelWasLoaded(int level)
		{
			OnSceneChanged(Application.loadedLevelName);
		}

		public void OnSceneChanged(string sceneName)
		{
			PostalChainPatches.ClearRootCache();
			if (sceneName == "GAME")
			{
				ExtendedSyncDebugHUD.Log("<color=#00ffcc>[LIFECYCLE]</color> Загружена сцена GAME! Перезапуск сетевых хуков...");
				JonnezPassengerSystem.Instance?.OnSceneReset();
				ExtendedVehiclesSync.Instance?.OnSceneReset();
				ExtendedEconomySync.Instance?.OnSceneReset();
				NetJoukoStorylineManager.Instance?.OnSceneReset();
				NetMinigamesSlotManager.Instance?.OnSceneReset();
				NetPartsDeliverySync.Instance?.OnSceneReset();
				BetterCheatBoxSyncManager.Instance?.OnSceneReset();
				CheatSpawnedItemSync.Instance?.OnSceneReset();
				UniversalHandItemSync.Instance?.OnSceneReset();
				NetTelephoneHardwareSync.Instance?.OnSceneReset();
				NetUrinationSync.Instance?.OnSceneReset();
				NetFlashlightSync.Instance?.OnSceneReset();
			}
		}
	}
	public class ExtendedSyncDebugHUD : MonoBehaviour
	{
		private class LogEntry
		{
			public string Text;

			public float TimeLeft;
		}

		public static ExtendedSyncDebugHUD Instance;

		private List<LogEntry> logs = new List<LogEntry>();

		private void Awake()
		{
			Instance = this;
		}

		public static void Log(string message)
		{
			try
			{
				if (Instance != null && Instance.logs != null)
				{
					Instance.logs.Insert(0, new LogEntry
					{
						Text = message,
						TimeLeft = 12f
					});
					if (Instance.logs.Count > 9)
					{
						Instance.logs.RemoveAt(Instance.logs.Count - 1);
					}
				}
				ModConsole.Print("<color=cyan>[WRECKMP SYNC]</color> " + message);
			}
			catch { }
		}

		private void Update()
		{
			try
			{
				for (int num = logs.Count - 1; num >= 0; num--)
				{
					logs[num].TimeLeft -= Time.deltaTime;
					if (logs[num].TimeLeft <= 0f)
					{
						logs.RemoveAt(num);
					}
				}
			}
			catch { }
		}

		private void OnGUI()
		{
			try
			{
				if (logs.Count <= 0)
				{
					return;
				}
				GUI.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
				GUI.color = Color.white;
				GUILayout.BeginArea(new Rect(Screen.width / 2 - 280, Screen.height - 225, 560f, 215f));
				GUILayout.BeginVertical("box");
				GUILayout.Label("<color=#00ffcc><b>★ WRECKMP EXTENDED NETWORK SYNC v3.6.4 ★</b></color>");
				if (GUILayout.Button("⚡ ВОСКРЕСИТЬ САТСУМУ В ГАРАЖ (Ctrl+F9)"))
				{
					Vector3 garagePos = new Vector3(-10.5f, 4.4f, 7.5f);
					Quaternion garageRot = Quaternion.Euler(0, 90f, 0);
					if (BetterCheatBoxSyncManager.ReviveAndTeleportSatsuma(garagePos, garageRot))
					{
						PlayMakerFSM.BroadcastEvent("SATSUMA_REVIVED");
						Log("<color=#00ffcc>⚡ [REVIVE]: Сацума успешно воскрешена в гараж!</color>");
					}
					else
					{
						Log("<color=#ff3333>ERR [REVIVE]: Сацума не найдена в памяти игры!</color>");
					}
				}
				for (int i = 0; i < logs.Count; i++)
				{
					if (logs[i] != null && !string.IsNullOrEmpty(logs[i].Text))
					{
						GUILayout.Label(logs[i].Text);
					}
				}
				GUILayout.EndVertical();
				GUILayout.EndArea();
			}
			catch { }
		}
	}
	public class JonnezPassengerSystem : MonoBehaviour
	{
		public static JonnezPassengerSystem Instance;

		private static readonly FieldInfo PlayerPosField = typeof(Player).GetField("pos", BindingFlags.Instance | BindingFlags.NonPublic);

		private static readonly FieldInfo PlayerRotField = typeof(Player).GetField("rot", BindingFlags.Instance | BindingFlags.NonPublic);

		private GameObject jonnez;

		private Transform passengerPivot;

		public bool isLocalPassenger;

		private GameEvent jonnezPassengerEvent;

		private ulong currentRemotePassengerSteamId;

		private Player cachedRemotePlayer;

		private float nextPlayerLookupTime;

		private GameObject cachedPlayer;

		private CharacterController cachedPlayerCC;

		private float originalNearClip = 0.3f;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			jonnezPassengerEvent = new GameEvent("SyncJonnezPassenger", OnReceivePassengerEvent);
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Combine(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			OnSceneReset();
		}

		private void OnDestroy()
		{
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Remove(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
		}

		private void OnMemberExit(ulong steamId)
		{
			if (currentRemotePassengerSteamId != 0L && currentRemotePassengerSteamId == steamId)
			{
				ExtendedSyncDebugHUD.Log("<color=#ffdd00>[JONNEZ]</color> Пассажир " + steamId + " покинул сессию. Место освобождено.");
				currentRemotePassengerSteamId = 0uL;
				cachedRemotePlayer = null;
			}
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			if (isLocalPassenger)
			{
				if (Camera.main != null)
				{
					Camera.main.nearClipPlane = ((originalNearClip > 0.05f) ? originalNearClip : 0.3f);
				}
				if (cachedPlayer == null)
				{
					cachedPlayer = GameObject.Find("PLAYER");
				}
				if (cachedPlayer != null)
				{
					Vector3 eulerAngles = cachedPlayer.transform.rotation.eulerAngles;
					cachedPlayer.transform.rotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
				}
				if (cachedPlayerCC != null)
				{
					cachedPlayerCC.enabled = true;
				}
			}
			isLocalPassenger = false;
			currentRemotePassengerSteamId = 0uL;
			cachedRemotePlayer = null;
			nextPlayerLookupTime = 0f;
			passengerPivot = null;
			jonnez = null;
			cachedPlayer = null;
			cachedPlayerCC = null;
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyFindJonnez());
			}
		}

		private Transform EnsurePassengerPivot()
		{
			if (passengerPivot != null)
			{
				return passengerPivot;
			}
			if (jonnez == null)
			{
				jonnez = GameObject.Find("JONNEZ ES(Clone)") ?? GameObject.Find("JONNEZ ES");
			}
			if (jonnez != null)
			{
				Transform transform = jonnez.transform.Find("Jonnez_PassengerSeatPivot");
				if (transform != null)
				{
					passengerPivot = transform;
					passengerPivot.localPosition = new Vector3(0f, 0.48f, -0.55f);
					passengerPivot.localRotation = Quaternion.identity;
				}
				else
				{
					GameObject gameObject = new GameObject("Jonnez_PassengerSeatPivot");
					gameObject.transform.parent = jonnez.transform;
					gameObject.transform.localPosition = new Vector3(0f, 0.48f, -0.55f);
					gameObject.transform.localRotation = Quaternion.identity;
					passengerPivot = gameObject.transform;
				}
			}
			return passengerPivot;
		}

		private IEnumerator LazyFindJonnez()
		{
			while (passengerPivot == null)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				if (EnsurePassengerPivot() != null)
				{
					ExtendedSyncDebugHUD.Log("<color=#00ffcc>[JONNEZ]</color> Посадочное место для напарника готово (Координаты смещены назад)!");
					break;
				}
				yield return new WaitForSeconds(2f);
			}
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME")
			{
				return;
			}
			if (cachedPlayer == null)
			{
				cachedPlayer = GameObject.Find("PLAYER");
				if (cachedPlayer != null)
				{
					cachedPlayerCC = cachedPlayer.GetComponent<CharacterController>();
				}
			}
			if (isLocalPassenger)
			{
				if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F) || Input.GetKeyDown(KeyCode.Space))
				{
					DismountLocalPassenger();
				}
				return;
			}
			Transform transform = EnsurePassengerPivot();
			if (cachedPlayer != null && transform != null && Vector3.Distance(cachedPlayer.transform.position, transform.position) < 1.8f && currentRemotePassengerSteamId == 0L && Input.GetKeyDown(KeyCode.U))
			{
				MountLocalPassenger();
			}
		}

		private void LateUpdate()
		{
			if (Application.loadedLevelName != "GAME")
			{
				return;
			}
			Transform transform = EnsurePassengerPivot();
			if (jonnez == null || transform == null)
			{
				return;
			}
			if (cachedPlayer == null)
			{
				cachedPlayer = GameObject.Find("PLAYER");
				if (cachedPlayer != null)
				{
					cachedPlayerCC = cachedPlayer.GetComponent<CharacterController>();
				}
			}
			if (isLocalPassenger)
			{
				if (cachedPlayer == null)
				{
					return;
				}
				if (Vector3.Angle(jonnez.transform.up, Vector3.up) > 60f)
				{
					DismountLocalPassenger();
					ExtendedSyncDebugHUD.Log("<color=#ff3333>[JONNEZ]</color> Мопед упал! Пассажир катапультирован.");
					return;
				}
				if (cachedPlayerCC != null && cachedPlayerCC.enabled)
				{
					cachedPlayerCC.enabled = false;
				}
				cachedPlayer.transform.position = transform.position;
				cachedPlayer.transform.rotation = transform.rotation;
			}
			if (currentRemotePassengerSteamId == 0L)
			{
				return;
			}
			if (cachedRemotePlayer == null)
			{
				cachedRemotePlayer = GetRemotePlayer(currentRemotePassengerSteamId);
			}
			if (!(cachedRemotePlayer != null))
			{
				return;
			}
			cachedRemotePlayer.transform.position = transform.position;
			cachedRemotePlayer.transform.rotation = transform.rotation;
			if (cachedRemotePlayer.player != null)
			{
				cachedRemotePlayer.player.transform.position = transform.position;
				cachedRemotePlayer.player.transform.rotation = transform.rotation;
			}
			try
			{
				if (PlayerPosField != null)
				{
					PlayerPosField.SetValue(cachedRemotePlayer, transform.position);
				}
				if (PlayerRotField != null)
				{
					PlayerRotField.SetValue(cachedRemotePlayer, transform.rotation.eulerAngles);
				}
			}
			catch
			{
			}
		}

		public void MountLocalPassenger()
		{
			Transform transform = EnsurePassengerPivot();
			if (cachedPlayer == null)
			{
				cachedPlayer = GameObject.Find("PLAYER");
			}
			if (cachedPlayer != null && cachedPlayerCC == null)
			{
				cachedPlayerCC = cachedPlayer.GetComponent<CharacterController>();
			}
			if (cachedPlayerCC != null)
			{
				cachedPlayerCC.enabled = false;
			}
			isLocalPassenger = true;
			if (cachedPlayer != null && transform != null)
			{
				cachedPlayer.transform.position = transform.position;
				cachedPlayer.transform.rotation = transform.rotation;
			}
			if (Camera.main != null)
			{
				originalNearClip = Camera.main.nearClipPlane;
				Camera.main.nearClipPlane = 0.05f;
			}
			BroadcastPassengerState(seated: true);
			ExtendedSyncDebugHUD.Log("<color=#00ff00>[JONNEZ]</color> Вы сели на пассажирское место Jonnez [F для высадки]");
		}

		public void DismountLocalPassenger()
		{
			if (!isLocalPassenger)
			{
				return;
			}
			isLocalPassenger = false;
			if (cachedPlayer == null)
			{
				cachedPlayer = GameObject.Find("PLAYER");
			}
			if (cachedPlayer != null)
			{
				if (cachedPlayerCC == null)
				{
					cachedPlayerCC = cachedPlayer.GetComponent<CharacterController>();
				}
				Vector3 eulerAngles = cachedPlayer.transform.rotation.eulerAngles;
				cachedPlayer.transform.rotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
				Transform transform = passengerPivot;
				if (transform != null)
				{
					cachedPlayer.transform.position = transform.position + transform.right * 0.8f + Vector3.up * 0.2f;
				}
				else
				{
					cachedPlayer.transform.position += Vector3.up * 0.2f;
				}
			}
			if (cachedPlayerCC != null)
			{
				cachedPlayerCC.enabled = true;
			}
			if (Camera.main != null)
			{
				Camera.main.nearClipPlane = ((originalNearClip > 0.05f) ? originalNearClip : 0.3f);
			}
			BroadcastPassengerState(seated: false);
			ExtendedSyncDebugHUD.Log("<color=#ffdd00>[JONNEZ]</color> Вы слезли с пассажирского места Jonnez.");
		}

		private void BroadcastPassengerState(bool seated)
		{
			using GameEventWriter gameEventWriter = jonnezPassengerEvent.Writer();
			gameEventWriter.Write(seated);
			jonnezPassengerEvent.Send(gameEventWriter, 0uL, safe: true);
		}

		private void OnReceivePassengerEvent(GameEventReader reader)
		{
			bool flag = reader.ReadBoolean();
			ulong sender = reader.sender;
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>[JONNEZ RX]</color> Сетевой игрок " + sender + " -> " + (flag ? "СЕЛ НА МОПЕД" : "СЛЕЗ С МОПЕДА"));
			if (flag)
			{
				currentRemotePassengerSteamId = sender;
				nextPlayerLookupTime = 0f;
				cachedRemotePlayer = GetRemotePlayer(sender);
				Transform transform = EnsurePassengerPivot();
				if (!(cachedRemotePlayer != null) || !(transform != null))
				{
					return;
				}
				cachedRemotePlayer.transform.position = transform.position;
				cachedRemotePlayer.transform.rotation = transform.rotation;
				if (cachedRemotePlayer.player != null)
				{
					cachedRemotePlayer.player.transform.position = transform.position;
					cachedRemotePlayer.player.transform.rotation = transform.rotation;
				}
				try
				{
					if (PlayerPosField != null)
					{
						PlayerPosField.SetValue(cachedRemotePlayer, transform.position);
					}
					if (PlayerRotField != null)
					{
						PlayerRotField.SetValue(cachedRemotePlayer, transform.rotation.eulerAngles);
					}
					return;
				}
				catch
				{
					return;
				}
			}
			if (cachedRemotePlayer != null)
			{
				Vector3 eulerAngles = cachedRemotePlayer.transform.rotation.eulerAngles;
				cachedRemotePlayer.transform.rotation = Quaternion.Euler(0f, eulerAngles.y, 0f);
				Transform transform2 = passengerPivot;
				if (transform2 != null)
				{
					cachedRemotePlayer.transform.position = transform2.position + transform2.right * 0.8f + Vector3.up * 0.2f;
				}
				else
				{
					cachedRemotePlayer.transform.position += Vector3.up * 0.2f;
				}
				if (cachedRemotePlayer.player != null)
				{
					cachedRemotePlayer.player.transform.rotation = cachedRemotePlayer.transform.rotation;
					cachedRemotePlayer.player.transform.position = cachedRemotePlayer.transform.position;
				}
				try
				{
					if (PlayerPosField != null)
					{
						PlayerPosField.SetValue(cachedRemotePlayer, cachedRemotePlayer.transform.position);
					}
					if (PlayerRotField != null)
					{
						PlayerRotField.SetValue(cachedRemotePlayer, cachedRemotePlayer.transform.rotation.eulerAngles);
					}
				}
				catch
				{
				}
			}
			cachedRemotePlayer = null;
			currentRemotePassengerSteamId = 0uL;
		}

		private Player GetRemotePlayer(ulong steamId)
		{
			if (steamId == 0L)
			{
				return null;
			}
			try
			{
				if (WreckMPGlobals.Players != null && WreckMPGlobals.Players.ContainsKey(steamId))
				{
					return WreckMPGlobals.Players[steamId];
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			try
			{
				Type type = typeof(GameScene).Assembly.GetType("WreckMP.CoreManager");
				if (type != null)
				{
					PropertyInfo property = type.GetProperty("Players", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null && property.GetValue(null, null) is IDictionary dictionary && dictionary.Contains(steamId))
					{
						return dictionary[steamId] as Player;
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			if (Time.time >= nextPlayerLookupTime)
			{
				nextPlayerLookupTime = Time.time + 1f;
				Player[] array = UnityEngine.Object.FindObjectsOfType<Player>();
				if (array != null)
				{
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i] != null && array[i].SteamID == steamId)
						{
							return array[i];
						}
					}
				}
			}
			return null;
		}

		private void OnGUI()
		{
			if (Application.loadedLevelName != "GAME")
			{
				return;
			}
			Transform transform = passengerPivot;
			if (transform == null || cachedPlayer == null)
			{
				return;
			}
			if (!isLocalPassenger && currentRemotePassengerSteamId == 0L)
			{
				if (Vector3.Distance(cachedPlayer.transform.position, transform.position) < 1.8f)
				{
					GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
					GUI.Box(new Rect(Screen.width / 2 - 160, Screen.height / 2 + 60, 320f, 35f), "");
					GUI.Label(new Rect(Screen.width / 2 - 150, Screen.height / 2 + 68, 300f, 25f), "<color=#00ffcc><b>[U] Сесть на пассажирское место Jonnez ES</b></color>");
				}
			}
			else if (isLocalPassenger)
			{
				GUI.backgroundColor = new Color(0f, 0f, 0f, 0.85f);
				GUI.Box(new Rect(Screen.width / 2 - 150, Screen.height - 75, 300f, 32f), "");
				GUI.Label(new Rect(Screen.width / 2 - 140, Screen.height - 69, 280f, 25f), "<color=#ffdd00><b>Пассажир Jonnez ES [Нажмите F для выхода]</b></color>");
			}
		}
	}
	public class ExtendedVehiclesSync : MonoBehaviour
	{
		public static ExtendedVehiclesSync Instance;

		private GameEvent hoodSyncEvent;

		private GameEvent refuelingSyncEvent;

		private GameEvent flatbedHydraulicsEvent;

		private GameEvent gifuHoseEvent;

		private GameEvent vehicleToggleEvent;

		private bool isNetworkApplying;

		public bool IsNetworkApplying => isNetworkApplying;

		private bool isHoodHooked;

		private bool lastHoodOpen;

		private PlayMakerFSM cachedHoodFsm;

		private PlayMakerFSM cachedFuelTankFsm;

		private float nextInteriorCheckTime;

		private class VehicleCabinTracker
		{
			public string VehicleName;
			public GameObject VehicleObject;
			public Light[] CabinLights;
			public PlayMakerFSM CabinLightFsm;
			public PlayMakerFSM HazardsFsm;
			public PlayMakerFSM WipersFsm;
			public PlayMakerFSM GloveboxFsm;
			public PlayMakerFSM ChokeFsm;

			public bool LastCabinLight;
			public bool LastHazards;
			public bool LastWipers;
			public bool LastGlovebox;
			public bool LastChoke;

			public bool IsInitialized;
			public float NextScanTime;
		}

		private static readonly Dictionary<string, VehicleCabinTracker> ActiveTrackers = new Dictionary<string, VehicleCabinTracker>(StringComparer.OrdinalIgnoreCase);

		private static readonly Dictionary<string, GameObject> CachedVehicles = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

		public static readonly string[] MonitoredVehicleNames = new string[]
		{
			"HAYOSIKO(1500kg, 250)",
			"SATSUMA(504kg, 330)",
			"RCO_RUSCKO12(270)",
			"GIFU(750/450psi)",
			"FERNDALE(1630kg)",
			"KEKMET(350-400psi)"
		};

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			hoodSyncEvent = new GameEvent("SyncSatsumaHood", OnReceiveHoodState);
			refuelingSyncEvent = new GameEvent("SyncRefueling", OnReceiveRefueling);
			flatbedHydraulicsEvent = new GameEvent("SyncFlatbedHydraulics", OnReceiveHydraulics);
			gifuHoseEvent = new GameEvent("SyncGifuHose", OnReceiveHose);
			vehicleToggleEvent = new GameEvent("SyncVehicleToggle", OnReceiveVehicleToggle);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			isHoodHooked = false;
			lastHoodOpen = false;
			cachedHoodFsm = null;
			cachedFuelTankFsm = null;
			ActiveTrackers.Clear();
			CachedVehicles.Clear();
			nextInteriorCheckTime = 0f;
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyFindVehiclesAndHood());
			}
		}

		private IEnumerator LazyFindVehiclesAndHood()
		{
			while (!isHoodHooked)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				GameObject gameObject = GameObject.Find("SATSUMA(580kg, 240hp)") ?? GameObject.Find("SATSUMA(504kg, 330)");
				if (gameObject != null)
				{
					PlayMakerFSM playMakerFSM = null;
					PlayMakerFSM[] componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
					foreach (PlayMakerFSM playMakerFSM2 in componentsInChildren)
					{
						if (playMakerFSM2.gameObject.name.IndexOf("hood", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							playMakerFSM = playMakerFSM2;
							break;
						}
					}
					if (playMakerFSM == null)
					{
						GameObject gameObject2 = GameObject.Find("hood(Clone)") ?? GameObject.Find("hood");
						if (gameObject2 != null)
						{
							playMakerFSM = gameObject2.GetComponent<PlayMakerFSM>();
						}
					}
					if (playMakerFSM != null)
					{
						cachedHoodFsm = playMakerFSM;
						FsmBool fsmBool = cachedHoodFsm.FsmVariables.FindFsmBool("Open");
						if (fsmBool != null)
						{
							lastHoodOpen = fsmBool.Value;
						}
						SafeFsmWatcher.Attach(cachedHoodFsm, new string[4] { "Open", "Open hood", "openhood", "State 1" }, delegate
						{
							if (!isNetworkApplying)
							{
								BroadcastHoodState(isOpen: true);
							}
						});
						SafeFsmWatcher.Attach(cachedHoodFsm, new string[4] { "Close", "Close hood", "closehood", "State 2" }, delegate
						{
							if (!isNetworkApplying)
							{
								BroadcastHoodState(isOpen: false);
							}
						});
						isHoodHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#00ffcc>[VEHICLES]</color> Капот Сацумы успешно найден и подключен к сети (Авто-синхронизация)!");
					}
					componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
					foreach (PlayMakerFSM playMakerFSM3 in componentsInChildren)
					{
						if (playMakerFSM3.FsmVariables.FindFsmFloat("FuelLevel") != null || playMakerFSM3.gameObject.name.IndexOf("fuel", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							cachedFuelTankFsm = playMakerFSM3;
							break;
						}
					}
				}
				yield return new WaitForSeconds(2f);
			}
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME") return;

			if (cachedHoodFsm != null && !isNetworkApplying)
			{
				FsmBool fsmBool = cachedHoodFsm.FsmVariables.FindFsmBool("Open");
				if (fsmBool != null && fsmBool.Value != lastHoodOpen)
				{
					BroadcastHoodState(fsmBool.Value);
				}
			}

			UpdateVehicleInteriorTracking();
		}

		public void BroadcastHoodState(bool isOpen)
		{
			if (isNetworkApplying || isOpen == lastHoodOpen)
			{
				return;
			}
			lastHoodOpen = isOpen;
			using GameEventWriter gameEventWriter = hoodSyncEvent.Writer();
			gameEventWriter.Write(isOpen);
			hoodSyncEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT: Капот Сацумы -> </color>" + (isOpen ? "ОТКРЫТ" : "ЗАКРЫТ"));
		}

		private void OnReceiveHoodState(GameEventReader reader)
		{
			bool flag = reader.ReadBoolean();
			ExtendedSyncDebugHUD.Log("<color=#aaff00>IN: Капот Сацумы -> </color>" + (flag ? "ОТКРЫТ" : "ЗАКРЫТ"));
			if (cachedHoodFsm == null)
			{
				GameObject gameObject = GameObject.Find("SATSUMA(580kg, 240hp)") ?? GameObject.Find("SATSUMA(504kg, 330)");
				if (gameObject != null)
				{
					PlayMakerFSM[] componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
					foreach (PlayMakerFSM playMakerFSM in componentsInChildren)
					{
						if (playMakerFSM.gameObject.name.IndexOf("hood", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							cachedHoodFsm = playMakerFSM;
							break;
						}
					}
				}
				if (cachedHoodFsm == null)
				{
					GameObject gameObject2 = GameObject.Find("hood(Clone)") ?? GameObject.Find("hood");
					if (gameObject2 != null)
					{
						cachedHoodFsm = gameObject2.GetComponent<PlayMakerFSM>();
					}
				}
			}
			if (!(cachedHoodFsm != null))
			{
				return;
			}
			isNetworkApplying = true;
			try
			{
				lastHoodOpen = flag;
				FsmBool fsmBool = cachedHoodFsm.FsmVariables.FindFsmBool("Open");
				if (fsmBool != null)
				{
					fsmBool.Value = flag;
				}
				cachedHoodFsm.SendEvent(flag ? "OPEN" : "CLOSE");
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastRefueling(int fuelType, float liters)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = refuelingSyncEvent.Writer();
			gameEventWriter.Write(fuelType);
			gameEventWriter.Write(liters);
			refuelingSyncEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT: Заправка Сацумы +" + liters.ToString("F1") + " л</color>");
		}

		private void OnReceiveRefueling(GameEventReader reader)
		{
			int num = reader.ReadInt32();
			float num2 = reader.ReadSingle();
			ExtendedSyncDebugHUD.Log("<color=#aaff00>IN: Заправка Сацумы +" + num2.ToString("F1") + " л (Тип: " + ((num == 0) ? "95 Бензин" : "Дизель") + ")</color>");
			if (!(cachedFuelTankFsm != null))
			{
				return;
			}
			isNetworkApplying = true;
			try
			{
				FsmFloat fsmFloat = cachedFuelTankFsm.FsmVariables.FindFsmFloat("FuelLevel");
				if (fsmFloat != null)
				{
					fsmFloat.Value = Mathf.Clamp(fsmFloat.Value + num2, 0f, 38f);
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastHydraulics(float targetPos)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = flatbedHydraulicsEvent.Writer();
			gameEventWriter.Write(targetPos);
			flatbedHydraulicsEvent.Send(gameEventWriter, 0uL, safe: true);
		}

		private void OnReceiveHydraulics(GameEventReader reader)
		{
			float value = reader.ReadSingle();
			GameObject gameObject = GameObject.Find("FLATBED");
			if (!(gameObject != null))
			{
				return;
			}
			isNetworkApplying = true;
			try
			{
				PlayMakerFSM component = gameObject.GetComponent<PlayMakerFSM>();
				if (component != null)
				{
					FsmFloat fsmFloat = component.FsmVariables.FindFsmFloat("HydraulicPos");
					if (fsmFloat != null)
					{
						fsmFloat.Value = value;
					}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastHose(bool attached)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = gifuHoseEvent.Writer();
			gameEventWriter.Write(attached);
			gifuHoseEvent.Send(gameEventWriter, 0uL, safe: true);
		}

		private void OnReceiveHose(GameEventReader reader)
		{
			bool flag = reader.ReadBoolean();
			GameObject gameObject = GameObject.Find("GIFU(750/450psi)");
			if (!(gameObject != null))
			{
				return;
			}
			isNetworkApplying = true;
			try
			{
				Transform transform = gameObject.transform.Find("HoseCoupling");
				if (transform != null)
				{
					PlayMakerFSM component = transform.GetComponent<PlayMakerFSM>();
					if (component != null)
					{
						component.SendEvent(flag ? "ATTACH" : "DETACH");
					}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public static bool IsCabinLight(Light l)
		{
			if (l == null) return false;
			string lName = l.name.ToLower();
			string pName = (l.transform.parent != null) ? l.transform.parent.name.ToLower() : "";
			return lName.Contains("interior") || lName.Contains("cabin") || lName.Contains("dome") || lName.Contains("sisavalo") || lName.Contains("lightcabin") ||
			       pName.Contains("interior") || pName.Contains("cabin") || pName.Contains("dome") || pName.Contains("sisavalo") || pName.Contains("lightcabin");
		}

		public static GameObject FindInactiveVehicle(string vehicleName)
		{
			if (string.IsNullOrEmpty(vehicleName)) return null;

			if (vehicleName.IndexOf("satsuma", StringComparison.OrdinalIgnoreCase) >= 0 && BetterCheatBoxSyncManager.cachedSatsuma != null)
			{
				return BetterCheatBoxSyncManager.cachedSatsuma;
			}

			string lower = vehicleName.ToLower();
			string targetPrefix = "";
			if (lower.Contains("hayo")) targetPrefix = "HAYOSIKO";
			else if (lower.Contains("satsuma")) targetPrefix = "SATSUMA";
			else if (lower.Contains("ruscko")) targetPrefix = "RCO_RUSCKO";
			else if (lower.Contains("gifu")) targetPrefix = "GIFU";
			else if (lower.Contains("fern")) targetPrefix = "FERNDALE";
			else if (lower.Contains("kekmet")) targetPrefix = "KEKMET";
			else targetPrefix = vehicleName;

			GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] != null && (string.Equals(all[i].name, vehicleName, StringComparison.OrdinalIgnoreCase) ||
				                       all[i].name.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase)))
				{
					return all[i];
				}
			}
			return null;
		}

		public static GameObject FindVehicle(string vehicleName)
		{
			if (string.IsNullOrEmpty(vehicleName)) return null;

			if (CachedVehicles.TryGetValue(vehicleName, out GameObject cached) && cached != null)
			{
				return cached;
			}

			GameObject veh = GameObject.Find(vehicleName) ?? FindInactiveVehicle(vehicleName);
			if (veh != null)
			{
				CachedVehicles[vehicleName] = veh;
			}
			return veh;
		}

		public static void HandleVehicleToggle(string vehicleName, string toggleType, bool state)
		{
			GameObject veh = GameObject.Find(vehicleName) ?? FindInactiveVehicle(vehicleName);
			if (veh == null) return;

			if (string.Equals(toggleType, "CABIN_LIGHT", StringComparison.OrdinalIgnoreCase))
			{
				// 1. Ищем Light компоненты внутри кабины
				Light[] lights = veh.GetComponentsInChildren<Light>(true);
				for (int i = 0; i < lights.Length; i++)
				{
					if (lights[i] == null) continue;
					if (IsCabinLight(lights[i]))
					{
						lights[i].enabled = state;
					}
				}

				// 2. Отправляем события в FSM переключателя плафона, чтобы тумблер сменил положение
				PlayMakerFSM[] fsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int j = 0; j < fsms.Length; j++)
				{
					if (fsms[j] == null) continue;
					string fName = fsms[j].gameObject.name.ToLower();
					string fsmName = (fsms[j].FsmName ?? "").ToLower();
					if (fName.Contains("interior") || fName.Contains("cabin") || fName.Contains("sisavalo") || fName.Contains("lightcabin") ||
					    fsmName.Contains("interior") || fsmName.Contains("cabin") || fsmName.Contains("sisavalo") || fsmName.Contains("lightcabin"))
					{
						FsmBool b = fsms[j].FsmVariables.FindFsmBool("Light") ?? fsms[j].FsmVariables.FindFsmBool("On") ?? fsms[j].FsmVariables.FindFsmBool("Switch");
						if (b != null) b.Value = state;
						fsms[j].SendEvent(state ? "ON" : "OFF");
					}
				}
			}
			else if (string.Equals(toggleType, "HAZARDS", StringComparison.OrdinalIgnoreCase))
			{
				PlayMakerFSM[] fsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] == null) continue;
					string fName = fsms[i].gameObject.name.ToLower();
					string fsmName = (fsms[i].FsmName ?? "").ToLower();
					if (fName.Contains("hazard") || fName.Contains("blinker") || fName.Contains("hata") ||
					    fsmName.Contains("hazard") || fsmName.Contains("blinker"))
					{
						FsmBool b = fsms[i].FsmVariables.FindFsmBool("Active") ?? fsms[i].FsmVariables.FindFsmBool("On") ?? fsms[i].FsmVariables.FindFsmBool("Blinking");
						if (b != null) b.Value = state;
						fsms[i].SendEvent(state ? "ON" : "OFF");
						fsms[i].SendEvent(state ? "HAZARD" : "OFF");
					}
				}
			}
			else if (string.Equals(toggleType, "WIPERS", StringComparison.OrdinalIgnoreCase))
			{
				PlayMakerFSM[] fsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] == null) continue;
					string fName = fsms[i].gameObject.name.ToLower();
					string fsmName = (fsms[i].FsmName ?? "").ToLower();
					if (fName.Contains("wiper") || fName.Contains("pyyhkijat") || fsmName.Contains("wiper"))
					{
						FsmBool b = fsms[i].FsmVariables.FindFsmBool("Active") ?? fsms[i].FsmVariables.FindFsmBool("On");
						if (b != null) b.Value = state;
						fsms[i].SendEvent(state ? "1" : "OFF");
						fsms[i].SendEvent(state ? "ON" : "OFF");
					}
				}
			}
			else if (string.Equals(toggleType, "GLOVEBOX", StringComparison.OrdinalIgnoreCase))
			{
				PlayMakerFSM[] fsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] == null) continue;
					string fName = fsms[i].gameObject.name.ToLower();
					string fsmName = (fsms[i].FsmName ?? "").ToLower();
					if (fName.Contains("glove") || fName.Contains("hanskikas") || fsmName.Contains("glove"))
					{
						FsmBool b = fsms[i].FsmVariables.FindFsmBool("Open");
						if (b != null) b.Value = state;
						fsms[i].SendEvent(state ? "OPEN" : "CLOSE");
						fsms[i].SendEvent(state ? "Open" : "Close");
					}
				}
			}
			else if (string.Equals(toggleType, "CHOKE", StringComparison.OrdinalIgnoreCase))
			{
				PlayMakerFSM[] fsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] == null) continue;
					string fName = fsms[i].gameObject.name.ToLower();
					string fsmName = (fsms[i].FsmName ?? "").ToLower();
					if (fName.Contains("choke") || fName.Contains("ryyppy") || fsmName.Contains("choke"))
					{
						FsmFloat pos = fsms[i].FsmVariables.FindFsmFloat("Position") ?? fsms[i].FsmVariables.FindFsmFloat("Stage") ?? fsms[i].FsmVariables.FindFsmFloat("Value");
						if (pos != null) pos.Value = state ? 1f : 0f;
						fsms[i].SendEvent(state ? "PULL" : "PUSH");
						fsms[i].SendEvent(state ? "ON" : "OFF");
					}
				}
			}
		}

		public void BroadcastVehicleToggle(string vehicleName, string toggleType, bool state)
		{
			if (isNetworkApplying) return;

			if (ActiveTrackers.TryGetValue(vehicleName, out VehicleCabinTracker tr))
			{
				if (string.Equals(toggleType, "CABIN_LIGHT", StringComparison.OrdinalIgnoreCase)) tr.LastCabinLight = state;
				else if (string.Equals(toggleType, "HAZARDS", StringComparison.OrdinalIgnoreCase)) tr.LastHazards = state;
				else if (string.Equals(toggleType, "WIPERS", StringComparison.OrdinalIgnoreCase)) tr.LastWipers = state;
				else if (string.Equals(toggleType, "GLOVEBOX", StringComparison.OrdinalIgnoreCase)) tr.LastGlovebox = state;
				else if (string.Equals(toggleType, "CHOKE", StringComparison.OrdinalIgnoreCase)) tr.LastChoke = state;
			}

			using GameEventWriter writer = vehicleToggleEvent.Writer();
			writer.Write(vehicleName);
			writer.Write(toggleType);
			writer.Write(state);
			vehicleToggleEvent.Send(writer, 0uL, safe: true);

			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [VEHICLE]: " + vehicleName + " -> " + toggleType + " = " + (state ? "ВКЛ" : "ВЫКЛ") + "</color>");
		}

		private void OnReceiveVehicleToggle(GameEventReader reader)
		{
			string vehicleName = reader.ReadString();
			string toggleType = reader.ReadString();
			bool state = reader.ReadBoolean();

			ExtendedSyncDebugHUD.Log("<color=#aaff00>IN [VEHICLE]: " + vehicleName + " -> " + toggleType + " = " + (state ? "ВКЛ" : "ВЫКЛ") + "</color>");

			isNetworkApplying = true;
			try
			{
				HandleVehicleToggle(vehicleName, toggleType, state);
				if (ActiveTrackers.TryGetValue(vehicleName, out VehicleCabinTracker tr))
				{
					if (string.Equals(toggleType, "CABIN_LIGHT", StringComparison.OrdinalIgnoreCase)) tr.LastCabinLight = state;
					else if (string.Equals(toggleType, "HAZARDS", StringComparison.OrdinalIgnoreCase)) tr.LastHazards = state;
					else if (string.Equals(toggleType, "WIPERS", StringComparison.OrdinalIgnoreCase)) tr.LastWipers = state;
					else if (string.Equals(toggleType, "GLOVEBOX", StringComparison.OrdinalIgnoreCase)) tr.LastGlovebox = state;
					else if (string.Equals(toggleType, "CHOKE", StringComparison.OrdinalIgnoreCase)) tr.LastChoke = state;
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[VehicleToggle Error] " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void CheckFsmEventForVehicleToggle(PlayMakerFSM fsm, string eventName)
		{
			if (fsm == null || string.IsNullOrEmpty(eventName) || isNetworkApplying) return;

			Transform root = fsm.transform.root;
			if (root == null) return;
			string rootName = root.name;

			string matchedVeh = null;
			for (int i = 0; i < MonitoredVehicleNames.Length; i++)
			{
				if (rootName.StartsWith(MonitoredVehicleNames[i].Substring(0, Math.Min(6, MonitoredVehicleNames[i].Length)), StringComparison.OrdinalIgnoreCase))
				{
					matchedVeh = MonitoredVehicleNames[i];
					break;
				}
			}
			if (matchedVeh == null) return;

			string fName = fsm.gameObject.name;
			string fsmName = fsm.FsmName ?? "";

			if (fName.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fName.IndexOf("cabin", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fName.IndexOf("sisavalo", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fName.IndexOf("lightcabin", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fsmName.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fsmName.IndexOf("cabin", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    fsmName.IndexOf("sisavalo", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (eventName.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("switch", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("toggle", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("state 1", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					Light l = fsm.GetComponentInChildren<Light>() ?? fsm.transform.parent?.GetComponentInChildren<Light>();
					bool newState = (l != null) ? !l.enabled : true;
					BroadcastVehicleToggle(matchedVeh, "CABIN_LIGHT", newState);
				}
				else if (eventName.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0 ||
				         eventName.IndexOf("state 2", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					BroadcastVehicleToggle(matchedVeh, "CABIN_LIGHT", false);
				}
			}
			else if (fName.IndexOf("hazard", StringComparison.OrdinalIgnoreCase) >= 0 ||
			         fName.IndexOf("blinker", StringComparison.OrdinalIgnoreCase) >= 0 ||
			         fName.IndexOf("hata", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (eventName.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("hazard", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("blink", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "HAZARDS", true);
				else if (eventName.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "HAZARDS", false);
			}
			else if (fName.IndexOf("wiper", StringComparison.OrdinalIgnoreCase) >= 0 ||
			         fName.IndexOf("pyyhkijat", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (string.Equals(eventName, "1", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(eventName, "2", StringComparison.OrdinalIgnoreCase) ||
				    eventName.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "WIPERS", true);
				else if (string.Equals(eventName, "0", StringComparison.OrdinalIgnoreCase) ||
				         eventName.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "WIPERS", false);
			}
			else if (fName.IndexOf("glove", StringComparison.OrdinalIgnoreCase) >= 0 ||
			         fName.IndexOf("hanskikas", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (eventName.IndexOf("open", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "GLOVEBOX", true);
				else if (eventName.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "GLOVEBOX", false);
			}
			else if (fName.IndexOf("choke", StringComparison.OrdinalIgnoreCase) >= 0 ||
			         fName.IndexOf("ryyppy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				if (eventName.IndexOf("pull", StringComparison.OrdinalIgnoreCase) >= 0 ||
				    eventName.IndexOf("on", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "CHOKE", true);
				else if (eventName.IndexOf("push", StringComparison.OrdinalIgnoreCase) >= 0 ||
				         eventName.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0)
					BroadcastVehicleToggle(matchedVeh, "CHOKE", false);
			}
		}

		private void UpdateVehicleInteriorTracking()
		{
			if (isNetworkApplying) return;
			if (Time.time < nextInteriorCheckTime) return;
			nextInteriorCheckTime = Time.time + 0.12f;

			for (int v = 0; v < MonitoredVehicleNames.Length; v++)
			{
				string vehName = MonitoredVehicleNames[v];
				if (!ActiveTrackers.TryGetValue(vehName, out VehicleCabinTracker tracker))
				{
					tracker = new VehicleCabinTracker { VehicleName = vehName };
					ActiveTrackers[vehName] = tracker;
				}

				if (tracker.VehicleObject == null || Time.time > tracker.NextScanTime)
				{
					tracker.NextScanTime = Time.time + 3.0f;
					tracker.VehicleObject = FindVehicle(vehName);
					if (tracker.VehicleObject != null)
					{
						List<Light> list = new List<Light>();
						Light[] allL = tracker.VehicleObject.GetComponentsInChildren<Light>(true);
						for (int i = 0; i < allL.Length; i++)
						{
							if (IsCabinLight(allL[i])) list.Add(allL[i]);
						}
						tracker.CabinLights = list.ToArray();

						PlayMakerFSM[] allFsms = tracker.VehicleObject.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int j = 0; j < allFsms.Length; j++)
						{
							string fName = allFsms[j].gameObject.name.ToLower();
							string fsmName = (allFsms[j].FsmName ?? "").ToLower();
							if (fName.Contains("hazard") || fName.Contains("blinker") || fName.Contains("hata"))
								tracker.HazardsFsm = allFsms[j];
							else if (fName.Contains("wiper") || fName.Contains("pyyhkijat"))
								tracker.WipersFsm = allFsms[j];
							else if (fName.Contains("glove") || fName.Contains("hanskikas"))
								tracker.GloveboxFsm = allFsms[j];
							else if (fName.Contains("choke") || fName.Contains("ryyppy"))
								tracker.ChokeFsm = allFsms[j];
							else if (fName.Contains("interior") || fName.Contains("cabin") || fName.Contains("sisavalo") || fName.Contains("lightcabin"))
								tracker.CabinLightFsm = allFsms[j];
						}
					}
				}

				if (tracker.VehicleObject == null) continue;

				// 1. Cabin Light Check
				if (tracker.CabinLights != null && tracker.CabinLights.Length > 0)
				{
					bool anyOn = false;
					for (int l = 0; l < tracker.CabinLights.Length; l++)
					{
						if (tracker.CabinLights[l] != null && tracker.CabinLights[l].enabled)
						{
							anyOn = true;
							break;
						}
					}

					if (!tracker.IsInitialized)
					{
						tracker.LastCabinLight = anyOn;
					}
					else if (anyOn != tracker.LastCabinLight)
					{
						tracker.LastCabinLight = anyOn;
						BroadcastVehicleToggle(vehName, "CABIN_LIGHT", anyOn);
					}
				}

				// 2. Hazards Check
				if (tracker.HazardsFsm != null)
				{
					FsmBool b = tracker.HazardsFsm.FsmVariables.FindFsmBool("Active") ?? tracker.HazardsFsm.FsmVariables.FindFsmBool("On") ?? tracker.HazardsFsm.FsmVariables.FindFsmBool("Blinking");
					bool isHazardsOn = false;
					if (b != null) isHazardsOn = b.Value;
					else if (!string.IsNullOrEmpty(tracker.HazardsFsm.ActiveStateName))
					{
						string st = tracker.HazardsFsm.ActiveStateName.ToLower();
						isHazardsOn = st.Contains("blink") || st.Contains("on") || st.Contains("hazard");
					}

					if (!tracker.IsInitialized)
					{
						tracker.LastHazards = isHazardsOn;
					}
					else if (isHazardsOn != tracker.LastHazards)
					{
						tracker.LastHazards = isHazardsOn;
						BroadcastVehicleToggle(vehName, "HAZARDS", isHazardsOn);
					}
				}

				// 3. Wipers Check
				if (tracker.WipersFsm != null)
				{
					FsmBool b = tracker.WipersFsm.FsmVariables.FindFsmBool("Active") ?? tracker.WipersFsm.FsmVariables.FindFsmBool("On");
					bool isWipersOn = false;
					if (b != null) isWipersOn = b.Value;
					else if (!string.IsNullOrEmpty(tracker.WipersFsm.ActiveStateName))
					{
						string st = tracker.WipersFsm.ActiveStateName.ToLower();
						isWipersOn = !st.Contains("off") && !st.Equals("state 1");
					}

					if (!tracker.IsInitialized)
					{
						tracker.LastWipers = isWipersOn;
					}
					else if (isWipersOn != tracker.LastWipers)
					{
						tracker.LastWipers = isWipersOn;
						BroadcastVehicleToggle(vehName, "WIPERS", isWipersOn);
					}
				}

				// 4. Glovebox Check
				if (tracker.GloveboxFsm != null)
				{
					FsmBool b = tracker.GloveboxFsm.FsmVariables.FindFsmBool("Open");
					bool isGloveboxOpen = false;
					if (b != null) isGloveboxOpen = b.Value;
					else if (!string.IsNullOrEmpty(tracker.GloveboxFsm.ActiveStateName))
					{
						isGloveboxOpen = tracker.GloveboxFsm.ActiveStateName.ToLower().Contains("open");
					}

					if (!tracker.IsInitialized)
					{
						tracker.LastGlovebox = isGloveboxOpen;
					}
					else if (isGloveboxOpen != tracker.LastGlovebox)
					{
						tracker.LastGlovebox = isGloveboxOpen;
						BroadcastVehicleToggle(vehName, "GLOVEBOX", isGloveboxOpen);
					}
				}

				// 5. Choke Check
				if (tracker.ChokeFsm != null)
				{
					FsmFloat pos = tracker.ChokeFsm.FsmVariables.FindFsmFloat("Position") ?? tracker.ChokeFsm.FsmVariables.FindFsmFloat("Stage") ?? tracker.ChokeFsm.FsmVariables.FindFsmFloat("Value");
					bool isChokePulled = (pos != null && pos.Value > 0.1f);

					if (!tracker.IsInitialized)
					{
						tracker.LastChoke = isChokePulled;
					}
					else if (isChokePulled != tracker.LastChoke)
					{
						tracker.LastChoke = isChokePulled;
						BroadcastVehicleToggle(vehName, "CHOKE", isChokePulled);
					}
				}

				tracker.IsInitialized = true;
			}
		}
	}
	public class NetJoukoStorylineManager : MonoBehaviour
	{
		public static NetJoukoStorylineManager Instance;

		private GameEvent joukoSuitcaseEvent;

		private bool isNetworkApplying;

		private bool isSuitcaseHooked;

		private bool hasBeenClaimed;

		private GameObject cachedSuitcase;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			joukoSuitcaseEvent = new GameEvent("SyncJoukoSuitcase", OnReceiveSuitcaseEvent);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			isSuitcaseHooked = false;
			hasBeenClaimed = false;
			cachedSuitcase = null;
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyFindSuitcase());
			}
		}

		private IEnumerator LazyFindSuitcase()
		{
			while (!isSuitcaseHooked)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				cachedSuitcase = GameObject.Find("suitcase(itemx)") ?? GameObject.Find("Suitcase");
				if (cachedSuitcase != null)
				{
					PlayMakerFSM component = cachedSuitcase.GetComponent<PlayMakerFSM>();
					if (component != null)
					{
						SafeFsmWatcher.Attach(component, new string[4] { "Take", "Open", "Pick", "State 1" }, delegate
						{
							if (!hasBeenClaimed && !isNetworkApplying)
							{
								BroadcastSuitcaseTaken();
							}
						});
						isSuitcaseHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#ffff00>[JOUKO ARC]</color> Чемодан на 2,000,000 MK синхронизирован (Авто-режим)!");
					}
				}
				yield return new WaitForSeconds(2.5f);
			}
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME" || hasBeenClaimed || isNetworkApplying)
			{
				return;
			}
			if (cachedSuitcase != null && !cachedSuitcase.activeInHierarchy)
			{
				BroadcastSuitcaseTaken();
				return;
			}
			FsmInt fsmInt = FsmVariables.GlobalVariables.FindFsmInt("JoukoStage");
			if (fsmInt != null && fsmInt.Value >= 3)
			{
				BroadcastSuitcaseTaken();
			}
		}

		public void BroadcastSuitcaseTaken()
		{
			if (hasBeenClaimed || isNetworkApplying)
			{
				return;
			}
			hasBeenClaimed = true;
			using GameEventWriter gameEventWriter = joukoSuitcaseEvent.Writer();
			gameEventWriter.Write(value: true);
			joukoSuitcaseEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#ffff00>OUT [JOUKO]:</color> Вы забрали чемодан с 2,000,000 MK!");
		}

		private void OnReceiveSuitcaseEvent(GameEventReader reader)
		{
			bool flag = reader.ReadBoolean();
			ExtendedSyncDebugHUD.Log("<color=#ffff00>IN [JOUKO]:</color> Напарник забрал чемодан! Баланс начислен, объект деактивирован.");
			isNetworkApplying = true;
			try
			{
				if (flag && !hasBeenClaimed)
				{
					hasBeenClaimed = true;
					if (cachedSuitcase == null)
					{
						cachedSuitcase = GameObject.Find("suitcase(itemx)") ?? GameObject.Find("Suitcase");
					}
					if (cachedSuitcase != null)
					{
						cachedSuitcase.SetActive(value: false);
					}
					FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
					if (fsmFloat != null)
					{
						fsmFloat.Value += 2000000f;
					}
					FsmInt fsmInt = FsmVariables.GlobalVariables.FindFsmInt("JoukoStage");
					if (fsmInt != null && fsmInt.Value < 3)
					{
						fsmInt.Value = 3;
					}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
	public class NetMinigamesSlotManager : MonoBehaviour
	{
		public static NetMinigamesSlotManager Instance;

		private GameEvent pajatsoSyncEvent;

		private bool isNetworkApplying;

		private bool isPajatsoHooked;

		private bool suppressPajatsoWatcher;

		private float lastPajatsoWinTime;

		private PlayMakerFSM pajatsoFsm;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			pajatsoSyncEvent = new GameEvent("SyncPajatsoGame", OnReceivePajatsoEvent);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			isPajatsoHooked = false;
			suppressPajatsoWatcher = false;
			lastPajatsoWinTime = 0f;
			pajatsoFsm = null;
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyFindPajatso());
			}
		}

		private IEnumerator LazyFindPajatso()
		{
			while (!isPajatsoHooked)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				GameObject gameObject = GameObject.Find("Pajatso") ?? GameObject.Find("pajatso");
				if (gameObject != null)
				{
					pajatsoFsm = gameObject.GetComponent<PlayMakerFSM>();
					if (pajatsoFsm != null)
					{
						SafeFsmWatcher.Attach(pajatsoFsm, new string[3] { "Win", "Payout", "Victory" }, delegate
						{
							if (!isNetworkApplying && !suppressPajatsoWatcher && Time.time - lastPajatsoWinTime > 2f)
							{
								lastPajatsoWinTime = Time.time;
								FsmFloat fsmFloat = pajatsoFsm.FsmVariables.FindFsmFloat("WinAmount") ?? pajatsoFsm.FsmVariables.FindFsmFloat("Win");
								float num = ((fsmFloat != null && fsmFloat.Value > 0f) ? fsmFloat.Value : 20f);
								BroadcastPajatsoWin((int)num);
							}
						});
						isPajatsoHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#33ccff>[PAJATSO]</color> Игровой автомат Теймо успешно синхронизирован!");
					}
				}
				yield return new WaitForSeconds(3f);
			}
		}

		public void BroadcastPajatsoWin(int payoutAmount)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = pajatsoSyncEvent.Writer();
			gameEventWriter.Write(payoutAmount);
			pajatsoSyncEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#33ccff>OUT [PAJATSO]:</color> Выигрыш в Паятсо: +" + payoutAmount + " MK!");
		}

		private void OnReceivePajatsoEvent(GameEventReader reader)
		{
			int num = reader.ReadInt32();
			ExtendedSyncDebugHUD.Log("<color=#33ccff>IN [PAJATSO]:</color> Напарник сорвал куш: +" + num + " MK!");
			isNetworkApplying = true;
			try
			{
				suppressPajatsoWatcher = true;
				lastPajatsoWinTime = Time.time;
				FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
				if (fsmFloat != null)
				{
					fsmFloat.Value += num;
				}
				if (pajatsoFsm != null)
				{
					pajatsoFsm.SendEvent("WIN");
					pajatsoFsm.SendEvent("PAYOUT");
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
	public class ExtendedEconomySync : MonoBehaviour
	{
		public static ExtendedEconomySync Instance;

		private GameEvent kiljuSaleEvent;

		private GameEvent teimoPurchaseEvent;

		private bool isNetworkApplying;

		private bool isKiljuHooked;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			kiljuSaleEvent = new GameEvent("SyncKiljuSale", OnReceiveKiljuSale);
			teimoPurchaseEvent = new GameEvent("SyncTeimoPurchase", OnReceiveTeimoPurchase);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			isKiljuHooked = false;
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyFindJokke());
			}
		}

		private IEnumerator LazyFindJokke()
		{
			while (!isKiljuHooked)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				GameObject gameObject = GameObject.Find("Juoppo") ?? GameObject.Find("Jokke") ?? GameObject.Find("Joppe");
				if (gameObject != null)
				{
					PlayMakerFSM fsm = gameObject.GetComponent<PlayMakerFSM>();
					if (fsm != null)
					{
						SafeFsmWatcher.Attach(fsm, new string[3] { "Pay", "Drink", "State 1" }, delegate
						{
							if (!isNetworkApplying)
							{
								float marks = (fsm.FsmVariables.FindFsmFloat("Payment") ?? fsm.FsmVariables.FindFsmFloat("Money"))?.Value ?? 170f;
								BroadcastKiljuSale(1, marks);
							}
						});
						isKiljuHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#ffaa00>[KILJU]</color> Синхронизация продажи килью Йокке подключена!");
					}
				}
				yield return new WaitForSeconds(3f);
			}
		}

		public void BroadcastKiljuSale(int bottles, float marks)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = kiljuSaleEvent.Writer();
			gameEventWriter.Write(bottles);
			gameEventWriter.Write(marks);
			kiljuSaleEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#ffaa00>OUT [KILJU]:</color> Продано килью: +" + marks + " MK!");
		}

		private void OnReceiveKiljuSale(GameEventReader reader)
		{
			int num = reader.ReadInt32();
			float num2 = reader.ReadSingle();
			ExtendedSyncDebugHUD.Log("<color=#ffaa00>IN [KILJU]:</color> Напарник продал " + num + " бут. килью: +" + num2 + " MK!");
			isNetworkApplying = true;
			try
			{
				FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
				if (fsmFloat != null)
				{
					fsmFloat.Value += num2;
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastTeimoPurchase(float sum)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using GameEventWriter gameEventWriter = teimoPurchaseEvent.Writer();
			gameEventWriter.Write(sum);
			teimoPurchaseEvent.Send(gameEventWriter, 0uL, safe: true);
		}

		private void OnReceiveTeimoPurchase(GameEventReader reader)
		{
			float num = reader.ReadSingle();
			ExtendedSyncDebugHUD.Log("<color=#ffff00>IN [SHOP]:</color> Оплата у Теймо: -" + num + " MK");
			isNetworkApplying = true;
			try
			{
				FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
				if (fsmFloat != null)
				{
					fsmFloat.Value = Mathf.Max(0f, fsmFloat.Value - num);
				}
				GameObject gameObject = GameObject.Find("STORE") ?? GameObject.Find("Store");
				if (gameObject != null)
				{
					PlayMakerFSM[] componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>();
					for (int i = 0; i < componentsInChildren.Length; i++)
					{
						componentsInChildren[i].SendEvent("PURCHASE");
					}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
	public class NetPartsDeliverySync : MonoBehaviour
	{
		public static NetPartsDeliverySync Instance;

		private GameEvent partsOrderPlacedEvent;

		private GameEvent envelopeMailedEvent;

		private GameEvent deliveryArrivedEvent;

		private GameEvent postOrderPayEvent;

		private GameEvent parcelUnboxEvent;

		private GameEvent universalPartSpawnEvent;

		private GameEvent catalogPartUnboxEvent;

		public static readonly Dictionary<string, string> AmisAutoCatalog = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			// Спойлеры и обвес
			{ "rear spoiler 1", "spoilers" },
			{ "rear spoiler 2", "spoilers" },
			{ "fiberglass hood", "package" },
			{ "fender flare 1", "package" },
			{ "fender flare 2", "package" },
			{ "skirt 1", "package" },
			{ "skirt 2", "package" },
			{ "rear window louver", "package" },
			{ "roof spoiler", "spoilers" },

			// Колёса и диски
			{ "Hayosiko", "wheels" },
			{ "Turbine", "wheels" },
			{ "Spoke", "wheels" },
			{ "Sprengel", "wheels" },
			{ "Graser", "wheels" },
			{ "Slotted", "wheels" },
			{ "Gommer Gobra", "wheels" },

			// Интерьер и рули
			{ "rally steering wheel", "package" },
			{ "racing steering wheel", "package" },
			{ "TOMMUMIKKULAIS", "package" },
			{ "leopard plush", "package" },
			{ "zebra plush", "package" },
			{ "pink plush", "package" },

			// Приборы и аудио
			{ "subwoofer panel", "subwoofer" },
			{ "subwoofer", "subwoofer" },
			{ "amplifier", "package" },
			{ "cd player", "package" },
			{ "extra gauges", "gauges" },
			{ "rpm gauge", "gauges" },
			{ "air fuel gauge", "gauges" },
			{ "oil pressure gauge", "gauges" },

			// Подвеска и тормоза
			{ "rally shock absorber", "package" },
			{ "rally shock absorber front", "package" },
			{ "rally shock absorber rear", "package" },
			{ "rally spring", "package" },
			{ "rally spring front", "package" },
			{ "rally spring rear", "package" },
			{ "disc brakes", "package" },

			// Двигатель и выхлоп
			{ "twin carburetors", "package" },
			{ "racing carburetor", "package" },
			{ "racing radiator", "package" },
			{ "racing exhaust", "package" },
			{ "exhaust pipe", "package" },
			{ "racing muffler", "package" },
			{ "nitrous bottle", "package" },
			{ "nitrous kit", "package" },

			// Безопасность и ковши
			{ "roll cage", "package" },
			{ "bucket seat", "package" },
			{ "bucket seat 1", "package" },
			{ "bucket seat 2", "package" },
			{ "harness", "package" },

			// Оптика и аксессуары
			{ "extra lights", "package" },
			{ "exhaust tip", "package" }
		};

		public static bool IsAmisCatalogPart(string rawName)
		{
			if (string.IsNullOrEmpty(rawName)) return false;
			string clean = UniversalHandItemSync.GetCleanItemName(rawName);
			if (AmisAutoCatalog.ContainsKey(clean)) return true;
			foreach (var key in AmisAutoCatalog.Keys)
			{
				if (clean.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0 || key.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}

		public static string MatchAmisCatalogPart(string rawName)
		{
			if (string.IsNullOrEmpty(rawName)) return "";
			string clean = UniversalHandItemSync.GetCleanItemName(rawName);
			if (AmisAutoCatalog.ContainsKey(clean)) return clean;
			foreach (var kvp in AmisAutoCatalog)
			{
				if (clean.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0 || kvp.Key.IndexOf(clean, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return kvp.Key;
				}
			}
			return clean;
		}

		private bool isNetworkApplying;

		public bool isSceneResetting;

		public bool IsNetworkApplying
		{
			get { return isNetworkApplying; }
		}

		public bool IsParcelSuppressed(int instanceId)
		{
			return suppressedParcels.Contains(instanceId);
		}

		public bool isCatalogHooked;

		public bool isPostOfficeHooked;

		public bool isMailboxHooked;

		public bool isTelephoneHooked;

		private bool envelopeWasActive;

		private bool envelopeMailedSent;

		private bool postOrderBuyWasActive;

		private bool deliveryArrivedSent;

		private bool postOrderPaidSent;

		private bool suppressOrderWatcher;

		private bool suppressPayWatcher;

		private float lastOrderBroadcastTime;

		private float lastEnvelopeMailedBroadcastTime;

		private float lastPostOrderPayBroadcastTime;

		private PlayMakerArrayListProxy cachedOrderList;

		private PlayMakerFSM cachedButtonOrderFsm;

		private PlayMakerFSM cachedPostOrderPayFsm;

		private GameObject cachedEnvelope;

		private HashSet<int> hookedParcels = new HashSet<int>();

		private HashSet<int> suppressedParcels = new HashSet<int>();

		private List<string> lastOrderItems = new List<string>();

		public static int unboxCounter = 0;

		private float nextEnvelopeScanTime;

		private void Awake()
		{
			Instance = this;
		}

		private float nextOrderListCheckTime;

		private void Start()
		{
			try
			{
				partsOrderPlacedEvent = new GameEvent("SyncPartsOrderPlaced", OnReceiveOrderPlaced);
				envelopeMailedEvent = new GameEvent("SyncEnvelopeMailed", OnReceiveEnvelopeMailed);
				deliveryArrivedEvent = new GameEvent("SyncPartsDeliveryArrived", OnReceiveDeliveryArrived);
				postOrderPayEvent = new GameEvent("SyncPostOrderPay", OnReceivePostOrderPay);
				parcelUnboxEvent = new GameEvent("SyncParcelUnbox", OnReceiveParcelUnbox);
				universalPartSpawnEvent = new GameEvent("SyncUniversalPartSpawn", OnReceiveUniversalPartSpawn);
				catalogPartUnboxEvent = new GameEvent("Sync_CatalogPartUnbox", OnReceiveCatalogPartUnbox);
				OnSceneReset();
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка Start: " + ex.Message);
			}
		}

		public void OnSceneReset()
		{
			isSceneResetting = true;
			try
			{
				StopAllCoroutines();
				isNetworkApplying = false;
				isCatalogHooked = false;
				isPostOfficeHooked = false;
				isMailboxHooked = false;
				isTelephoneHooked = false;
				envelopeWasActive = false;
				envelopeMailedSent = false;
				postOrderBuyWasActive = false;
				deliveryArrivedSent = false;
				postOrderPaidSent = false;
				suppressOrderWatcher = false;
				suppressPayWatcher = false;
				lastOrderBroadcastTime = 0f;
				lastEnvelopeMailedBroadcastTime = 0f;
				lastPostOrderPayBroadcastTime = 0f;
				cachedOrderList = null;
				cachedButtonOrderFsm = null;
				cachedPostOrderPayFsm = null;
				cachedEnvelope = null;
				hookedParcels.Clear();
				suppressedParcels.Clear();
				lastOrderItems.Clear();
				unboxCounter = 0;
				nextOrderListCheckTime = 0f;
				if (Application.loadedLevelName == "GAME")
				{
					StartCoroutine(LazyHookCatalogAndPostal());
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка OnSceneReset: " + ex.Message);
			}
			finally
			{
				isSceneResetting = false;
			}
		}

		private PlayMakerArrayListProxy FindOrderList()
		{
			if (cachedOrderList != null && cachedOrderList.gameObject != null)
			{
				return cachedOrderList;
			}
			try
			{
				GameObject mag = GameObject.Find("Sheets/Magazine");
				if (mag != null)
				{
					cachedOrderList = mag.GetComponentInChildren<PlayMakerArrayListProxy>();
					if (cachedOrderList != null) return cachedOrderList;
				}
				GameObject orderListObj = GameObject.Find("OrderList");
				if (orderListObj != null)
				{
					cachedOrderList = orderListObj.GetComponent<PlayMakerArrayListProxy>();
					if (cachedOrderList != null) return cachedOrderList;
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			return null;
		}

		private GameObject FindPostOrderBuy(out PlayMakerFSM payFsm)
		{
			payFsm = cachedPostOrderPayFsm;
			if (payFsm != null && payFsm.gameObject != null)
			{
				return payFsm.gameObject;
			}
			try
			{
				GameObject store = GameObject.Find("STORE");
				if (store != null)
				{
					Transform transform = store.transform.Find("LOD/ActivateStore/PostOffice/PostOrderBuy");
					if (transform == null)
					{
						PlayMakerFSM[] componentsInChildren = store.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
						for (int i = 0; i < componentsInChildren.Length; i++)
						{
							if (componentsInChildren[i] != null && componentsInChildren[i].gameObject.name == "PostOrderBuy")
							{
								transform = componentsInChildren[i].transform;
								break;
							}
						}
					}
					if (transform != null)
					{
						cachedPostOrderPayFsm = transform.GetComponent<PlayMakerFSM>();
						payFsm = cachedPostOrderPayFsm;
						return transform.gameObject;
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			payFsm = null;
			return null;
		}

		private IEnumerator LazyHookCatalogAndPostal()
		{
			while (!isCatalogHooked || !isPostOfficeHooked)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				if (!isCatalogHooked)
				{
					PlayMakerArrayListProxy proxy = FindOrderList();
					if (proxy != null)
					{
						cachedOrderList = proxy;
					}
					if (cachedButtonOrderFsm == null)
					{
						GameObject gameObject = GameObject.Find("Sheets/Magazine/ButtonOrder");
						if (gameObject != null)
						{
							cachedButtonOrderFsm = gameObject.GetComponent<PlayMakerFSM>();
						}
					}
					if (cachedButtonOrderFsm != null)
					{
						FsmEvent fsmEvent = cachedButtonOrderFsm.Fsm.GetEvent("MP_ENVELOPE");
						if (fsmEvent == null)
						{
							fsmEvent = cachedButtonOrderFsm.AddEvent("MP_ENVELOPE");
							cachedButtonOrderFsm.AddGlobalTransition(fsmEvent, "State 3");
						}
						SafeFsmWatcher.Attach(cachedButtonOrderFsm, new string[2] { "State 3", "Order" }, delegate
						{
							if (!isNetworkApplying && !suppressOrderWatcher)
							{
								BroadcastOrderPlaced();
							}
							suppressOrderWatcher = false;
						});
						isCatalogHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS CATALOG]</color> Каталог запчастей и бланк заказа успешно синхронизированы!");
					}
				}
				if (!isPostOfficeHooked)
				{
					PlayMakerFSM payFsm;
					GameObject postOrderBuyObj = FindPostOrderBuy(out payFsm);
					if (postOrderBuyObj != null && payFsm != null)
					{
						cachedPostOrderPayFsm = payFsm;
						FsmEvent fsmEvent2 = cachedPostOrderPayFsm.Fsm.GetEvent("MP_PAY");
						if (fsmEvent2 == null)
						{
							fsmEvent2 = cachedPostOrderPayFsm.AddEvent("MP_PAY");
							cachedPostOrderPayFsm.AddGlobalTransition(fsmEvent2, "State 1");
						}
						SafeFsmWatcher.Attach(cachedPostOrderPayFsm, new string[3] { "State 1", "CreateItems", "Pay" }, delegate
						{
							PlayMakerArrayListProxy proxy2 = FindOrderList();
							if (proxy2 != null && proxy2.arrayList != null)
							{
								if (proxy2.arrayList.Count == 0 && lastOrderItems.Count > 0)
								{
									for (int k = 0; k < lastOrderItems.Count; k++)
									{
										if (!proxy2.arrayList.Contains(lastOrderItems[k]))
										{
											proxy2.arrayList.Add(lastOrderItems[k]);
										}
									}
									ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Восстановлен OrderList (" + proxy2.arrayList.Count + " дет.) перед оплатой!");
								}
								else if (proxy2.arrayList.Count > 0)
								{
									lastOrderItems.Clear();
									for (int l = 0; l < proxy2.arrayList.Count; l++)
									{
										object obj = proxy2.arrayList[l];
										if (obj != null)
										{
											lastOrderItems.Add(obj.ToString());
										}
									}
								}
							}
							if (!isNetworkApplying && !postOrderPaidSent && !suppressPayWatcher)
							{
								BroadcastPostOrderPay();
							}
							suppressPayWatcher = false;
						});
						isPostOfficeHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[POST OFFICE]</color> Касса выдачи посылок Теймо (PostOrderBuy) подключена!");
					}
				}
				yield return new WaitForSeconds(2.5f);
			}
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME")
			{
				return;
			}
			try
			{
				if (Time.time > nextOrderListCheckTime)
				{
					nextOrderListCheckTime = Time.time + 1f;
					PlayMakerArrayListProxy activeOrderList = cachedOrderList ?? FindOrderList();
					if (activeOrderList != null && activeOrderList.arrayList != null)
					{
						if (activeOrderList.arrayList.Count > 0)
						{
							lastOrderItems.Clear();
							foreach (object array in activeOrderList.arrayList)
							{
								if (array != null)
								{
									lastOrderItems.Add(array.ToString());
								}
							}
						}
						else if (lastOrderItems.Count > 0 && postOrderBuyWasActive && !postOrderPaidSent)
						{
							foreach (string item in lastOrderItems)
							{
								if (!activeOrderList.arrayList.Contains(item))
								{
									activeOrderList.arrayList.Add(item);
								}
							}
						}
					}
				}
				if (cachedEnvelope == null)
				{
					cachedEnvelope = GameObject.Find("order_envelope(itemx)") ?? GameObject.Find("order_envelope");
					if (cachedEnvelope == null && Time.time > nextEnvelopeScanTime)
					{
						nextEnvelopeScanTime = Time.time + 2f;
						GameObject gameObject = GameObject.Find("YARD");
						if (gameObject != null)
						{
							Transform[] componentsInChildren = gameObject.GetComponentsInChildren<Transform>(includeInactive: true);
							foreach (Transform transform in componentsInChildren)
							{
								if (transform != null && transform.name.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									cachedEnvelope = transform.gameObject;
									break;
								}
							}
						}
					}
					if (cachedEnvelope != null)
					{
						int partsLayer = LayerMask.NameToLayer("Parts");
						int pLayer = (partsLayer != -1) ? partsLayer : 19;
						cachedEnvelope.layer = pLayer;
						foreach (Transform child in cachedEnvelope.GetComponentsInChildren<Transform>(true))
						{
							if (child != null) child.gameObject.layer = pLayer;
						}
						CheatSpawnedItemSync.AttachToSpawned(cachedEnvelope, "msc_shared_envelope");
						PlayMakerFSM component = cachedEnvelope.GetComponent<PlayMakerFSM>();
						if (component != null)
						{
							SafeFsmWatcher.Attach(component, new string[6] { "Mail", "Send", "Sent", "Destroy", "Box", "State 1" }, delegate
							{
								if (!isNetworkApplying && !envelopeMailedSent)
								{
									BroadcastEnvelopeMailed();
								}
							});
						}
					}
				}
				if (cachedEnvelope != null)
				{
					if (cachedEnvelope.activeSelf && cachedEnvelope.activeInHierarchy)
					{
						envelopeWasActive = true;
					}
					else if (envelopeWasActive && (!cachedEnvelope.activeSelf || !cachedEnvelope.activeInHierarchy))
					{
						envelopeWasActive = false;
						if (!isNetworkApplying && !envelopeMailedSent)
						{
							BroadcastEnvelopeMailed();
						}
					}
				}
				else if (envelopeWasActive)
				{
					envelopeWasActive = false;
					if (!isNetworkApplying && !envelopeMailedSent)
					{
						BroadcastEnvelopeMailed();
					}
				}
				if (!isMailboxHooked)
				{
					GameObject gameObject2 = GameObject.Find("MailBox") ?? GameObject.Find("mailbox") ?? GameObject.Find("YellowMailbox");
					if (gameObject2 == null)
					{
						GameObject gameObject3 = GameObject.Find("STORE");
						if (gameObject3 != null)
						{
							Transform[] componentsInChildren2 = gameObject3.GetComponentsInChildren<Transform>(includeInactive: true);
							foreach (Transform transform2 in componentsInChildren2)
							{
								if (transform2 != null && transform2.name.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0)
								{
									gameObject2 = transform2.gameObject;
									break;
								}
							}
						}
					}
					if (gameObject2 != null)
					{
						PlayMakerFSM[] componentsInChildren3 = gameObject2.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
						for (int i = 0; i < componentsInChildren3.Length; i++)
						{
							if (componentsInChildren3[i] != null)
							{
								SafeFsmWatcher.Attach(componentsInChildren3[i], new string[6] { "Mail", "Send", "Envelope", "Post", "State 1", "State 2" }, delegate
								{
									if (!isNetworkApplying && !envelopeMailedSent)
									{
										BroadcastEnvelopeMailed();
									}
								});
							}
						}
						isMailboxHooked = true;
					}
				}
				if (!postOrderBuyWasActive && !postOrderPaidSent)
				{
					PlayMakerFSM payFsm;
					GameObject postOrderBuyObj = FindPostOrderBuy(out payFsm);
					if (postOrderBuyObj != null && (postOrderBuyObj.activeSelf || postOrderBuyObj.activeInHierarchy))
					{
						postOrderBuyWasActive = true;
						if (!isNetworkApplying && !deliveryArrivedSent)
						{
							BroadcastDeliveryReady();
						}
					}
				}
				if (!isTelephoneHooked)
				{
					GameObject gameObject4 = GameObject.Find("YARD");
					PlayMakerFSM phoneFsm = null;
					if (gameObject4 != null)
					{
						Transform transform3 = gameObject4.transform.Find("Building/LIVINGROOM/Telephone/Logic/Ring");
						if (transform3 != null)
						{
							phoneFsm = transform3.GetComponent<PlayMakerFSM>();
						}
					}
					if (phoneFsm == null)
					{
						GameObject gameObject5 = GameObject.Find("TELEPHONE") ?? GameObject.Find("Telephone");
						if (gameObject5 != null)
						{
							phoneFsm = gameObject5.GetComponent<PlayMakerFSM>();
						}
					}
					if (phoneFsm != null)
					{
						SafeFsmWatcher.Attach(phoneFsm, new string[3] { "Ring", "Ringing", "State 1" }, delegate
						{
							FsmString fsmString = phoneFsm.FsmVariables.FindFsmString("Topic");
							string text = ((fsmString != null) ? fsmString.Value : "");
							if ((text.IndexOf("PART", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("TEIMO", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("ORDER", StringComparison.OrdinalIgnoreCase) >= 0) && !isNetworkApplying && !deliveryArrivedSent)
							{
								BroadcastDeliveryReady();
							}
						});
						isTelephoneHooked = true;
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Домашний телефон успешно подключен к синхронизации звонков Теймо!");
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Update error: " + ex.Message);
			}
		}

		public void BroadcastOrderPlaced()
		{
			if (isNetworkApplying || suppressOrderWatcher || Time.time - lastOrderBroadcastTime < 1.5f)
			{
				return;
			}
			lastOrderBroadcastTime = Time.time;
			suppressOrderWatcher = true;

			List<string> list = new List<string>();
			PlayMakerArrayListProxy proxy = cachedOrderList ?? FindOrderList();
			if (proxy != null && proxy.arrayList != null && proxy.arrayList.Count > 0)
			{
				foreach (object array in proxy.arrayList)
				{
					if (array != null)
					{
						list.Add(array.ToString());
					}
				}
			}
			else if (lastOrderItems.Count > 0)
			{
				list.AddRange(lastOrderItems);
			}
			if (list.Count > 0)
			{
				lastOrderItems.Clear();
				lastOrderItems.AddRange(list);
			}

			GameObject localEnv = GameObject.Find("order_envelope(itemx)") ?? GameObject.Find("order_envelope");
			Vector3 pos = (localEnv != null) ? localEnv.transform.position : new Vector3(-14.3f, 4.2f, 12.8f);
			Quaternion rot = (localEnv != null) ? localEnv.transform.rotation : Quaternion.identity;

			using (GameEventWriter gameEventWriter = partsOrderPlacedEvent.Writer())
			{
				gameEventWriter.Write(pos.x);
				gameEventWriter.Write(pos.y);
				gameEventWriter.Write(pos.z);
				gameEventWriter.Write(rot.x);
				gameEventWriter.Write(rot.y);
				gameEventWriter.Write(rot.z);
				gameEventWriter.Write(rot.w);
				gameEventWriter.Write(list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					gameEventWriter.Write(list[i]);
				}
				partsOrderPlacedEvent.Send(gameEventWriter, 0uL, safe: true);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>OUT [PARTS]: Заказ сформирован (" + list.Count + " дет.)</color>");
			}

			if (localEnv != null)
			{
				cachedEnvelope = localEnv;
				int partsLayer = LayerMask.NameToLayer("Parts");
				int pLayer = (partsLayer != -1) ? partsLayer : 19;
				localEnv.layer = pLayer;
				foreach (Transform child in localEnv.GetComponentsInChildren<Transform>(true))
				{
					if (child != null) child.gameObject.layer = pLayer;
				}
				BoxCollider boxCol = localEnv.GetComponent<BoxCollider>() ?? localEnv.GetComponentInChildren<BoxCollider>();
				if (boxCol == null)
				{
					boxCol = localEnv.AddComponent<BoxCollider>();
				}
				boxCol.size = new Vector3(0.25f, 0.02f, 0.15f);
				boxCol.isTrigger = false;
				boxCol.enabled = true;
				foreach (Collider c in localEnv.GetComponentsInChildren<Collider>(true))
				{
					if (c != null)
					{
						c.isTrigger = false;
						c.enabled = true;
					}
				}
				Rigidbody rb = localEnv.GetComponent<Rigidbody>() ?? localEnv.GetComponentInChildren<Rigidbody>();
				if (rb == null)
				{
					rb = localEnv.AddComponent<Rigidbody>();
				}
				rb.isKinematic = false;
				rb.mass = 0.2f;
				rb.useGravity = true;

				CheatSpawnedItemSync.AttachToSpawned(localEnv, "msc_shared_envelope");
				BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(localEnv);
			}

			envelopeMailedSent = false;
			envelopeWasActive = false;
			deliveryArrivedSent = false;
			postOrderPaidSent = false;
		}

		private void OnReceiveOrderPlaced(GameEventReader reader)
		{
			Vector3 pos = new Vector3(-14.3f, 4.2f, 12.8f);
			Quaternion rot = Quaternion.identity;
			int count = 0;
			List<string> list = new List<string>();

			try
			{
				long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
				if (remaining >= 32)
				{
					pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					count = reader.ReadInt32();
					for (int i = 0; i < count; i++)
					{
						list.Add(reader.ReadString());
					}
				}
				else if (remaining >= 4)
				{
					count = reader.ReadInt32();
					for (int j = 0; j < count; j++)
					{
						list.Add(reader.ReadString());
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}

			ExtendedSyncDebugHUD.Log("<color=#33ff33>IN [PARTS]: Напарник заказал " + (list.Count > 0 ? list.Count : count) + " деталей по каталогу!</color>");
			isNetworkApplying = true;
			try
			{
				suppressOrderWatcher = true;
				lastOrderItems.Clear();
				lastOrderItems.AddRange(list);

				PlayMakerArrayListProxy proxy = FindOrderList();
				if (proxy != null && proxy.arrayList != null)
				{
					for (int k = 0; k < list.Count; k++)
					{
						if (!proxy.arrayList.Contains(list[k]))
						{
							proxy.arrayList.Add(list[k]);
						}
					}
				}

				GameObject env = null;
				GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
				GameObject template = null;
				if (allGameObjects != null)
				{
					for (int m = 0; m < allGameObjects.Length; m++)
					{
						GameObject g = allGameObjects[m];
						if (g != null && g.name.StartsWith("order_envelope", StringComparison.OrdinalIgnoreCase))
						{
							if (g.transform.root != null && g.transform.root.name != "FPSPlayer" && (g.activeInHierarchy || g.transform.position.sqrMagnitude > 1f))
							{
								env = g;
								break;
							}
							if (template == null)
							{
								template = g;
							}
						}
					}
				}

				if (env == null && template != null)
				{
					env = (GameObject)UnityEngine.Object.Instantiate(template, pos, rot);
					env.name = "order_envelope(itemx)";
				}
				else if (env != null)
				{
					env.transform.position = pos;
					env.transform.rotation = rot;
				}

				if (env != null)
				{
					int partsLayer = LayerMask.NameToLayer("Parts");
					int pLayer = (partsLayer != -1) ? partsLayer : 19;
					env.layer = pLayer;
					foreach (Transform child in env.GetComponentsInChildren<Transform>(true))
					{
						if (child != null) child.gameObject.layer = pLayer;
					}

					foreach (var r in env.GetComponentsInChildren<Renderer>(true))
					{
						if (r != null) r.enabled = true;
					}

					BoxCollider boxCol = env.GetComponent<BoxCollider>() ?? env.GetComponentInChildren<BoxCollider>();
					if (boxCol == null)
					{
						boxCol = env.AddComponent<BoxCollider>();
					}
					boxCol.size = new Vector3(0.25f, 0.02f, 0.15f);
					boxCol.isTrigger = false;
					boxCol.enabled = true;

					foreach (var c in env.GetComponentsInChildren<Collider>(true))
					{
						if (c != null)
						{
							c.isTrigger = false;
							c.enabled = true;
						}
					}

					Rigidbody rb = env.GetComponent<Rigidbody>() ?? env.GetComponentInChildren<Rigidbody>();
					if (rb == null)
					{
						rb = env.AddComponent<Rigidbody>();
					}
					rb.isKinematic = false;
					rb.mass = 0.2f;
					rb.useGravity = true;

					PlayMakerFSM[] envFsms = env.GetComponentsInChildren<PlayMakerFSM>(true);
					for (int f = 0; f < envFsms.Length; f++)
					{
						if (envFsms[f] != null) envFsms[f].enabled = true;
					}

					env.SetActive(true);
					cachedEnvelope = env;
					CheatSpawnedItemSync.AttachToSpawned(env, "msc_shared_envelope");

					PlayMakerFSM component = cachedEnvelope.GetComponent<PlayMakerFSM>();
					if (component != null)
					{
						SafeFsmWatcher.Attach(component, new string[6] { "Mail", "Send", "Sent", "Destroy", "Box", "State 1" }, delegate
						{
							if (!isNetworkApplying && !envelopeMailedSent)
							{
								BroadcastEnvelopeMailed();
							}
						});
					}
					ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Конверт заказа (msc_shared_envelope) материализован как сетевой физический предмет!");
				}

				envelopeMailedSent = false;
				envelopeWasActive = false;
				deliveryArrivedSent = false;
				postOrderPaidSent = false;
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastEnvelopeMailed()
		{
			if (isNetworkApplying || envelopeMailedSent || Time.time - lastEnvelopeMailedBroadcastTime < 1.5f)
			{
				return;
			}
			lastEnvelopeMailedBroadcastTime = Time.time;
			envelopeMailedSent = true;
			envelopeWasActive = false;

			List<string> list = new List<string>();
			PlayMakerArrayListProxy proxy = cachedOrderList ?? FindOrderList();
			if (proxy != null && proxy.arrayList != null && proxy.arrayList.Count > 0)
			{
				foreach (object obj in proxy.arrayList)
				{
					if (obj != null)
					{
						list.Add(obj.ToString());
					}
				}
			}
			else if (lastOrderItems.Count > 0)
			{
				list.AddRange(lastOrderItems);
			}
			if (list.Count > 0)
			{
				lastOrderItems.Clear();
				lastOrderItems.AddRange(list);
			}

			using (GameEventWriter gameEventWriter = envelopeMailedEvent.Writer())
			{
				gameEventWriter.Write(list.Count);
				for (int i = 0; i < list.Count; i++)
				{
					gameEventWriter.Write(list[i]);
				}
				envelopeMailedEvent.Send(gameEventWriter, 0uL, safe: true);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>OUT [PARTS]: Конверт опущен в почтовый ящик! (" + list.Count + " дет.)</color>");
			}

			ApplyEnvelopeMailedGlobal(list);
		}

		private void OnReceiveEnvelopeMailed(GameEventReader reader)
		{
			List<string> list = new List<string>();
			try
			{
				long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
				if (remaining >= 4)
				{
					int count = reader.ReadInt32();
					for (int i = 0; i < count; i++)
					{
						list.Add(reader.ReadString());
					}
				}
				else
				{
					reader.ReadBoolean();
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}

			ExtendedSyncDebugHUD.Log("<color=#33ff33>IN [PARTS]: Напарник отправил конверт почтой! Заказ в пути (" + (list.Count > 0 ? list.Count : lastOrderItems.Count) + " дет.).</color>");
			isNetworkApplying = true;
			try
			{
				envelopeMailedSent = true;
				envelopeWasActive = false;
				ApplyEnvelopeMailedGlobal(list);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void ApplyEnvelopeMailedGlobal(List<string> items)
		{
			try
			{
				DestroyOrHideAllEnvelopes();

				if (items != null && items.Count > 0)
				{
					lastOrderItems.Clear();
					lastOrderItems.AddRange(items);
				}

				PlayMakerArrayListProxy proxy = cachedOrderList ?? FindOrderList();
				if (proxy != null && proxy.arrayList != null)
				{
					if (items != null && items.Count > 0)
					{
						for (int i = 0; i < items.Count; i++)
						{
							if (!proxy.arrayList.Contains(items[i]))
							{
								proxy.arrayList.Add(items[i]);
							}
						}
					}
					else if (proxy.arrayList.Count == 0 && lastOrderItems.Count > 0)
					{
						for (int j = 0; j < lastOrderItems.Count; j++)
						{
							if (!proxy.arrayList.Contains(lastOrderItems[j]))
							{
								proxy.arrayList.Add(lastOrderItems[j]);
							}
						}
					}
				}

				PlayMakerFSM timerFsm = null;
				if (proxy != null)
				{
					Transform timerTr = proxy.transform.Find("Timer");
					if (timerTr != null)
					{
						timerFsm = timerTr.GetComponent<PlayMakerFSM>();
					}
					if (timerFsm == null)
					{
						timerFsm = proxy.GetComponentInChildren<PlayMakerFSM>();
					}
				}
				if (timerFsm == null)
				{
					PlayMakerFSM[] allFsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
					if (allFsms != null)
					{
						for (int k = 0; k < allFsms.Length; k++)
						{
							if (allFsms[k] != null && allFsms[k].gameObject != null && allFsms[k].gameObject.name == "Timer")
							{
								Transform p = allFsms[k].transform.parent;
								if (p != null && p.name == "OrderList")
								{
									timerFsm = allFsms[k];
									break;
								}
							}
						}
					}
				}

				if (timerFsm != null)
				{
					timerFsm.SendEvent("START");
					timerFsm.SendEvent("WAIT");
					ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Таймер доставки OrderList/Timer переведён в состояние ожидания (START/WAIT)");
				}

				TriggerMailBoxEvent();
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка ApplyEnvelopeMailedGlobal: " + ex.Message);
			}
		}

		private void TriggerMailBoxEvent()
		{
			try
			{
				GameObject mb = GameObject.Find("MailBox") ?? GameObject.Find("mailbox") ?? GameObject.Find("YellowMailbox");
				if (mb == null)
				{
					GameObject store = GameObject.Find("STORE");
					if (store != null)
					{
						Transform[] storeTr = store.GetComponentsInChildren<Transform>(true);
						for (int i = 0; i < storeTr.Length; i++)
						{
							if (storeTr[i] != null && storeTr[i].name.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								mb = storeTr[i].gameObject;
								break;
							}
						}
					}
				}
				if (mb == null)
				{
					GameObject yard = GameObject.Find("YARD");
					if (yard != null)
					{
						Transform[] yardTr = yard.GetComponentsInChildren<Transform>(true);
						for (int j = 0; j < yardTr.Length; j++)
						{
							if (yardTr[j] != null && yardTr[j].name.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								mb = yardTr[j].gameObject;
								break;
							}
						}
					}
				}

				if (mb != null)
				{
					PlayMakerFSM[] fsms = mb.GetComponentsInChildren<PlayMakerFSM>(true);
					for (int k = 0; k < fsms.Length; k++)
					{
						if (fsms[k] != null)
						{
							fsms[k].SendEvent("MAIL");
							fsms[k].SendEvent("SEND");
							fsms[k].SendEvent("Mail");
							fsms[k].SendEvent("Send");
						}
					}
					ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Почтовый ящик сработал! Таймер доставки запущен на обоих ПК.");
				}
				else
				{
					PlayMakerFSM[] allFsms = Resources.FindObjectsOfTypeAll<PlayMakerFSM>();
					if (allFsms != null)
					{
						for (int m = 0; m < allFsms.Length; m++)
						{
							if (allFsms[m] != null && allFsms[m].gameObject != null && allFsms[m].gameObject.name.IndexOf("mailbox", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								allFsms[m].SendEvent("MAIL");
								allFsms[m].SendEvent("SEND");
								allFsms[m].SendEvent("Mail");
								allFsms[m].SendEvent("Send");
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка TriggerMailBoxEvent: " + ex.Message);
			}
		}

		private void DestroyOrHideAllEnvelopes()
		{
			if (cachedEnvelope != null)
			{
				cachedEnvelope.SetActive(value: false);
			}
			try
			{
				GameObject[] array = UnityEngine.Object.FindObjectsOfType<GameObject>();
				if (array != null)
				{
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i] != null && array[i].name.StartsWith("order_envelope", StringComparison.OrdinalIgnoreCase))
						{
							array[i].SetActive(value: false);
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}

			try
			{
				CheatSpawnedItemSync envItem = CheatSpawnedItemSync.FindItem("msc_shared_envelope") ?? CheatSpawnedItemSync.FindItem("msc_order_envelope_shared");
				if (envItem != null)
				{
					envItem.isHeldByRemote = false;
					if (envItem.gameObject != null)
					{
						envItem.gameObject.SetActive(false);
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}

			try
			{
				UniversalHandItemSync.Instance?.ClearHandVisualByName("envelope");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
		}

		public void BroadcastDeliveryReady()
		{
			if (isNetworkApplying || deliveryArrivedSent)
			{
				return;
			}
			deliveryArrivedSent = true;
			using GameEventWriter gameEventWriter = deliveryArrivedEvent.Writer();
			gameEventWriter.Write(value: true);
			deliveryArrivedEvent.Send(gameEventWriter, 0uL, safe: true);
			ExtendedSyncDebugHUD.Log("<color=#33ff33>OUT [PARTS]: Посылки прибыли в магазин Теймо!</color>");
		}

		private void OnReceiveDeliveryArrived(GameEventReader reader)
		{
			reader.ReadBoolean();
			ExtendedSyncDebugHUD.Log("<color=#33ff33>IN [PARTS]: Заказ деталей доставлен! Теймо ждёт на кассе.</color>");
			isNetworkApplying = true;
			try
			{
				deliveryArrivedSent = true;
				postOrderBuyWasActive = true;
				try
				{
					typeof(GameScene).Assembly.GetType("WreckMP.NetTelephoneManager")?.GetMethod("TriggerCall", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, new object[1] { "PARTS" });
				}
				catch (Exception ex)
				{
					ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
				}
				GameObject gameObject = GameObject.Find("YARD");
				if (gameObject != null)
				{
					Transform transform = gameObject.transform.Find("Building/LIVINGROOM/Telephone/Logic/Ring");
					if (transform != null)
					{
						transform.gameObject.SetActive(value: true);
						PlayMakerFSM component = transform.GetComponent<PlayMakerFSM>();
						if (component != null)
						{
							FsmString fsmString = component.FsmVariables.FindFsmString("Topic");
							if (fsmString != null)
							{
								fsmString.Value = "PARTS";
							}
							component.SendEvent("RING");
						}
					}
				}
				PlayMakerFSM payFsm;
				GameObject postOrderBuyObj = FindPostOrderBuy(out payFsm);
				if (postOrderBuyObj != null)
				{
					postOrderBuyObj.SetActive(value: true);
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void BroadcastPostOrderPay()
		{
			if (isNetworkApplying || postOrderPaidSent || suppressPayWatcher || Time.time - lastPostOrderPayBroadcastTime < 1.5f)
			{
				return;
			}
			lastPostOrderPayBroadcastTime = Time.time;
			postOrderPaidSent = true;
			postOrderBuyWasActive = false;
			PlayMakerArrayListProxy proxy = FindOrderList();
			if (proxy != null && proxy.arrayList != null && proxy.arrayList.Count > 0)
			{
				lastOrderItems.Clear();
				for (int i = 0; i < proxy.arrayList.Count; i++)
				{
					object obj = proxy.arrayList[i];
					if (obj != null)
					{
						lastOrderItems.Add(obj.ToString());
					}
				}
			}

			float billPrice = 0f;
			if (cachedPostOrderPayFsm != null && cachedPostOrderPayFsm.FsmVariables != null)
			{
				FsmFloat fPrice = cachedPostOrderPayFsm.FsmVariables.FindFsmFloat("Price") ??
				                  cachedPostOrderPayFsm.FsmVariables.FindFsmFloat("Total") ??
				                  cachedPostOrderPayFsm.FsmVariables.FindFsmFloat("Cost");
				if (fPrice != null) billPrice = fPrice.Value;
			}

			using (GameEventWriter gameEventWriter = postOrderPayEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(billPrice);
				gameEventWriter.Write(lastOrderItems.Count);
				for (int j = 0; j < lastOrderItems.Count; j++)
				{
					gameEventWriter.Write(lastOrderItems[j]);
				}
				postOrderPayEvent.Send(gameEventWriter, 0uL, safe: true);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>OUT [PARTS]: Заказ деталей оплачен на кассе! (" + lastOrderItems.Count + " дет.)</color>");
			}
			CleanupAllPostOrderBuyObjects();
			StartCoroutine(RegisterUnpackedBoxesCoroutine(new Vector3(-15.5f, 0f, 15f), isPayer: true));
		}

		private void OnReceivePostOrderPay(GameEventReader reader)
		{
			ulong payerSteamId = 0uL;
			float billPrice = 0f;
			List<string> items = new List<string>();
			try
			{
				long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
				if (remaining >= 16)
				{
					payerSteamId = reader.ReadUInt64();
					billPrice = reader.ReadSingle();
					int count = reader.ReadInt32();
					for (int i = 0; i < count; i++)
					{
						items.Add(reader.ReadString());
					}
				}
				else if (remaining >= 12)
				{
					payerSteamId = reader.ReadUInt64();
					int count = reader.ReadInt32();
					for (int i = 0; i < count; i++)
					{
						items.Add(reader.ReadString());
					}
				}
				else
				{
					reader.ReadBoolean();
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}

			ExtendedSyncDebugHUD.Log("<color=#33ff33>IN [PARTS]: Напарник оплатил посылки (" + (items.Count > 0 ? items.Count.ToString() : lastOrderItems.Count.ToString()) + " дет.)! Выдача коробок...</color>");
			isNetworkApplying = true;
			try
			{
				postOrderPaidSent = true;
				postOrderBuyWasActive = false;
				suppressPayWatcher = true;
				if (items.Count > 0)
				{
					lastOrderItems.Clear();
					lastOrderItems.AddRange(items);
				}
				else if (lastOrderItems.Count > 0)
				{
					items.AddRange(lastOrderItems);
				}

				PlayMakerArrayListProxy proxy = FindOrderList();
				if (proxy != null && proxy.arrayList != null)
				{
					if (items.Count > 0)
					{
						for (int j = 0; j < items.Count; j++)
						{
							if (!proxy.arrayList.Contains(items[j]))
							{
								proxy.arrayList.Add(items[j]);
							}
						}
					}
					else if (proxy.arrayList.Count == 0 && lastOrderItems.Count > 0)
					{
						for (int k = 0; k < lastOrderItems.Count; k++)
						{
							if (!proxy.arrayList.Contains(lastOrderItems[k]))
							{
								proxy.arrayList.Add(lastOrderItems[k]);
							}
						}
					}
				}

				PlayMakerFSM payFsm;
				GameObject postOrderBuyObj = FindPostOrderBuy(out payFsm);
				if (payFsm != null)
				{
					payFsm.SendEvent("PAID");
					payFsm.SendEvent("Paid");
					payFsm.SendEvent("BOUGHT");
					payFsm.SendEvent("Bought");
					payFsm.SendEvent("DISABLE");
					payFsm.SendEvent("Disable");
				}

				CleanupAllPostOrderBuyObjects();
				StartCoroutine(CompletePostOrderPayReceiver(payFsm, postOrderBuyObj, items));
				StartCoroutine(RegisterUnpackedBoxesCoroutine(new Vector3(-15.5f, 0f, 15f), isPayer: false));
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public static GameObject FindParcelBoxTemplateInResources()
		{
			try
			{
				GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
				if (all != null)
				{
					for (int i = 0; i < all.Length; i++)
					{
						if (all[i] == null) continue;
						string n = all[i].name;
						if (n.StartsWith("amis auto toy package", StringComparison.OrdinalIgnoreCase) ||
							n.StartsWith("amis auto package", StringComparison.OrdinalIgnoreCase) ||
							n.StartsWith("package", StringComparison.OrdinalIgnoreCase) ||
							n.StartsWith("Post Package", StringComparison.OrdinalIgnoreCase))
						{
							return all[i];
						}
					}
					for (int j = 0; j < all.Length; j++)
					{
						if (all[j] == null) continue;
						if (IsParcelBox(all[j].name))
						{
							return all[j];
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			return null;
		}

		public static int CountParcelBoxesNear(Vector3 pos, float radius)
		{
			int count = 0;
			try
			{
				GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>();
				if (all != null)
				{
					for (int i = 0; i < all.Length; i++)
					{
						if (all[i] != null && IsParcelBox(all[i].name))
						{
							if (Vector3.Distance(all[i].transform.position, pos) <= radius)
							{
								count++;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			return count;
		}

		private IEnumerator CompletePostOrderPayReceiver(PlayMakerFSM payFsm, GameObject postOrderBuyObj, List<string> items)
		{
			yield return new WaitForSeconds(0.35f);
			CleanupAllPostOrderBuyObjects();

			Vector3 storePos = new Vector3(-1551.5f, 4.5f, 1182.8f);
			int parcelCount = CountParcelBoxesNear(storePos, 25f);
			if (parcelCount == 0 && items != null && items.Count > 0)
			{
				ExtendedSyncDebugHUD.Log("<color=#ffaa00>WARN [PARTS]: На кассе 0 коробок. Запуск гарантированного спавна " + items.Count + " посылок...</color>");
				GameObject template = FindParcelBoxTemplateInResources();
				if (template != null)
				{
					for (int i = 0; i < items.Count; i++)
					{
						Vector3 spawnPos = storePos + new Vector3(i * 0.45f, 0.1f, 0f);
						GameObject newBox = (GameObject)UnityEngine.Object.Instantiate(template, spawnPos, Quaternion.identity);
						newBox.name = "amis auto toy package(Clone)";
						int partsLayer = LayerMask.NameToLayer("Parts");
						int pLayer = (partsLayer != -1) ? partsLayer : 19;
						newBox.layer = pLayer;
						foreach (Transform child in newBox.GetComponentsInChildren<Transform>(true))
						{
							if (child != null) child.gameObject.layer = pLayer;
						}

						foreach (var rend in newBox.GetComponentsInChildren<Renderer>(true))
						{
							if (rend != null) rend.enabled = true;
						}
						foreach (var col in newBox.GetComponentsInChildren<Collider>(true))
						{
							if (col != null)
							{
								col.isTrigger = false;
								col.enabled = true;
							}
						}
						Rigidbody rb = newBox.GetComponent<Rigidbody>();
						if (rb != null)
						{
							rb.isKinematic = false;
							rb.useGravity = true;
						}

						ParcelUnboxTracker trk = newBox.GetComponent<ParcelUnboxTracker>() ?? newBox.AddComponent<ParcelUnboxTracker>();
						trk.BoxName = newBox.name;
						trk.PartName = items[i];
						trk.ItemIndex = i;

						string cleanPartName = UniversalHandItemSync.GetCleanItemName(items[i]);
						int hashBox = ("msc_parcel_" + cleanPartName + "_" + i).GetHashFNV_1a();
						if (rb != null)
						{
							try
							{
								if (NetRigidbodyManager.GetRigidbodyHash(rb) == 0)
								{
									NetRigidbodyManager.AddRigidbody(rb, hashBox);
								}
							}
							catch (Exception ex)
							{
								ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
							}
						}

						PlayMakerFSM[] boxFsms = newBox.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int f = 0; f < boxFsms.Length; f++)
						{
							if (boxFsms[f] != null)
							{
								boxFsms[f].enabled = true;
								if (boxFsms[f].FsmVariables != null)
								{
									FsmString fsmStr = boxFsms[f].FsmVariables.FindFsmString("Item") ?? 
									                   boxFsms[f].FsmVariables.FindFsmString("Part") ?? 
									                   boxFsms[f].FsmVariables.FindFsmString("Name");
									if (fsmStr != null) fsmStr.Value = items[i];

									FsmGameObject fsmGo = boxFsms[f].FsmVariables.FindFsmGameObject("Item") ?? 
									                      boxFsms[f].FsmVariables.FindFsmGameObject("Part") ?? 
									                      boxFsms[f].FsmVariables.FindFsmGameObject("Spawn");
									if (fsmGo != null)
									{
										GameObject partTmpl = FindPartTemplateInResources(UniversalHandItemSync.GetCleanItemName(items[i]));
										if (partTmpl != null) fsmGo.Value = partTmpl;
									}
								}
							}
						}

						newBox.SetActive(true);
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Спавн посылки: " + items[i]);
					}
					ScanAndHookParcels();
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [PARTS]: Шаблон коробки посылки не найден в ресурсах!</color>");
				}
			}

			yield return new WaitForSeconds(0.2f);
			CleanupAllPostOrderBuyObjects();
		}

		public static bool IsReceiptObject(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			return name.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("PostOrder", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("Bill", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public void CleanupAllPostOrderBuyObjects()
		{
			postOrderBuyWasActive = false;
			try
			{
				GameObject store = GameObject.Find("STORE");
				if (store != null)
				{
					Transform[] allTr = store.GetComponentsInChildren<Transform>(true);
					for (int k = 0; k < allTr.Length; k++)
					{
						if (allTr[k] != null && IsReceiptObject(allTr[k].name))
						{
							DisablePostOrderBuyObject(allTr[k].gameObject);
						}
					}
				}

				GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
				if (allObjects != null)
				{
					for (int j = 0; j < allObjects.Length; j++)
					{
						GameObject obj = allObjects[j];
						if (obj != null && IsReceiptObject(obj.name))
						{
							DisablePostOrderBuyObject(obj);
						}
					}
				}

				GameObject[] sceneObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
				if (sceneObjects != null)
				{
					for (int i = 0; i < sceneObjects.Length; i++)
					{
						GameObject obj = sceneObjects[i];
						if (obj != null && IsReceiptObject(obj.name))
						{
							DisablePostOrderBuyObject(obj);
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка CleanupAllPostOrderBuyObjects: " + ex.Message);
			}
		}

		private void DisablePostOrderBuyObject(GameObject obj)
		{
			if (obj == null) return;
			try
			{
				Renderer[] rends = obj.GetComponentsInChildren<Renderer>(true);
				for (int r = 0; r < rends.Length; r++)
				{
					if (rends[r] != null) rends[r].enabled = false;
				}

				Collider[] cols = obj.GetComponentsInChildren<Collider>(true);
				for (int c = 0; c < cols.Length; c++)
				{
					if (cols[c] != null) cols[c].enabled = false;
				}

				PlayMakerFSM[] fsms = obj.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] != null)
					{
						try
						{
							fsms[i].SendEvent("PAID");
							fsms[i].SendEvent("Paid");
							fsms[i].SendEvent("BOUGHT");
							fsms[i].SendEvent("Bought");
							fsms[i].SendEvent("DISABLE");
							fsms[i].SendEvent("Disable");
							fsms[i].SendEvent("FINISH");
						}
						catch (Exception ex)
						{
							ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
						}
					}
				}

				obj.SetActive(false);
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
		}

		private IEnumerator RegisterUnpackedBoxesCoroutine(Vector3 pos, bool isPayer)
		{
			yield return new WaitForSeconds(0.4f);
			ScanAndHookParcels();

			PlayMakerFSM[] array = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
			if (array != null)
			{
				for (int k = 0; k < array.Length; k++)
				{
					PlayMakerFSM playMakerFSM = array[k];
					if (playMakerFSM == null || playMakerFSM.gameObject == null)
					{
						continue;
					}
					string text = playMakerFSM.gameObject.name;
					string rootText = (playMakerFSM.transform.root != null) ? playMakerFSM.transform.root.name : "";
					if (IsParcelBox(text) || IsParcelBox(rootText))
					{
						GameObject boxObj = IsParcelBox(text) ? playMakerFSM.gameObject : playMakerFSM.transform.root.gameObject;
						Rigidbody rbBox = boxObj.GetComponent<Rigidbody>();
						if (rbBox != null)
						{
							ParcelUnboxTracker trk = boxObj.GetComponent<ParcelUnboxTracker>();
							int itemIndex = -1;
							string cleanBox = UniversalHandItemSync.GetCleanItemName(boxObj.name);
							string cleanPartName = cleanBox;
							if (trk != null)
							{
								if (trk.ItemIndex >= 0) itemIndex = trk.ItemIndex;
								if (!string.IsNullOrEmpty(trk.PartName)) cleanPartName = UniversalHandItemSync.GetCleanItemName(trk.PartName);
							}
							if (itemIndex < 0 && lastOrderItems != null && lastOrderItems.Count > 0)
							{
								for (int i = 0; i < lastOrderItems.Count; i++)
								{
									string oName = UniversalHandItemSync.GetCleanItemName(lastOrderItems[i]);
									if (string.Equals(oName, cleanBox, StringComparison.OrdinalIgnoreCase) ||
									    cleanBox.IndexOf(oName, StringComparison.OrdinalIgnoreCase) >= 0 ||
									    oName.IndexOf(cleanBox, StringComparison.OrdinalIgnoreCase) >= 0)
									{
										itemIndex = i;
										cleanPartName = oName;
										break;
									}
								}
							}
							if (itemIndex < 0) itemIndex = 0;
							if (trk != null)
							{
								trk.ItemIndex = itemIndex;
								trk.PartName = cleanPartName;
							}

							int hashBox = ("msc_parcel_" + cleanPartName + "_" + itemIndex).GetHashFNV_1a();
							try
							{
								if (NetRigidbodyManager.GetRigidbodyHash(rbBox) == 0)
								{
									NetRigidbodyManager.AddRigidbody(rbBox, hashBox);
								}
							}
							catch (Exception ex)
							{
								ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
							}
							if (isPayer)
							{
								BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(boxObj);
							}
							else
							{
								BetterCheatBoxSyncManager.ResetRigidbodyPhysicsLocal(boxObj);
							}
							BetterCheatBoxSyncManager.UpdateNetRigidbodyCache(boxObj, rbBox.position, rbBox.rotation);
						}
					}
				}
			}
		}

		public static bool IsParcelBox(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			if (name.IndexOf("PostOrderBuy", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("PostOffice", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("STORE", StringComparison.OrdinalIgnoreCase) >= 0 ||
			    name.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return false;
			}
			return name.IndexOf("package", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("parcel", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("spoilers", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("wheels", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("gauges", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("subwoofer", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("amis", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("post ", StringComparison.OrdinalIgnoreCase) >= 0 ||
			       name.IndexOf("post_", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		public static bool IsParcelBox(GameObject go)
		{
			if (go == null) return false;
			if (IsParcelBox(go.name)) return true;
			if (go.transform.root != null && IsParcelBox(go.transform.root.name)) return true;

			try
			{
				PlayMakerFSM[] fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
				if (fsms != null)
				{
					for (int i = 0; i < fsms.Length; i++)
					{
						var fsm = fsms[i];
						if (fsm == null || fsm.FsmStates == null) continue;
						for (int s = 0; s < fsm.FsmStates.Length; s++)
						{
							var state = fsm.FsmStates[s];
							if (state == null || state.Actions == null) continue;
							for (int a = 0; a < state.Actions.Length; a++)
							{
								var action = state.Actions[a];
								if (action == null) continue;
								string typeName = action.GetType().Name;
								if (typeName == "CreateObject" || typeName == "SpawnObject")
								{
									return true;
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			return false;
		}

		public static string FindSpawnedPartNameFromFsm(GameObject box)
		{
			if (box == null) return "";
			try
			{
				PlayMakerFSM[] fsms = box.GetComponentsInChildren<PlayMakerFSM>(true);
				for (int i = 0; i < fsms.Length; i++)
				{
					if (fsms[i] == null || fsms[i].Fsm == null) continue;
					if (fsms[i].FsmVariables != null)
					{
						FsmGameObject fsmGo = fsms[i].FsmVariables.FindFsmGameObject("Item") ?? 
						                      fsms[i].FsmVariables.FindFsmGameObject("Part") ?? 
						                      fsms[i].FsmVariables.FindFsmGameObject("Spawn");
						if (fsmGo != null && fsmGo.Value != null)
						{
							return fsmGo.Value.name;
						}
						FsmString fsmStr = fsms[i].FsmVariables.FindFsmString("Item") ?? 
						                   fsms[i].FsmVariables.FindFsmString("Part") ?? 
						                   fsms[i].FsmVariables.FindFsmString("Name");
						if (fsmStr != null && !string.IsNullOrEmpty(fsmStr.Value))
						{
							return fsmStr.Value;
						}
					}
					if (fsms[i].FsmStates != null)
					{
						for (int s = 0; s < fsms[i].FsmStates.Length; s++)
						{
							var state = fsms[i].FsmStates[s];
							if (state != null && state.Actions != null)
							{
								for (int a = 0; a < state.Actions.Length; a++)
								{
									var action = state.Actions[a];
									if (action != null)
									{
										var field = action.GetType().GetField("gameObject", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
										if (field != null)
										{
											var val = field.GetValue(action) as FsmGameObject;
											if (val != null && val.Value != null)
											{
												return val.Value.name;
											}
										}
									}
								}
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
			return "";
		}

		public static string ExtractPartNameFromBox(string boxName)
		{
			if (string.IsNullOrEmpty(boxName)) return "";
			string name = boxName;
			name = name.Replace("(Clone)", "").Replace("(itemx)", "").Replace("(item)", "").Trim();
			name = name.Replace("packages", "").Replace("parcels", "").Replace("package", "").Replace("parcel", "").Trim();
			if (name.StartsWith("amis ", StringComparison.OrdinalIgnoreCase))
			{
				name = name.Substring(5).Trim();
			}
			else if (name.StartsWith("Post ", StringComparison.OrdinalIgnoreCase))
			{
				name = name.Substring(5).Trim();
			}
			return name.Trim();
		}

		public GameObject FindSpawnedPartNear(Vector3 pos, string cleanPartName, float radius)
		{
			Collider[] colliders = Physics.OverlapSphere(pos, radius);
			if (colliders != null)
			{
				string cleanTarget = !string.IsNullOrEmpty(cleanPartName) ? UniversalHandItemSync.GetCleanItemName(cleanPartName) : "";
				for (int i = 0; i < colliders.Length; i++)
				{
					Collider c = colliders[i];
					if (c == null) continue;
					GameObject go = c.attachedRigidbody != null ? c.attachedRigidbody.gameObject : c.gameObject;
					string n = go.name;
					string rootN = (go.transform.root != null) ? go.transform.root.name : "";
					if (n.IndexOf("PLAYER", StringComparison.OrdinalIgnoreCase) >= 0 ||
						rootN.IndexOf("PLAYER", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("STORE", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("YARD", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
						IsParcelBox(n) || IsParcelBox(rootN))
					{
						continue;
					}

					string cleanFound = UniversalHandItemSync.GetCleanItemName(n);
					if (string.IsNullOrEmpty(cleanTarget) ||
						string.Equals(cleanFound, cleanTarget, StringComparison.OrdinalIgnoreCase) ||
						cleanFound.IndexOf(cleanTarget, StringComparison.OrdinalIgnoreCase) >= 0 ||
						cleanTarget.IndexOf(cleanFound, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return go;
					}
				}
			}
			return null;
		}

		public static GameObject FindCatalogPartTemplate(string cleanPartName)
		{
			if (string.IsNullOrEmpty(cleanPartName)) return null;

			// 1. BetterCheatBox register lookup (Instant lookup in preloaded list)
			if (BetterCheatBoxSyncManager.Instance != null)
			{
				GameObject bcbTemplate = BetterCheatBoxSyncManager.Instance.FindSpawnTemplate(cleanPartName, cleanPartName);
				if (bcbTemplate != null) return bcbTemplate;
			}

			// 2. Direct GameObject.Find in loaded scene objects
			GameObject direct = GameObject.Find(cleanPartName);
			if (direct != null) return direct;

			direct = GameObject.Find(cleanPartName + "(Clone)");
			if (direct != null) return direct;

			direct = GameObject.Find(cleanPartName + "(itemx)");
			if (direct != null) return direct;

			return null;
		}

		public static GameObject FindPartTemplateInResources(string cleanPartName)
		{
			return FindCatalogPartTemplate(cleanPartName);
		}

		private void ScanAndHookParcels()
		{
			GameObject store = GameObject.Find("STORE");
			if (store == null) return;
			PlayMakerFSM[] array = store.GetComponentsInChildren<PlayMakerFSM>(true);
			if (array == null) return;

			foreach (PlayMakerFSM playMakerFSM in array)
			{
				if (playMakerFSM == null || playMakerFSM.gameObject == null) continue;
				string text = playMakerFSM.gameObject.name;
				string rootText = (playMakerFSM.transform.root != null) ? playMakerFSM.transform.root.name : "";
				bool isBox = IsParcelBox(text) || IsParcelBox(rootText) || IsParcelBox(playMakerFSM.gameObject);
				if (!isBox) continue;

				GameObject boxGo = playMakerFSM.gameObject;
				int instanceID = boxGo.GetInstanceID();
				if (hookedParcels.Contains(instanceID)) continue;
				hookedParcels.Add(instanceID);

				ParcelUnboxTracker tracker = boxGo.GetComponent<ParcelUnboxTracker>();
				if (tracker == null)
				{
					tracker = boxGo.AddComponent<ParcelUnboxTracker>();
					tracker.BoxName = boxGo.name;
				}

				Transform targetTr = boxGo.transform;
				string boxName = boxGo.name;
				int localId = instanceID;
				SafeFsmWatcher.Attach(playMakerFSM, new string[9] { "1", "Open", "Assemble", "Unbox", "AssembleItems", "OPEN", "ASSEMBLE", "State 2", "Spawn" }, delegate
				{
					if (suppressedParcels.Contains(localId))
					{
						suppressedParcels.Remove(localId);
					}
					else if (!isNetworkApplying && targetTr != null)
					{
						int itmIdx = (tracker != null) ? tracker.ItemIndex : -1;
						if (tracker != null)
						{
							tracker.WasTriggered = true;
						}
						StartCoroutine(InitiatorUnboxCoroutine(targetTr.position, boxGo, boxName, itmIdx));
					}
				});
				ExtendedSyncDebugHUD.Log("<color=#33ff33>[PARTS]</color> Найдена посылка " + boxName + ", хук распаковки подключен!");
			}
		}

		public IEnumerator InitiatorUnboxCoroutine(Vector3 boxPos, GameObject boxGo, string boxName, int itemIndex = -1)
		{
			yield return new WaitForFixedUpdate();
			yield return new WaitForSeconds(0.15f);

			if (isSceneResetting) yield break;

			string cleanPartName = "";
			if (boxGo != null)
			{
				cleanPartName = FindSpawnedPartNameFromFsm(boxGo);
				if (itemIndex < 0)
				{
					ParcelUnboxTracker trk = boxGo.GetComponent<ParcelUnboxTracker>();
					if (trk != null && trk.ItemIndex >= 0)
					{
						itemIndex = trk.ItemIndex;
					}
				}
			}
			if (!string.IsNullOrEmpty(cleanPartName))
			{
				cleanPartName = UniversalHandItemSync.GetCleanItemName(cleanPartName);
			}

			// Check tight 1.5m radius for spawned part, strictly filtering out vehicle parts
			Collider[] colliders = Physics.OverlapSphere(boxPos, 1.5f);
			GameObject foundPart = null;

			if (colliders != null)
			{
				for (int i = 0; i < colliders.Length; i++)
				{
					Collider c = colliders[i];
					if (c == null) continue;
					Rigidbody rb = c.attachedRigidbody;
					if (rb == null) continue;
					GameObject go = rb.gameObject;
					if (go == null) continue;

					string n = go.name;
					string rootN = (go.transform.root != null) ? go.transform.root.name : "";

					// Strictly ignore player, store, yard, envelope, satsuma, car tracking, fenders, parcel boxes
					if (n.IndexOf("PLAYER", StringComparison.OrdinalIgnoreCase) >= 0 ||
						rootN.IndexOf("PLAYER", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("STORE", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("YARD", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("SATSUMA", StringComparison.OrdinalIgnoreCase) >= 0 ||
						rootN.IndexOf("SATSUMA", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("CarTracking", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("fender", StringComparison.OrdinalIgnoreCase) >= 0 ||
						n.IndexOf("drive1", StringComparison.OrdinalIgnoreCase) >= 0 ||
						(boxGo != null && (go == boxGo || go.transform.root == boxGo.transform.root)) ||
						IsParcelBox(n) || IsParcelBox(rootN))
					{
						continue;
					}

					string candName = UniversalHandItemSync.GetCleanItemName(n);
					if (IsAmisCatalogPart(candName) || (!string.IsNullOrEmpty(cleanPartName) && candName.IndexOf(cleanPartName, StringComparison.OrdinalIgnoreCase) >= 0))
					{
						foundPart = go;
						cleanPartName = MatchAmisCatalogPart(candName);
						break;
					}
				}
			}

			if (string.IsNullOrEmpty(cleanPartName))
			{
				cleanPartName = MatchAmisCatalogPart(ExtractPartNameFromBox(boxName));
			}

			if (string.IsNullOrEmpty(cleanPartName) && lastOrderItems.Count > 0)
			{
				cleanPartName = MatchAmisCatalogPart(lastOrderItems[0]);
			}

			if (string.IsNullOrEmpty(cleanPartName))
			{
				cleanPartName = "spoilers";
			}

			if (itemIndex < 0 && lastOrderItems != null && lastOrderItems.Count > 0)
			{
				for (int idx = 0; idx < lastOrderItems.Count; idx++)
				{
					string oName = UniversalHandItemSync.GetCleanItemName(lastOrderItems[idx]);
					if (string.Equals(oName, cleanPartName, StringComparison.OrdinalIgnoreCase) ||
					    cleanPartName.IndexOf(oName, StringComparison.OrdinalIgnoreCase) >= 0 ||
					    oName.IndexOf(cleanPartName, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						itemIndex = idx;
						break;
					}
				}
			}
			if (itemIndex < 0) itemIndex = 0;

			int netHash = ("msc_parcel_" + cleanPartName + "_" + itemIndex).GetHashFNV_1a();

			if (foundPart != null)
			{
				Rigidbody rb = foundPart.GetComponent<Rigidbody>() ?? foundPart.GetComponentInChildren<Rigidbody>();
				if (rb != null)
				{
					try
					{
						if (NetRigidbodyManager.GetRigidbodyHash(rb) == 0)
						{
							NetRigidbodyManager.AddRigidbody(rb, netHash);
						}
					}
					catch (Exception ex)
					{
						ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
					}

					BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(foundPart);
					BetterCheatBoxSyncManager.UpdateNetRigidbodyCache(foundPart, rb.position, rb.rotation);
				}

				BroadcastCatalogPartUnbox(cleanPartName, itemIndex, foundPart.transform.position, foundPart.transform.rotation);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>[AMIS AUTO]</color> Инициатор распаковал деталь: " + cleanPartName + " (NetHash: " + netHash + ")");
			}
			else
			{
				BroadcastCatalogPartUnbox(cleanPartName, itemIndex, boxPos, Quaternion.identity);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>[AMIS AUTO]</color> Инициатор распаковал деталь (по реестру): " + cleanPartName + " (NetHash: " + netHash + ")");
			}
		}

		public void BroadcastCatalogPartUnbox(string cleanPartName, Vector3 pos, Quaternion rot)
		{
			BroadcastCatalogPartUnbox(cleanPartName, 0, pos, rot);
		}

		public void BroadcastCatalogPartUnbox(string cleanPartName, int itemIndex, Vector3 pos, Quaternion rot)
		{
			if (isNetworkApplying) return;
			using (GameEventWriter writer = catalogPartUnboxEvent.Writer())
			{
				writer.Write(cleanPartName ?? "");
				writer.Write(itemIndex);
				writer.Write(pos.x);
				writer.Write(pos.y);
				writer.Write(pos.z);
				writer.Write(rot.x);
				writer.Write(rot.y);
				writer.Write(rot.z);
				writer.Write(rot.w);
				catalogPartUnboxEvent.Send(writer, 0uL, safe: true);
				ExtendedSyncDebugHUD.Log("<color=#33ff33>OUT [AMIS AUTO]: Распаковка " + cleanPartName + " [#" + itemIndex + "] на " + pos.ToString("F1") + "</color>");
			}
		}

		private void OnReceiveCatalogPartUnbox(GameEventReader reader)
		{
			string cleanPartName = "";
			int itemIndex = 0;
			Vector3 pos = Vector3.zero;
			Quaternion rot = Quaternion.identity;

			try
			{
				cleanPartName = reader.ReadString();
				long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
				if (remaining >= 32)
				{
					itemIndex = reader.ReadInt32();
				}
				pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
				return;
			}

			ExtendedSyncDebugHUD.Log("<color=#33ff33>IN [AMIS AUTO]: Получена распаковка " + cleanPartName + " [#" + itemIndex + "] на " + pos.ToString("F1") + "</color>");
			isNetworkApplying = true;
			try
			{
				HandleCatalogPartUnbox(cleanPartName, itemIndex, pos, rot);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void HandleCatalogPartUnbox(string cleanPartName, Vector3 pos, Quaternion rot)
		{
			HandleCatalogPartUnbox(cleanPartName, 0, pos, rot);
		}

		public void HandleCatalogPartUnbox(string cleanPartName, int itemIndex, Vector3 pos, Quaternion rot)
		{
			if (string.IsNullOrEmpty(cleanPartName)) return;

			// 1. Check if part already exists within 2.0m
			Collider[] colliders = Physics.OverlapSphere(pos, 2.0f);
			GameObject spawnedPart = null;
			if (colliders != null)
			{
				for (int i = 0; i < colliders.Length; i++)
				{
					if (colliders[i] == null) continue;
					GameObject go = (colliders[i].attachedRigidbody != null) ? colliders[i].attachedRigidbody.gameObject : colliders[i].gameObject;
					if (go == null) continue;
					string cleanFound = UniversalHandItemSync.GetCleanItemName(go.name);
					if (string.Equals(cleanFound, cleanPartName, StringComparison.OrdinalIgnoreCase) || cleanFound.IndexOf(cleanPartName, StringComparison.OrdinalIgnoreCase) >= 0)
					{
						spawnedPart = go;
						break;
					}
				}
			}

			// 2. If not found within 2m, instantiate
			if (spawnedPart == null)
			{
				GameObject template = FindCatalogPartTemplate(cleanPartName);
				if (template != null)
				{
					spawnedPart = (GameObject)UnityEngine.Object.Instantiate(template, pos, rot);
					spawnedPart.name = cleanPartName + "(Clone)";
					spawnedPart.SetActive(true);
					int partsLayer = LayerMask.NameToLayer("Parts");
					spawnedPart.layer = (partsLayer != -1) ? partsLayer : 19;
					foreach (var r in spawnedPart.GetComponentsInChildren<Renderer>(true))
					{
						if (r != null) r.enabled = true;
					}
					foreach (var c in spawnedPart.GetComponentsInChildren<Collider>(true))
					{
						if (c != null)
						{
							c.enabled = true;
							c.isTrigger = false;
						}
					}
					ExtendedSyncDebugHUD.Log("<color=#33ff33>[AMIS AUTO]</color> Деталь материализована: " + spawnedPart.name + " на " + pos.ToString("F1"));
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ffaa00>WARN [AMIS AUTO]: Шаблон детали " + cleanPartName + " не найден в сцене/BetterCheatBox!</color>");
				}
			}
			else
			{
				ExtendedSyncDebugHUD.Log("<color=#33ff33>[AMIS AUTO]</color> Деталь " + cleanPartName + " уже существует рядом (" + spawnedPart.name + ")");
			}

			// 3. Connect physics to WreckMP network stack
			int netHash = ("msc_parcel_" + cleanPartName + "_" + itemIndex).GetHashFNV_1a();
			if (spawnedPart != null)
			{
				Rigidbody rb = spawnedPart.GetComponent<Rigidbody>() ?? spawnedPart.AddComponent<Rigidbody>();
				rb.isKinematic = false;
				rb.useGravity = true;
				if (NetRigidbodyManager.GetRigidbodyHash(rb) == 0)
				{
					try
					{
						NetRigidbodyManager.AddRigidbody(rb, netHash);
					}
					catch (Exception ex)
					{
						ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
					}
				}
				BetterCheatBoxSyncManager.ResetRigidbodyPhysicsLocal(spawnedPart);
				BetterCheatBoxSyncManager.UpdateNetRigidbodyCache(spawnedPart, pos, rot);
			}

			// 4. Destroy empty parcel box within 2.5 meters
			Collider[] nearby = Physics.OverlapSphere(pos, 2.5f);
			if (nearby != null)
			{
				for (int i = 0; i < nearby.Length; i++)
				{
					Collider c = nearby[i];
					if (c == null) continue;
					GameObject go = (c.attachedRigidbody != null) ? c.attachedRigidbody.gameObject : c.gameObject;
					if (go == null) continue;

					string n = go.name;
					string rootN = (go.transform.root != null) ? go.transform.root.name : "";
					if (IsParcelBox(n) || IsParcelBox(rootN) || IsParcelBox(go) || (go.transform.root != null && IsParcelBox(go.transform.root.gameObject)))
					{
						GameObject box = (go.transform.root != null && (IsParcelBox(rootN) || IsParcelBox(go.transform.root.gameObject))) ? go.transform.root.gameObject : go;
						suppressedParcels.Add(box.GetInstanceID());
						ParcelUnboxTracker tracker = box.GetComponent<ParcelUnboxTracker>();
						if (tracker != null)
						{
							tracker.WasTriggered = true;
						}
						foreach (var r in box.GetComponentsInChildren<Renderer>(true))
						{
							if (r != null) r.enabled = false;
						}
						foreach (var col in box.GetComponentsInChildren<Collider>(true))
						{
							if (col != null) col.enabled = false;
						}
						box.SetActive(false);
						UnityEngine.Object.Destroy(box, 0.1f);
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[AMIS AUTO]</color> Пустая коробка " + box.name + " удалена у напарника.");
						break;
					}
				}
			}
		}

		public void BroadcastUniversalPartSpawn(string cleanPartName, Vector3 pos, Quaternion rot, int netHash)
		{
			BroadcastCatalogPartUnbox(cleanPartName, 0, pos, rot);
		}

		private void OnReceiveUniversalPartSpawn(GameEventReader reader)
		{
			try
			{
				string cleanPartName = reader.ReadString();
				Vector3 pos = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				Quaternion rot = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				HandleCatalogPartUnbox(cleanPartName, 0, pos, rot);
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
		}

		public void HandleUniversalPartSpawn(string cleanPartName, Vector3 pos, Quaternion rot, int netHash)
		{
			HandleCatalogPartUnbox(cleanPartName, 0, pos, rot);
		}

		public void BroadcastParcelUnbox(Vector3 pos, string boxName = "", string partName = "", int orderIndex = -1)
		{
			string cleanPart = MatchAmisCatalogPart(!string.IsNullOrEmpty(partName) ? partName : ExtractPartNameFromBox(boxName));
			BroadcastCatalogPartUnbox(cleanPart, (orderIndex >= 0 ? orderIndex : 0), pos, Quaternion.identity);
		}

		private void OnReceiveParcelUnbox(GameEventReader reader)
		{
			try
			{
				Vector3 b = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				string boxName = reader.ReadString();
				string partName = (reader.BaseStream.Position < reader.BaseStream.Length) ? reader.ReadString() : "";
				string clean = MatchAmisCatalogPart(!string.IsNullOrEmpty(partName) ? partName : ExtractPartNameFromBox(boxName));
				HandleCatalogPartUnbox(clean, 0, b, Quaternion.identity);
			}
			catch (Exception ex)
			{
				ModConsole.Error("[WreckMP ExtendedSync Error]: " + ex.Message);
			}
		}

		public IEnumerator RegisterUnpackedPartsCoroutine(Vector3 pos, string boxName, string partName, int orderIndex, bool isPayer)
		{
			yield break;
		}
	}
	public class BetterCheatBoxSyncManager : MonoBehaviour
	{
		public static BetterCheatBoxSyncManager Instance;

		public static GameObject cachedSatsuma;

		public static float nextSatsumaWatchdogTime;

		public bool isNetworkApplying;

		public bool isStateCacheInitialized;

		public bool isHarmonyPatched;
		public bool suppressSkipPostOrder;
		public float lastPostOrderSkipTime;

		private GameEvent cheatTeleportEvent;

		private GameEvent cheatSpawnEvent;

		private GameEvent cheatMoneyEvent;

		private GameEvent cheatNeedsEvent;

		private GameEvent cheatTimeEvent;

		private GameEvent cheatSkipEvent;

		private GameEvent cheatVehicleEvent;

		private GameEvent cheatKeyEvent;

		private GameEvent cheatPoliceEvent;

		private GameEvent cheatHouseFloorEvent;

		private GameEvent cheatRepairEvent;

		private float cachedMoney = -999999f;

		private bool cachedNeeds = true;

		private float cachedFatigue = -1f;

		private float cachedTimeScale = -1f;

		private int cachedDay = -1;

		private float cachedPhysicsSpeed = 1f;

		private bool cachedRoadCops;

		private bool cachedHomeCops;

		private bool cachedSatsumaFire;

		private int cachedUncleStage = -1;

		private float cachedKiljuTime = -1f;

		private float cachedFleetariOrderTime = -1f;

		private float[] cachedFuel = new float[16];

		private int[] cachedTires = new int[4];

		private Vector3[] cachedCornerAngles = new Vector3[4];

		private int[] cachedKeys = new int[16];

		private Vector3[] cachedStains = new Vector3[4];

		private float nextSlowCheck;

		public static BetterCheatBox cachedBcbInstance;

		public static BetterCheatBox GetBetterCheatBox()
		{
			if (cachedBcbInstance != null)
			{
				return cachedBcbInstance;
			}
			if (ModLoader.LoadedMods != null)
			{
				for (int i = 0; i < ModLoader.LoadedMods.Count; i++)
				{
					if (ModLoader.LoadedMods[i] != null && ModLoader.LoadedMods[i].ID == "BetterCheatBox")
					{
						cachedBcbInstance = ModLoader.LoadedMods[i] as BetterCheatBox;
						return cachedBcbInstance;
					}
				}
			}
			return null;
		}

		public void BroadcastRepair(bool fixAll, bool tightenBolts)
		{
			BroadcastFullRepair(fixAll, tightenBolts, tuneEngine: false);
		}

		public void BroadcastSkipTimer(string timerType)
		{
			BroadcastSkip(timerType);
		}

		private static readonly Dictionary<string, string> KeyVarMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "Ferndale key", "PlayerKeyFerndale" },
			{ "Gifu key", "PlayerKeyGifu" },
			{ "Hayosiko key", "PlayerKeyHayosiko" },
			{ "Home key", "PlayerKeyHome" },
			{ "Ruscko key", "PlayerKeyRuscko" },
			{ "Satsuma key", "PlayerKeySatsuma" }
		};

		public static string ResolveKeyVarName(string name)
		{
			if (string.IsNullOrEmpty(name)) return "";
			if (KeyVarMap.TryGetValue(name, out string mapped)) return mapped;
			if (name.StartsWith("PlayerKey", StringComparison.OrdinalIgnoreCase)) return name;
			return "PlayerKey" + name.Replace(" key", "").Replace(" ", "");
		}

		public static bool IsVehicleName(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			string n = name.ToLower();
			return n.Contains("satsuma") || n.Contains("hayosiko") || n.Contains("ruscko") ||
			       n.Contains("ferndale") || n.Contains("jonnez") || n.Contains("gifu") ||
			       n.Contains("kekmet") || n.Contains("boat") || n.Contains("flatbed") ||
			       n.Contains("fittan") || n.Contains("bus") || n.Contains("kuski") ||
			       n.Contains("kylajani") || n.Contains("amis") || n.Contains("menace") ||
			       n.Contains("combine");
		}

		public GameObject FindInBetterCheatBoxMulti(string friendlyName, int subIndex)
		{
			if (string.IsNullOrEmpty(friendlyName) || subIndex < 0) return null;

			BetterCheatBox bcb = GetBetterCheatBox();
			if (bcb == null || bcb.tpItemsButtons == null) return null;

			for (int i = 0; i < bcb.tpItemsButtons.Length; i++)
			{
				var btn = bcb.tpItemsButtons[i];
				if (btn == null || btn.items == null) continue;
				for (int j = 0; j < btn.items.Length; j++)
				{
					var item = btn.items[j];
					if (item == null) continue;
					if (string.Equals(item.buttonName, friendlyName, StringComparison.OrdinalIgnoreCase))
					{
						if (item.transforms != null && subIndex < item.transforms.Length && item.transforms[subIndex] != null)
						{
							return item.transforms[subIndex].gameObject;
						}
					}
				}
			}

			return null;
		}

		private void Awake()
		{
			Instance = this;
			GetBetterCheatBox();
		}

		private void Start()
		{
			cheatTeleportEvent = new GameEvent("Cheat_Teleport", OnReceiveTeleport);
			cheatSpawnEvent = new GameEvent("Cheat_Spawn", OnReceiveSpawn);
			cheatMoneyEvent = new GameEvent("Cheat_Money", OnReceiveMoney);
			cheatNeedsEvent = new GameEvent("Cheat_Needs", OnReceiveNeeds);
			cheatTimeEvent = new GameEvent("Cheat_Time", OnReceiveTime);
			cheatSkipEvent = new GameEvent("Cheat_Skip", OnReceiveSkip);
			cheatVehicleEvent = new GameEvent("Cheat_Vehicle", OnReceiveVehicle);
			cheatKeyEvent = new GameEvent("Cheat_Key", OnReceiveKey);
			cheatPoliceEvent = new GameEvent("Cheat_Police", OnReceivePolice);
			cheatHouseFloorEvent = new GameEvent("Cheat_HouseFloor", OnReceiveHouseFloor);
			cheatRepairEvent = new GameEvent("Cheat_RepairVehicle", OnReceiveRepair);
			InitializeHarmony();
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			isStateCacheInitialized = false;
			cachedSatsuma = null;
			nextSatsumaWatchdogTime = 0f;
			GetBetterCheatBox();
			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyInitCheatBox());
			}
		}

		private void InitializeHarmony()
		{
			if (isHarmonyPatched)
			{
				return;
			}
			try
			{
				HarmonyInstance harmonyInstance = HarmonyInstance.Create("com.wreckmp.extendedsync.bettercheatbox");
				Type typeFromHandle = typeof(BetterCheatBox);
				if (typeFromHandle != null)
				{
					MethodInfo method = typeFromHandle.GetMethod("TPToPlayer", BindingFlags.Instance | BindingFlags.Public);
					MethodInfo method2 = typeof(BetterCheatBoxPatches).GetMethod("TPToPlayer_Prefix", BindingFlags.Static | BindingFlags.Public);
					if (method != null && method2 != null)
					{
						harmonyInstance.Patch(method, new HarmonyMethod(method2));
					}
					MethodInfo method3 = typeFromHandle.GetMethod("SpawnAtPlayer", BindingFlags.Instance | BindingFlags.Public);
					MethodInfo method4 = typeof(BetterCheatBoxPatches).GetMethod("SpawnAtPlayer_Prefix", BindingFlags.Static | BindingFlags.Public);
					if (method3 != null && method4 != null)
					{
						harmonyInstance.Patch(method3, new HarmonyMethod(method4));
					}
					MethodInfo method5 = typeFromHandle.GetMethod("TPPlayerTo", BindingFlags.Instance | BindingFlags.Public);
					MethodInfo method6 = typeof(BetterCheatBoxPatches).GetMethod("TPPlayerTo_Prefix", BindingFlags.Static | BindingFlags.Public);
					if (method5 != null && method6 != null)
					{
						harmonyInstance.Patch(method5, new HarmonyMethod(method6));
					}
					MethodInfo mSendEvent = typeof(PlayMakerFSM).GetMethod("SendEvent", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(string) }, null);
					MethodInfo pSendEvent = typeof(BetterCheatBoxPatches).GetMethod("PlayMakerFSM_SendEvent_Prefix", BindingFlags.Public | BindingFlags.Static);
					if (mSendEvent != null && pSendEvent != null)
					{
						harmonyInstance.Patch(mSendEvent, new HarmonyMethod(pSendEvent), null, null);
					}
					isHarmonyPatched = true;
					ModConsole.Print("<color=green>[BCB Sync]</color> Harmony перехватчики BetterCheatBox успешно установлены!");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Ошибка установки Harmony патчей: " + ex.Message);
			}
		}

		private IEnumerator LazyInitCheatBox()
		{
			while (!isStateCacheInitialized)
			{
				if (Application.loadedLevelName != "GAME")
				{
					yield return new WaitForSeconds(3f);
					continue;
				}
				BetterCheatBox bcb = GetBetterCheatBox();
				if (bcb != null)
				{
					InitializeCache(bcb);
					if (bcb.orderFsm != null)
					{
						SafeFsmWatcher.Attach(bcb.orderFsm, new string[4] { "FINISHED", "Finished", "State 2", "Done" }, delegate
						{
							if (!isNetworkApplying && !suppressSkipPostOrder && (Time.time - lastPostOrderSkipTime >= 2f))
							{
								BroadcastSkip("POST_ORDER");
							}
						});
					}
					if (bcb.cloudFsm != null)
					{
						SafeFsmWatcher.Attach(bcb.cloudFsm, new string[3] { "Randomize", "Set cloud", "Move clouds" }, delegate
						{
							if (!isNetworkApplying && bcb.guiShow)
							{
								BroadcastSkip("WEATHER");
							}
						});
					}
					if (bcb.inventoryFsm != null)
					{
						SafeFsmWatcher.Attach(bcb.inventoryFsm, new string[3] { "DAY", "Proceed", "Restock" }, delegate
						{
							if (!isNetworkApplying && bcb.guiShow)
							{
								BroadcastSkip("RESTOCK");
							}
						});
					}
					ExtendedSyncDebugHUD.Log("<color=#00ffcc>[BCB SYNC]</color> BetterCheatBox успешно подключен к синхронизации P2P!");
					break;
				}
				yield return new WaitForSeconds(2f);
			}
		}

		private void InitializeCache(BetterCheatBox bcb)
		{
			if (bcb == null)
			{
				return;
			}
			cachedMoney = ((bcb.money != null) ? bcb.money.Value : (FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney")?.Value ?? 0f));
			cachedNeeds = bcb.needs;
			cachedFatigue = ((bcb.fatigue != null) ? bcb.fatigue.Value : 0f);
			cachedTimeScale = ((bcb.timeScale != null) ? bcb.timeScale.Value : 600f);
			cachedDay = ((bcb.day == null) ? 1 : bcb.day.Value);
			cachedPhysicsSpeed = Time.timeScale;
			cachedRoadCops = bcb.copController != null && bcb.copController.activeSelf;
			cachedHomeCops = bcb.copsAtHome != null && bcb.copsAtHome.activeSelf;
			cachedSatsumaFire = bcb.satsumaFire != null && bcb.satsumaFire.activeSelf;
			cachedUncleStage = ((bcb.uncleStage != null) ? bcb.uncleStage.Value : 0);
			cachedKiljuTime = ((bcb.kiljuTime != null) ? bcb.kiljuTime.Value : 0f);
			cachedFleetariOrderTime = ((!(bcb.workOrderFsm != null)) ? 0f : (bcb.workOrderFsm.FsmVariables.FindFsmFloat("_OrderTime")?.Value ?? 0f));
			if (bcb.carDebugEntries != null)
			{
				for (int i = 0; i < bcb.carDebugEntries.Length && i < cachedFuel.Length; i++)
				{
					cachedFuel[i] = ((bcb.carDebugEntries[i]?.fuelLevel != null) ? bcb.carDebugEntries[i].fuelLevel.Value : 0f);
				}
			}
			if (bcb.suspensionDamageDisabler != null)
			{
				SuspensionDamageDisabler suspensionDamageDisabler = bcb.suspensionDamageDisabler;
				if (suspensionDamageDisabler.tireTypes != null)
				{
					for (int j = 0; j < suspensionDamageDisabler.tireTypes.Length && j < cachedTires.Length; j++)
					{
						cachedTires[j] = ((suspensionDamageDisabler.tireTypes[j] != null) ? suspensionDamageDisabler.tireTypes[j].Value : 0);
					}
				}
				if (suspensionDamageDisabler.corners != null)
				{
					for (int k = 0; k < suspensionDamageDisabler.corners.Length && k < cachedCornerAngles.Length; k++)
					{
						cachedCornerAngles[k] = ((suspensionDamageDisabler.corners[k]?.Value != null) ? suspensionDamageDisabler.corners[k].Value.transform.localEulerAngles : Vector3.zero);
					}
				}
			}
			if (bcb.playerKeys != null)
			{
				for (int l = 0; l < bcb.playerKeys.Length && l < cachedKeys.Length; l++)
				{
					cachedKeys[l] = ((bcb.playerKeys[l]?.keyValue != null) ? bcb.playerKeys[l].keyValue.Value : 0);
				}
			}
			if (bcb.pissStains != null)
			{
				for (int m = 0; m < bcb.pissStains.Length && m < cachedStains.Length; m++)
				{
					cachedStains[m] = ((bcb.pissStains[m]?.transform != null) ? bcb.pissStains[m].transform.localScale : Vector3.one);
				}
			}
			isStateCacheInitialized = true;
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME")
			{
				return;
			}

			// 1. Anti-Despawn Watchdog (every 3 seconds)
			if (Time.time > nextSatsumaWatchdogTime)
			{
				nextSatsumaWatchdogTime = Time.time + 3.0f;
				if (cachedSatsuma == null)
				{
					cachedSatsuma = GameObject.Find("SATSUMA(504kg, 330)") ?? GameObject.Find("SATSUMA(580kg, 240hp)");
				}
				if (cachedSatsuma != null && !cachedSatsuma.activeInHierarchy)
				{
					Transform curr = cachedSatsuma.transform;
					while (curr != null)
					{
						curr.gameObject.SetActive(true);
						curr = curr.parent;
					}
					cachedSatsuma.transform.parent = null;
					cachedSatsuma.SetActive(true);
					foreach (var r in cachedSatsuma.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
					foreach (var c in cachedSatsuma.GetComponentsInChildren<Collider>(true)) c.enabled = true;

					ExtendedSyncDebugHUD.Log("<color=#00ffcc>[WRECKMP SYNC] [WATCHDOG]: Сацума была скрыта WreckMP! Принудительно возвращена в активное состояние.</color>");
					ModConsole.Print("[WRECKMP SYNC] [WATCHDOG]: Сацума была скрыта WreckMP! Принудительно возвращена в активное состояние.");
				}
			}

			// 2. Manual Revive Hotkey (Ctrl + F9)
			if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.F9))
			{
				Vector3 garagePos = new Vector3(-10.5f, 4.4f, 7.5f);
				Quaternion garageRot = Quaternion.Euler(0, 90f, 0);
				if (ReviveAndTeleportSatsuma(garagePos, garageRot))
				{
					PlayMakerFSM.BroadcastEvent("SATSUMA_REVIVED");
					ExtendedSyncDebugHUD.Log("<color=#00ffcc>⚡ [REVIVE]: Сацума успешно воскрешена в гараж!</color>");
					ModConsole.Print("[WRECKMP SYNC] [REVIVE]: Сацума успешно воскрешена в гараж!");
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [REVIVE]: Сацума не найдена в памяти игры!</color>");
					ModConsole.Print("[WRECKMP SYNC] [REVIVE]: Сацума не найдена в памяти игры!");
				}
			}

			if (isNetworkApplying)
			{
				return;
			}
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null)
			{
				return;
			}
			if (!isStateCacheInitialized)
			{
				InitializeCache(betterCheatBox);
				return;
			}
			if (betterCheatBox.money != null)
			{
				float value = betterCheatBox.money.Value;
				if (betterCheatBox.guiShow && Math.Abs(value - cachedMoney) > 0.05f)
				{
					cachedMoney = value;
					BroadcastMoney(value);
				}
				else
				{
					cachedMoney = value;
				}
			}
			if (betterCheatBox.needs != cachedNeeds)
			{
				cachedNeeds = betterCheatBox.needs;
				BroadcastToggleNeeds(cachedNeeds);
			}
			if (betterCheatBox.fatigue != null)
			{
				float value2 = betterCheatBox.fatigue.Value;
				if (value2 >= 99.5f && cachedFatigue < 99.0f && betterCheatBox.guiShow)
				{
					cachedFatigue = value2;
					BroadcastFatigue(value2);
				}
				else
				{
					cachedFatigue = value2;
				}
			}
			if (betterCheatBox.timeScale != null)
			{
				float value3 = betterCheatBox.timeScale.Value;
				if (Math.Abs(value3 - cachedTimeScale) > 0.05f && betterCheatBox.guiShow)
				{
					cachedTimeScale = value3;
					BroadcastTimeScale(value3);
				}
			}
			if (betterCheatBox.day != null)
			{
				int value4 = betterCheatBox.day.Value;
				if (value4 != cachedDay && betterCheatBox.guiShow)
				{
					cachedDay = value4;
					BroadcastDay(value4);
				}
			}
			float timeScale = Time.timeScale;
			if (Math.Abs(timeScale - cachedPhysicsSpeed) > 0.01f && betterCheatBox.guiShow)
			{
				cachedPhysicsSpeed = timeScale;
				BroadcastPhysicsSpeed(timeScale);
			}
			bool flag = betterCheatBox.copController != null && betterCheatBox.copController.activeSelf;
			bool flag2 = betterCheatBox.copsAtHome != null && betterCheatBox.copsAtHome.activeSelf;
			if (flag != cachedRoadCops || flag2 != cachedHomeCops)
			{
				cachedRoadCops = flag;
				cachedHomeCops = flag2;
				if (betterCheatBox.guiShow)
				{
					BroadcastPolice(flag, flag2);
				}
			}
			bool flag3 = betterCheatBox.satsumaFire != null && betterCheatBox.satsumaFire.activeSelf;
			if (flag3 != cachedSatsumaFire)
			{
				cachedSatsumaFire = flag3;
				if (betterCheatBox.guiShow)
				{
					BroadcastSatsumaFire(flag3);
				}
			}
			if (!(Time.time > nextSlowCheck))
			{
				return;
			}
			nextSlowCheck = Time.time + 0.2f;
			if (betterCheatBox.carDebugEntries != null && betterCheatBox.guiShow)
			{
				for (int i = 0; i < betterCheatBox.carDebugEntries.Length && i < cachedFuel.Length; i++)
				{
					CarDebugEntry carDebugEntry = betterCheatBox.carDebugEntries[i];
					if (carDebugEntry != null && carDebugEntry.fuelLevel != null)
					{
						float value5 = carDebugEntry.fuelLevel.Value;
						if (Math.Abs(value5 - cachedFuel[i]) > 0.1f && carDebugEntry.menuOpen)
						{
							cachedFuel[i] = value5;
							BroadcastFuel(carDebugEntry.buttonName, value5);
						}
					}
				}
			}
			if (betterCheatBox.suspensionDamageDisabler != null)
			{
				SuspensionDamageDisabler suspensionDamageDisabler = betterCheatBox.suspensionDamageDisabler;
				if (suspensionDamageDisabler.tireTypes != null && betterCheatBox.guiShow)
				{
					for (int j = 0; j < suspensionDamageDisabler.tireTypes.Length && j < cachedTires.Length; j++)
					{
						if (suspensionDamageDisabler.tireTypes[j] != null)
						{
							int value6 = suspensionDamageDisabler.tireTypes[j].Value;
							if (value6 != cachedTires[j])
							{
								cachedTires[j] = value6;
								BroadcastTire(j, value6);
							}
						}
					}
				}
				if (suspensionDamageDisabler.corners != null && betterCheatBox.guiShow)
				{
					for (int k = 0; k < suspensionDamageDisabler.corners.Length && k < cachedCornerAngles.Length; k++)
					{
						if (suspensionDamageDisabler.corners[k] != null && suspensionDamageDisabler.corners[k].Value != null)
						{
							Vector3 localEulerAngles = suspensionDamageDisabler.corners[k].Value.transform.localEulerAngles;
							if (localEulerAngles == Vector3.zero && cachedCornerAngles[k] != Vector3.zero)
							{
								cachedCornerAngles[k] = localEulerAngles;
								BroadcastStraightenSuspension(k);
							}
							else
							{
								cachedCornerAngles[k] = localEulerAngles;
							}
						}
					}
				}
			}
			if (betterCheatBox.playerKeys != null && betterCheatBox.guiShow)
			{
				for (int l = 0; l < betterCheatBox.playerKeys.Length && l < cachedKeys.Length; l++)
				{
					PlayerKey playerKey = betterCheatBox.playerKeys[l];
					if (playerKey != null && playerKey.keyValue != null)
					{
						int value7 = playerKey.keyValue.Value;
						if (value7 != cachedKeys[l])
						{
							cachedKeys[l] = value7;
							BroadcastKey(playerKey.buttonName, value7);
						}
					}
				}
			}
			if (betterCheatBox.pissStains != null && betterCheatBox.guiShow)
			{
				for (int m = 0; m < betterCheatBox.pissStains.Length && m < cachedStains.Length; m++)
				{
					PissObject pissObject = betterCheatBox.pissStains[m];
					if (pissObject != null && pissObject.transform != null)
					{
						Vector3 localScale = pissObject.transform.localScale;
						if ((localScale - cachedStains[m]).sqrMagnitude > 0.001f)
						{
							cachedStains[m] = localScale;
							BroadcastPissStain(m, localScale.x, localScale.y);
						}
					}
				}
			}
			if (betterCheatBox.uncleStage != null)
			{
				int value8 = betterCheatBox.uncleStage.Value;
				if (value8 == 5 && cachedUncleStage < 5 && betterCheatBox.guiShow)
				{
					cachedUncleStage = value8;
					BroadcastSkip("UNCLE");
				}
				else
				{
					cachedUncleStage = value8;
				}
			}
			if (betterCheatBox.kiljuTime != null)
			{
				float value9 = betterCheatBox.kiljuTime.Value;
				if (value9 >= 1000f && cachedKiljuTime < 1000f && betterCheatBox.guiShow)
				{
					cachedKiljuTime = value9;
					BroadcastSkip("KILJU");
				}
				else
				{
					cachedKiljuTime = value9;
				}
			}
			if (!(betterCheatBox.workOrderFsm != null) || !betterCheatBox.guiShow)
			{
				return;
			}
			FsmFloat fsmFloat = betterCheatBox.workOrderFsm.FsmVariables.FindFsmFloat("_OrderTime");
			if (fsmFloat != null)
			{
				float value10 = fsmFloat.Value;
				if (value10 == 0f && cachedFleetariOrderTime > 0f)
				{
					cachedFleetariOrderTime = value10;
					BroadcastSkip("REPAIR_WORK");
				}
				else
				{
					cachedFleetariOrderTime = value10;
				}
			}
		}

		public void BroadcastTeleportObject(GameObject go, Vector3 pos, Quaternion rot, string friendlyName, int subIndex = -1)
		{
			if (go == null || isNetworkApplying)
			{
				return;
			}
			ResetRigidbodyPhysicsAndClaim(go);
			UpdateNetRigidbodyCache(go, pos, rot);
			string gameObjectPath = GetGameObjectPath(go);
			using (GameEventWriter gameEventWriter = cheatTeleportEvent.Writer())
			{
				gameEventWriter.Write((byte)(subIndex >= 0 ? 2 : 0));
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(gameObjectPath);
				gameEventWriter.Write(friendlyName ?? go.name);
				gameEventWriter.Write(subIndex);
				gameEventWriter.Write(pos.x);
				gameEventWriter.Write(pos.y);
				gameEventWriter.Write(pos.z);
				gameEventWriter.Write(rot.x);
				gameEventWriter.Write(rot.y);
				gameEventWriter.Write(rot.z);
				gameEventWriter.Write(rot.w);
				cheatTeleportEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Телепортация к себе: " + (friendlyName ?? go.name) + (subIndex >= 0 ? " #" + subIndex : "") + "</color>");
		}

		public void SafeTeleportLocalPlayer(Vector3 pos, Quaternion rot, string locName)
		{
			GameObject gameObject = GameObject.Find("PLAYER");
			if (gameObject != null)
			{
				try
				{
					var curVeh = PlayMakerGlobals.Instance?.Variables?.FindFsmString("PlayerCurrentVehicle");
					if (curVeh != null && !string.IsNullOrEmpty(curVeh.Value))
					{
						PlayMakerFSM.BroadcastEvent("EXITDRIVING");
						curVeh.Value = "";
					}
				}
				catch {}

				CharacterController component = gameObject.GetComponent<CharacterController>();
				if (component != null)
				{
					component.enabled = false;
				}
				gameObject.transform.position = pos;
				if (rot != Quaternion.identity)
				{
					gameObject.transform.rotation = rot;
				}
				if (component != null)
				{
					component.enabled = true;
				}
				Rigidbody component2 = gameObject.GetComponent<Rigidbody>();
				if (component2 != null)
				{
					component2.velocity = Vector3.zero;
					component2.angularVelocity = Vector3.zero;
				}
				using (GameEventWriter gameEventWriter = cheatTeleportEvent.Writer())
				{
					gameEventWriter.Write((byte)1);
					gameEventWriter.Write(WreckMPGlobals.UserID);
					gameEventWriter.Write("PLAYER");
					gameEventWriter.Write(locName ?? "Location");
					gameEventWriter.Write(-1);
					gameEventWriter.Write(pos.x);
					gameEventWriter.Write(pos.y);
					gameEventWriter.Write(pos.z);
					gameEventWriter.Write(rot.x);
					gameEventWriter.Write(rot.y);
					gameEventWriter.Write(rot.z);
					gameEventWriter.Write(rot.w);
					cheatTeleportEvent.Send(gameEventWriter, 0uL, safe: true);
				}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Телепортация игрока -> " + (locName ?? "Место") + "</color>");
			}
		}

		public void BroadcastSpawnObject(string buttonName, string templateName, int count, Vector3 pos, Quaternion rot, string batchId = null, ulong senderSteamId = 0uL, int spawnerIndex = 0)
		{
			if (isNetworkApplying)
			{
				return;
			}
			if (senderSteamId == 0uL)
			{
				senderSteamId = WreckMPGlobals.UserID;
			}
			using (GameEventWriter gameEventWriter = cheatSpawnEvent.Writer())
			{
				gameEventWriter.Write(senderSteamId);
				gameEventWriter.Write(spawnerIndex);
				gameEventWriter.Write(buttonName ?? "");
				gameEventWriter.Write(templateName ?? "");
				gameEventWriter.Write(count);
				gameEventWriter.Write(pos.x);
				gameEventWriter.Write(pos.y);
				gameEventWriter.Write(pos.z);
				gameEventWriter.Write(rot.x);
				gameEventWriter.Write(rot.y);
				gameEventWriter.Write(rot.z);
				gameEventWriter.Write(rot.w);
				gameEventWriter.Write(batchId ?? "");
				cheatSpawnEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Спавн предмета " + buttonName + " x" + count + "</color>");
		}

		public void BroadcastMoney(float amount)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedMoney = amount;
			using (GameEventWriter gameEventWriter = cheatMoneyEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(amount);
				cheatMoneyEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Баланс установлен -> " + amount.ToString("N0") + " MK</color>");
		}

		public void BroadcastMoneyAdd(float delta)
		{
			float num = (cachedMoney > -900000f) ? cachedMoney : (FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney")?.Value ?? 0f);
			num += delta;
			ApplyMoneyLocal(num);
			BroadcastMoney(num);
		}

		public void ApplyMoneyLocal(float amount)
		{
			FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
			if (fsmFloat != null)
			{
				fsmFloat.Value = amount;
			}
			try
			{
				FsmFloat fsmFloat2 = PlayMakerGlobals.Instance?.Variables?.FindFsmFloat("PlayerMoney");
				if (fsmFloat2 != null)
				{
					fsmFloat2.Value = amount;
				}
			}
			catch {}
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox != null && betterCheatBox.money != null)
			{
				betterCheatBox.money.Value = amount;
				betterCheatBox.moneyTemp = amount;
			}
			cachedMoney = amount;
		}

		public void BroadcastToggleNeeds(bool enabled)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedNeeds = enabled;
			using (GameEventWriter gameEventWriter = cheatNeedsEvent.Writer())
			{
				gameEventWriter.Write((byte)0);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(enabled);
				cheatNeedsEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Потребности -> " + (enabled ? "Включены" : "Отключены (Godmode)") + "</color>");
		}

		public void BroadcastFatigue(float fatigue)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedFatigue = fatigue;
			using (GameEventWriter gameEventWriter = cheatNeedsEvent.Writer())
			{
				gameEventWriter.Write((byte)1);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(fatigue);
				cheatNeedsEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Усталость установлена на " + fatigue + "</color>");
		}

		public void ResetAllNeeds()
		{
			ResetAllNeedsLocal();
			using (GameEventWriter gameEventWriter = cheatNeedsEvent.Writer())
			{
				gameEventWriter.Write((byte)2);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				cheatNeedsEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>✔ [CHEAT]: Все потребности сброшены в 0!</color>");
		}

		public void BroadcastTimeScale(float scale)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedTimeScale = scale;
			using (GameEventWriter gameEventWriter = cheatTimeEvent.Writer())
			{
				gameEventWriter.Write((byte)0);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(scale);
				cheatTimeEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Скорость времени -> " + scale + "</color>");
		}

		public void BroadcastDay(int day)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedDay = day;
			using (GameEventWriter gameEventWriter = cheatTimeEvent.Writer())
			{
				gameEventWriter.Write((byte)1);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(day);
				cheatTimeEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: День недели -> " + day + "</color>");
		}

		public void BroadcastSkip(string skipType)
		{
			if (isNetworkApplying)
			{
				return;
			}
			if (skipType == "POST_ORDER")
			{
				if (suppressSkipPostOrder || (Time.time - lastPostOrderSkipTime < 2f))
				{
					return;
				}
				lastPostOrderSkipTime = Time.time;
			}
			using (GameEventWriter gameEventWriter = cheatSkipEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(skipType ?? "");
				cheatSkipEvent.Send(gameEventWriter, 0uL, safe: true);
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Пропуск / Скип -> " + skipType + "</color>");
			}
			suppressSkipPostOrder = true;
			try
			{
				ApplySkipLocal(skipType);
			}
			finally
			{
				suppressSkipPostOrder = false;
			}
		}

		public void BroadcastFuel(string vehicleName, float amount)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using (GameEventWriter gameEventWriter = cheatVehicleEvent.Writer())
			{
				gameEventWriter.Write((byte)0);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(vehicleName ?? "");
				gameEventWriter.Write(amount);
				cheatVehicleEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Топливо " + vehicleName + " -> " + amount.ToString("F1") + " л</color>");
		}

		public void BroadcastSatsumaFire(bool active)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedSatsumaFire = active;
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox != null && betterCheatBox.satsumaFire != null)
			{
				betterCheatBox.satsumaFire.SetActive(active);
			}
			using (GameEventWriter gameEventWriter = cheatVehicleEvent.Writer())
			{
				gameEventWriter.Write((byte)1);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(active);
				cheatVehicleEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Пожар двигателя -> " + (active ? "ЗАЖЖЕН" : "ПОТУШЕН") + "</color>");
		}

		public void BroadcastTire(int cornerIndex, int tireType)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using (GameEventWriter gameEventWriter = cheatVehicleEvent.Writer())
			{
				gameEventWriter.Write((byte)2);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(cornerIndex);
				gameEventWriter.Write(tireType);
				cheatVehicleEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Шина колеса #" + cornerIndex + " -> тип " + tireType + "</color>");
		}

		public void BroadcastStraightenSuspension(int cornerIndex)
		{
			if (isNetworkApplying)
			{
				return;
			}
			using (GameEventWriter gameEventWriter = cheatVehicleEvent.Writer())
			{
				gameEventWriter.Write((byte)3);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(cornerIndex);
				cheatVehicleEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Выравнивание подвески #" + cornerIndex + "</color>");
		}

		public void BroadcastPhysicsSpeed(float speed)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedPhysicsSpeed = speed;
			using (GameEventWriter gameEventWriter = cheatVehicleEvent.Writer())
			{
				gameEventWriter.Write((byte)4);
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(speed);
				cheatVehicleEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Скорость физики -> " + speed + "x</color>");
		}

		public void BroadcastKey(string keyName, int value)
		{
			if (isNetworkApplying)
			{
				return;
			}
			string canonicalVar = ResolveKeyVarName(keyName);
			using (GameEventWriter gameEventWriter = cheatKeyEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(canonicalVar);
				gameEventWriter.Write(value);
				cheatKeyEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Ключ " + canonicalVar + " -> " + ((value > 0) ? "Взят" : "Заблокирован") + "</color>");
		}

		public void BroadcastKeyUnlock(string keyName)
		{
			ApplyKeyLocal(keyName, 1);
			BroadcastKey(keyName, 1);
		}

		public void UnlockAllKeys()
		{
			string[] array = new string[6] { "PlayerKeySatsuma", "PlayerKeyHayosiko", "PlayerKeyGifu", "PlayerKeyRuscko", "PlayerKeyFerndale", "PlayerKeyHome" };
			foreach (string keyName in array)
			{
				ApplyKeyLocal(keyName, 1);
				BroadcastKey(keyName, 1);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>✔ [CHEAT]: Все ключи разблокированы и синхронизированы!</color>");
		}

		public void BroadcastPolice(bool roadCops, bool homeCops)
		{
			if (isNetworkApplying)
			{
				return;
			}
			cachedRoadCops = roadCops;
			cachedHomeCops = homeCops;
			using (GameEventWriter gameEventWriter = cheatPoliceEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(roadCops);
				gameEventWriter.Write(homeCops);
				cheatPoliceEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Полиция (Дорога: " + roadCops + ", Дом: " + homeCops + ")</color>");
		}

		public void BroadcastPissStain(int stainIndex, float scaleX, float scaleY)
		{
			if (isNetworkApplying)
			{
				return;
			}
			ApplyPissStainLocal(stainIndex, scaleX, scaleY);
			using (GameEventWriter gameEventWriter = cheatHouseFloorEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(stainIndex);
				gameEventWriter.Write(scaleX);
				gameEventWriter.Write(scaleY);
				cheatHouseFloorEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Пятно пола #" + stainIndex + " -> " + scaleX.ToString("F1") + "x" + scaleY.ToString("F1") + "</color>");
		}

		public void BroadcastFullRepair(bool fixAll, bool tightenBolts, bool tuneEngine)
		{
			ApplyFullRepairLocal(fixAll, tightenBolts, tuneEngine);
			using (GameEventWriter gameEventWriter = cheatRepairEvent.Writer())
			{
				gameEventWriter.Write(WreckMPGlobals.UserID);
				gameEventWriter.Write(fixAll);
				gameEventWriter.Write(tightenBolts);
				gameEventWriter.Write(tuneEngine);
				cheatRepairEvent.Send(gameEventWriter, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>OUT [CHEAT]: Ремонт, сборка, затяжка болтов и настройка Сацумы</color>");
		}

		private void OnReceiveTeleport(GameEventReader reader)
		{
			byte b = reader.ReadByte();
			ulong senderSteamId = reader.ReadUInt64();
			string text = reader.ReadString();
			string text2 = reader.ReadString();
			int subIndex = reader.ReadInt32();
			Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Quaternion rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Телепортация от " + senderSteamId + " -> " + text2 + (subIndex >= 0 ? " #" + subIndex : "") + "</color>");
			isNetworkApplying = true;
			try
			{
				if (b != 1)
				{
					GameObject gameObject = null;
					if (subIndex >= 0)
					{
						gameObject = FindInBetterCheatBoxMulti(text2, subIndex);
					}
					if (gameObject == null)
					{
						gameObject = FindObjectByPath(text) ?? GameObject.Find(text2) ?? FindInBetterCheatBox(text2);
					}
					if (gameObject != null)
					{
						gameObject.SetActive(value: true);
						gameObject.transform.position = position;
						gameObject.transform.rotation = rotation;
						if (WreckMPGlobals.IsHost && senderSteamId != WreckMPGlobals.UserID)
						{
							TransferOrRelinquishOwnership(gameObject, senderSteamId);
						}
						ResetRigidbodyPhysicsLocal(gameObject);
						UpdateNetRigidbodyCache(gameObject, position, rotation);
					}
					else
					{
						ExtendedSyncDebugHUD.Log("<color=#ffaa00>WARN [CHEAT]: Объект для телепортации не найден: " + text + " (" + text2 + ")</color>");
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Ошибка в OnReceiveTeleport: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceiveSpawn(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			int spawnerIndex = reader.ReadInt32();
			string buttonName = reader.ReadString();
			string templateName = reader.ReadString();
			int num = reader.ReadInt32();
			Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			Quaternion rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			string batchId = reader.ReadString();
			if (string.IsNullOrEmpty(batchId))
			{
				batchId = "gen_" + (buttonName ?? "").ToLower().Replace(" ", "_") + "_" + (long)position.x + "_" + (long)position.z;
			}
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Спавн предмета " + buttonName + " x" + num + " от " + senderSteamId + "</color>");
			isNetworkApplying = true;
			try
			{
				GameObject gameObject = FindSpawnTemplate(buttonName, templateName);
				if (gameObject != null)
				{
					Transform p = GameObject.Find("PLAYER")?.transform;
					Vector3 fwd = p != null ? p.forward : Vector3.forward;
					Vector3 right = p != null ? p.right : Vector3.right;

					for (int i = 0; i < num; i++)
					{
						Vector3 spawnPos = position + fwd * (1.2f + (i / 3) * 0.6f) + right * ((i % 3 - 1) * 0.5f) + Vector3.up * 0.25f;
						GameObject obj = (GameObject)UnityEngine.Object.Instantiate(gameObject, spawnPos, rotation);
						obj.SetActive(value: true);
						string itemId = string.Format("bcb_{0}_{1}_{2}_{3}", senderSteamId, buttonName, batchId, i);
						CheatSpawnedItemSync.AttachToSpawned(obj, itemId);
						if (WreckMPGlobals.IsHost && senderSteamId != WreckMPGlobals.UserID)
						{
							TransferOrRelinquishOwnership(obj, senderSteamId);
						}
						ResetRigidbodyPhysicsLocal(obj);
						UpdateNetRigidbodyCache(obj, spawnPos, rotation);
					}
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [CHEAT]: Шаблон для спавна не найден: " + buttonName + " / " + templateName + "</color>");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Ошибка в OnReceiveSpawn: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceiveMoney(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			float num = reader.ReadSingle();
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Синхронизация баланса от " + senderSteamId + " -> " + num.ToString("N0") + " MK</color>");
			isNetworkApplying = true;
			try
			{
				ApplyMoneyLocal(num);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceiveNeeds(GameEventReader reader)
		{
			byte b = reader.ReadByte();
			ulong senderSteamId = reader.ReadUInt64();
			isNetworkApplying = true;
			try
			{
				BetterCheatBox betterCheatBox = GetBetterCheatBox();
				switch (b)
				{
				case 0:
				{
					bool flag = reader.ReadBoolean();
					if (betterCheatBox != null)
					{
						betterCheatBox.needs = flag;
					}
					cachedNeeds = flag;
					if (!flag)
					{
						ResetAllNeedsLocal();
					}
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Потребности от " + senderSteamId + " -> " + (flag ? "Включены" : "Отключены (Godmode)") + "</color>");
					break;
				}
				case 1:
				{
					float value = reader.ReadSingle();
					FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat("PlayerFatigue");
					if (fsmFloat != null)
					{
						fsmFloat.Value = value;
					}
					if (betterCheatBox != null && betterCheatBox.fatigue != null)
					{
						betterCheatBox.fatigue.Value = value;
					}
					cachedFatigue = value;
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Усталость от " + senderSteamId + " -> " + value + "</color>");
					break;
				}
				case 2:
					ResetAllNeedsLocal();
					ExtendedSyncDebugHUD.Log("<color=#00ffcc>✔ [CHEAT]: Все потребности обнулены партнером (" + senderSteamId + ")!</color>");
					break;
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void ResetAllNeedsLocal()
		{
			string[] array = new string[8] { "PlayerFatigue", "PlayerDirtiness", "PlayerDrunk", "PlayerHunger", "PlayerThirst", "PlayerUrine", "PlayerStress", "PlayerStressRate" };
			foreach (string text in array)
			{
				FsmFloat fsmFloat = FsmVariables.GlobalVariables.FindFsmFloat(text);
				if (fsmFloat != null)
				{
					fsmFloat.Value = 0f;
				}
				try
				{
					FsmFloat fsmFloat2 = PlayMakerGlobals.Instance?.Variables?.FindFsmFloat(text);
					if (fsmFloat2 != null) fsmFloat2.Value = 0f;
				}
				catch {}
			}
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || betterCheatBox.needVariables == null)
			{
				return;
			}
			for (int j = 0; j < betterCheatBox.needVariables.Length; j++)
			{
				if (betterCheatBox.needVariables[j] != null)
				{
					betterCheatBox.needVariables[j].Value = 0f;
				}
			}
		}

		private void OnReceiveTime(GameEventReader reader)
		{
			byte b = reader.ReadByte();
			ulong senderSteamId = reader.ReadUInt64();
			isNetworkApplying = true;
			try
			{
				BetterCheatBox betterCheatBox = GetBetterCheatBox();
				switch (b)
				{
				case 0:
				{
					float value2 = reader.ReadSingle();
					if (betterCheatBox != null && betterCheatBox.timeScale != null)
					{
						betterCheatBox.timeScale.Value = value2;
					}
					cachedTimeScale = value2;
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Скорость времени от " + senderSteamId + " -> " + value2 + "</color>");
					break;
				}
				case 1:
				{
					int value = reader.ReadInt32();
					FsmInt fsmInt = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
					if (fsmInt != null)
					{
						fsmInt.Value = value;
					}
					try
					{
						FsmInt fsmInt2 = PlayMakerGlobals.Instance?.Variables?.FindFsmInt("GlobalDay");
						if (fsmInt2 != null)
						{
							fsmInt2.Value = value;
						}
					}
					catch {}
					if (betterCheatBox != null && betterCheatBox.day != null)
					{
						betterCheatBox.day.Value = value;
					}
					cachedDay = value;
					if (WreckMPGlobals.IsHost)
					{
						try
						{
							GameObject sunObj = GameObject.Find("MAP/SUN/Pivot/SUN");
							if (sunObj != null)
							{
								PlayMakerFSM sunFsm = sunObj.GetComponent<PlayMakerFSM>();
								if (sunFsm != null)
								{
									sunFsm.SendEvent("WAKEUP");
									sunFsm.SendEvent("State 3");
								}
							}
						}
						catch {}
					}
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: День недели от " + senderSteamId + " -> " + value + "</color>");
					break;
				}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceiveSkip(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			string text = reader.ReadString();
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Пропуск таймера / Скип от " + senderSteamId + " -> " + text + "</color>");
			if (text == "POST_ORDER")
			{
				lastPostOrderSkipTime = Time.time;
			}
			isNetworkApplying = true;
			suppressSkipPostOrder = true;
			try
			{
				ApplySkipLocal(text);
			}
			finally
			{
				isNetworkApplying = false;
				suppressSkipPostOrder = false;
			}
		}

		private void ApplySkipLocal(string skipType)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			switch (skipType)
			{
			case "UNCLE":
			{
				FsmInt fsmInt = FsmVariables.GlobalVariables.FindFsmInt("UncleStage");
				if (fsmInt != null)
				{
					fsmInt.Value = 5;
				}
				try
				{
					FsmInt fsmInt2 = PlayMakerGlobals.Instance?.Variables?.FindFsmInt("UncleStage");
					if (fsmInt2 != null) fsmInt2.Value = 5;
				}
				catch {}
				if (betterCheatBox != null && betterCheatBox.uncleStage != null)
				{
					betterCheatBox.uncleStage.Value = 5;
				}
				cachedUncleStage = 5;
				Transform transform = ((betterCheatBox != null && betterCheatBox.hayosiko != null) ? betterCheatBox.hayosiko : GameObject.Find("HAYOSIKO(1500kg, 250)")?.transform);
				if (transform != null)
				{
					transform.position = new Vector3(21.7f, 0f, -48.85f);
					transform.localEulerAngles = new Vector3(0.2f, 90f, 0f);
					ResetRigidbodyPhysicsLocal(transform.gameObject);
					UpdateNetRigidbodyCache(transform.gameObject, transform.position, transform.rotation);
				}
				break;
			}
			case "KILJU":
			{
				if (betterCheatBox != null && betterCheatBox.kiljuTime != null)
				{
					betterCheatBox.kiljuTime.Value = 1000f;
				}
				cachedKiljuTime = 1000f;
				GameObject gameObject = GameObject.Find("bucket(itemx)") ?? GameObject.Find("bucket");
				if (gameObject != null)
				{
					PlayMakerFSM component = gameObject.GetComponent<PlayMakerFSM>();
					if (component != null && component.FsmVariables != null)
					{
						FsmFloat fsmFloat = component.FsmVariables.FindFsmFloat("Time");
						if (fsmFloat != null)
						{
							fsmFloat.Value = 1000f;
						}
					}
				}
				break;
			}
			case "POST_ORDER":
			{
				suppressSkipPostOrder = true;
				lastPostOrderSkipTime = Time.time;
				try
				{
					PlayMakerFSM playMakerFSM4 = ((betterCheatBox != null && betterCheatBox.orderFsm != null) ? betterCheatBox.orderFsm : GameObject.Find("Sheets/OrderList/Timer")?.GetComponent<PlayMakerFSM>());
					if (playMakerFSM4 != null)
					{
						playMakerFSM4.SendEvent("FINISHED");
					}
					try { NetPartsDeliverySync.Instance?.BroadcastDeliveryReady(); } catch {}
				}
				finally
				{
					suppressSkipPostOrder = false;
				}
				break;
			}
			case "REPAIR_WORK":
			{
				PlayMakerFSM playMakerFSM3 = ((betterCheatBox != null && betterCheatBox.workOrderFsm != null) ? betterCheatBox.workOrderFsm : GameObject.Find("REPAIRSHOP/Order")?.GetComponent<PlayMakerFSM>());
				if (playMakerFSM3 != null)
				{
					FsmFloat fsmFloat2 = playMakerFSM3.FsmVariables.FindFsmFloat("_OrderTime");
					if (fsmFloat2 != null)
					{
						fsmFloat2.Value = 0f;
					}
					playMakerFSM3.SendEvent("WAIT");
				}
				if (betterCheatBox != null)
				{
					if (betterCheatBox.distanceCheck != null && betterCheatBox.distanceCheck.float2 != null) betterCheatBox.distanceCheck.float2.Value = -1f;
					if (betterCheatBox.distanceCheck2 != null && betterCheatBox.distanceCheck2.float2 != null) betterCheatBox.distanceCheck2.float2.Value = -1f;
				}
				cachedFleetariOrderTime = 0f;
				break;
			}
			case "RESTOCK":
			{
				PlayMakerFSM playMakerFSM2 = ((betterCheatBox != null && betterCheatBox.inventoryFsm != null) ? betterCheatBox.inventoryFsm : GameObject.Find("STORE/Inventory")?.GetComponent<PlayMakerFSM>());
				if (playMakerFSM2 != null)
				{
					playMakerFSM2.SendEvent("DAY");
					playMakerFSM2.SendEvent("PROCEED");
				}
				break;
			}
			case "WEATHER":
			{
				PlayMakerFSM playMakerFSM = ((betterCheatBox != null && betterCheatBox.cloudFsm != null) ? betterCheatBox.cloudFsm : GameObject.Find("MAP/CloudSystem/Clouds")?.GetComponent<PlayMakerFSM>());
				if (playMakerFSM != null)
				{
					playMakerFSM.SendEvent("RANDOMIZE");
					playMakerFSM.SendEvent("Move clouds");
				}
				break;
			}
			}
		}

		private void OnReceiveVehicle(GameEventReader reader)
		{
			byte b = reader.ReadByte();
			ulong senderSteamId = reader.ReadUInt64();
			isNetworkApplying = true;
			try
			{
				BetterCheatBox betterCheatBox = GetBetterCheatBox();
				switch (b)
				{
				case 0:
				{
					string vehicleName = reader.ReadString();
					float amount = reader.ReadSingle();
					ApplySetFuelLocal(vehicleName, amount);
					break;
				}
				case 1:
				{
					bool flag = reader.ReadBoolean();
					if (betterCheatBox != null && betterCheatBox.satsumaFire != null)
					{
						betterCheatBox.satsumaFire.SetActive(flag);
					}
					else
					{
						GameObject gameObject = GameObject.Find("satsumaFire") ?? GameObject.Find("SATSUMA(580kg, 240hp)/CarSimulation/Fire");
						if (gameObject != null)
						{
							gameObject.SetActive(flag);
						}
					}
					cachedSatsumaFire = flag;
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Огонь двигателя от " + senderSteamId + " -> " + (flag ? "ВКЛ" : "ВЫКЛ") + "</color>");
					break;
				}
				case 2:
				{
					int cornerIndex2 = reader.ReadInt32();
					int tireType = reader.ReadInt32();
					ApplySetTireLocal(cornerIndex2, tireType);
					break;
				}
				case 3:
				{
					int cornerIndex = reader.ReadInt32();
					ApplyStraightenSuspensionLocal(cornerIndex);
					break;
				}
				case 4:
				{
					float num = (cachedPhysicsSpeed = (Time.timeScale = reader.ReadSingle()));
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Скорость физики от " + senderSteamId + " -> " + num + "x</color>");
					break;
				}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void ApplySetFuelLocal(string vehicleName, float amount)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || betterCheatBox.carDebugEntries == null)
			{
				return;
			}
			for (int i = 0; i < betterCheatBox.carDebugEntries.Length; i++)
			{
				CarDebugEntry carDebugEntry = betterCheatBox.carDebugEntries[i];
				if (carDebugEntry != null && string.Equals(carDebugEntry.buttonName, vehicleName, StringComparison.OrdinalIgnoreCase))
				{
					if (carDebugEntry.fuelLevel != null)
					{
						carDebugEntry.fuelLevel.Value = amount;
					}
					if (i < cachedFuel.Length)
					{
						cachedFuel[i] = amount;
					}
					ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Заправка " + vehicleName + " -> " + amount.ToString("F1") + " л</color>");
					break;
				}
			}
		}

		private void ApplySetTireLocal(int cornerIndex, int tireType)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || !(betterCheatBox.suspensionDamageDisabler != null))
			{
				return;
			}
			SuspensionDamageDisabler suspensionDamageDisabler = betterCheatBox.suspensionDamageDisabler;
			if (suspensionDamageDisabler.tireTypes != null && cornerIndex < suspensionDamageDisabler.tireTypes.Length && suspensionDamageDisabler.tireTypes[cornerIndex] != null)
			{
				suspensionDamageDisabler.tireTypes[cornerIndex].Value = tireType;
				if (suspensionDamageDisabler.tires != null && cornerIndex < suspensionDamageDisabler.tires.Length && suspensionDamageDisabler.tires[cornerIndex] != null)
				{
					suspensionDamageDisabler.tires[cornerIndex].SendEvent("CHANGETIRE");
				}
				if (cornerIndex < cachedTires.Length)
				{
					cachedTires[cornerIndex] = tireType;
				}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Смена шины #" + cornerIndex + " -> " + tireType + "</color>");
			}
		}

		private void ApplyStraightenSuspensionLocal(int cornerIndex)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || !(betterCheatBox.suspensionDamageDisabler != null))
			{
				return;
			}
			SuspensionDamageDisabler suspensionDamageDisabler = betterCheatBox.suspensionDamageDisabler;
			if (suspensionDamageDisabler.corners != null && cornerIndex < suspensionDamageDisabler.corners.Length && suspensionDamageDisabler.corners[cornerIndex]?.Value != null)
			{
				suspensionDamageDisabler.corners[cornerIndex].Value.transform.localEulerAngles = Vector3.zero;
				if (cornerIndex < cachedCornerAngles.Length)
				{
					cachedCornerAngles[cornerIndex] = Vector3.zero;
				}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Подвеска колеса #" + cornerIndex + " выровнена!</color>");
			}
		}

		private void OnReceiveKey(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			string text = reader.ReadString();
			int num = reader.ReadInt32();
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Ключ от " + senderSteamId + " -> " + text + " -> " + ((num > 0) ? "Взят" : "Заблокирован") + "</color>");
			isNetworkApplying = true;
			try
			{
				ApplyKeyLocal(text, num);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void ApplyKeyLocal(string keyName, int value)
		{
			string canonicalVar = ResolveKeyVarName(keyName);
			FsmInt fsmInt = FsmVariables.GlobalVariables.FindFsmInt(canonicalVar);
			if (fsmInt != null)
			{
				fsmInt.Value = value;
			}
			try
			{
				FsmInt fsmInt2 = PlayMakerGlobals.Instance?.Variables?.FindFsmInt(canonicalVar);
				if (fsmInt2 != null)
				{
					fsmInt2.Value = value;
				}
			}
			catch {}
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || betterCheatBox.playerKeys == null)
			{
				return;
			}
			for (int i = 0; i < betterCheatBox.playerKeys.Length; i++)
			{
				PlayerKey playerKey = betterCheatBox.playerKeys[i];
				if (playerKey != null)
				{
					string pkCanonical = ResolveKeyVarName(playerKey.buttonName);
					if (string.Equals(pkCanonical, canonicalVar, StringComparison.OrdinalIgnoreCase) || (playerKey.keyValue != null && string.Equals(playerKey.keyValue.Name, canonicalVar, StringComparison.OrdinalIgnoreCase)))
					{
						if (playerKey.keyValue != null)
						{
							playerKey.keyValue.Value = value;
						}
						if (i < cachedKeys.Length)
						{
							cachedKeys[i] = value;
						}
					}
				}
			}
		}

		private void OnReceivePolice(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			bool active = reader.ReadBoolean();
			bool active2 = reader.ReadBoolean();
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Полиция от " + senderSteamId + " -> Дорога: " + active + ", Дом: " + active2 + "</color>");
			isNetworkApplying = true;
			try
			{
				ApplyPoliceLocal(active, active2);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void ApplyPoliceLocal(bool roadCops, bool homeCops)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox != null)
			{
				if (betterCheatBox.copController != null)
				{
					betterCheatBox.copController.SetActive(roadCops);
				}
				if (betterCheatBox.copsAtHome != null)
				{
					betterCheatBox.copsAtHome.SetActive(homeCops);
				}
			}
			cachedRoadCops = roadCops;
			cachedHomeCops = homeCops;
		}

		private void OnReceiveHouseFloor(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			int stainIndex = reader.ReadInt32();
			float scaleX = reader.ReadSingle();
			float scaleY = reader.ReadSingle();
			isNetworkApplying = true;
			try
			{
				ApplyPissStainLocal(stainIndex, scaleX, scaleY);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void ApplyPissStainLocal(int stainIndex, float scaleX, float scaleY)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null || betterCheatBox.pissStains == null || stainIndex >= betterCheatBox.pissStains.Length)
			{
				return;
			}
			PissObject pissObject = betterCheatBox.pissStains[stainIndex];
			if (pissObject != null && pissObject.transform != null)
			{
				pissObject.transform.localScale = ((scaleX == 0f && scaleY == 0f) ? Vector3.zero : new Vector3(scaleX, scaleY, 1f));
				if (stainIndex < cachedStains.Length)
				{
					cachedStains[stainIndex] = pissObject.transform.localScale;
				}
			}
		}

		private void OnReceiveRepair(GameEventReader reader)
		{
			ulong senderSteamId = reader.ReadUInt64();
			bool fixAll = reader.ReadBoolean();
			bool tightenBolts = reader.ReadBoolean();
			bool tuneEngine = reader.ReadBoolean();
			ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [CHEAT]: Применение полного ремонта и затяжки Сацумы от " + senderSteamId + "</color>");
			isNetworkApplying = true;
			try
			{
				ApplyFullRepairLocal(fixAll, tightenBolts, tuneEngine);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		public void ApplyFullRepairLocal(bool fixAll, bool tightenBolts, bool tuneEngine)
		{
			GameObject gameObject = cachedSatsuma ?? GameObject.Find("SATSUMA(504kg, 330)") ?? GameObject.Find("SATSUMA(580kg, 240hp)") ?? GameObject.Find("SATSUMA(557kg, 248)") ?? GameObject.Find("SATSUMA");
			if (gameObject == null)
			{
				ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [CHEAT]: Сацума не найдена в сцене!</color>");
				return;
			}
			PlayMakerFSM[] componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>(includeInactive: true);
			PlayMakerFSM[] array = componentsInChildren;
			foreach (PlayMakerFSM playMakerFSM in array)
			{
				if (playMakerFSM == null || playMakerFSM.FsmVariables == null)
				{
					continue;
				}
				if (fixAll)
				{
					FsmFloat fsmFloat = playMakerFSM.FsmVariables.FindFsmFloat("Wear");
					if (fsmFloat != null)
					{
						fsmFloat.Value = 0f;
					}
					FsmBool fsmBool = playMakerFSM.FsmVariables.FindFsmBool("Damaged");
					if (fsmBool != null)
					{
						fsmBool.Value = false;
					}
					FsmBool fsmBool2 = playMakerFSM.FsmVariables.FindFsmBool("Broken");
					if (fsmBool2 != null)
					{
						fsmBool2.Value = false;
					}
					FsmBool fsmBool3 = playMakerFSM.FsmVariables.FindFsmBool("Detached");
					if (fsmBool3 != null)
					{
						fsmBool3.Value = false;
					}
					FsmFloat fsmTireHealth = playMakerFSM.FsmVariables.FindFsmFloat("TireHealth");
					if (fsmTireHealth != null)
					{
						fsmTireHealth.Value = 100f;
					}
				}
				if (tightenBolts)
				{
					FsmFloat fsmFloat2 = playMakerFSM.FsmVariables.FindFsmFloat("Tightness");
					if (fsmFloat2 != null)
					{
						fsmFloat2.Value = 8f;
					}
					FsmInt fsmInt = playMakerFSM.FsmVariables.FindFsmInt("Stage");
					if (fsmInt != null)
					{
						fsmInt.Value = 8;
					}
					FsmInt fsmInt2 = playMakerFSM.FsmVariables.FindFsmInt("Bolts");
					if (fsmInt2 != null)
					{
						fsmInt2.Value = 8;
					}
					FsmBool fsmBool4 = playMakerFSM.FsmVariables.FindFsmBool("Bolted");
					if (fsmBool4 != null)
					{
						fsmBool4.Value = true;
					}
					FsmBool fsmBool5 = playMakerFSM.FsmVariables.FindFsmBool("Installed");
					if (fsmBool5 != null)
					{
						fsmBool5.Value = true;
					}
					try
					{
						playMakerFSM.SendEvent("TIGHTEN");
					}
					catch
					{
					}
					try
					{
						playMakerFSM.SendEvent("ASSEMBLE");
					}
					catch
					{
					}
				}
			}
			if (tightenBolts)
			{
				try
				{
					FieldInfo fieldInfo = typeof(GameScene).Assembly.GetType("WreckMP.NetPartManager")?.GetField("bolts", BindingFlags.Static | BindingFlags.NonPublic);
					if (fieldInfo != null && fieldInfo.GetValue(null) is IEnumerable enumerable)
					{
						MethodInfo methodInfo = null;
						foreach (object item in enumerable)
						{
							if (item != null)
							{
								if (methodInfo == null)
								{
									methodInfo = item.GetType().GetMethod("SetTightness", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								}
								methodInfo?.Invoke(item, new object[1] { (byte)8 });
							}
						}
					}
				}
				catch
				{
				}
			}
			if (fixAll)
			{
				try
				{
					Transform transform = gameObject.transform.Find("FuelTank") ?? gameObject.transform.Find("CarSimulation/FuelTank");
					if (transform != null)
					{
						PlayMakerFSM component = transform.GetComponent<PlayMakerFSM>();
						if (component != null)
						{
							FsmFloat fsmFloat3 = component.FsmVariables.FindFsmFloat("FuelLevel");
							if (fsmFloat3 != null)
							{
								fsmFloat3.Value = 36f;
							}
						}
					}
				}
				catch
				{
				}
				array = componentsInChildren;
				foreach (PlayMakerFSM obj5 in array)
				{
					FsmFloat fsmFloat4 = obj5.FsmVariables.FindFsmFloat("OilLevel");
					if (fsmFloat4 != null)
					{
						fsmFloat4.Value = 100f;
					}
					FsmFloat fsmFloat5 = obj5.FsmVariables.FindFsmFloat("OilContamination");
					if (fsmFloat5 != null)
					{
						fsmFloat5.Value = 0f;
					}
					FsmFloat fsmFloat6 = obj5.FsmVariables.FindFsmFloat("Coolant");
					if (fsmFloat6 != null)
					{
						fsmFloat6.Value = 100f;
					}
					FsmFloat fsmFloat7 = obj5.FsmVariables.FindFsmFloat("Fluid");
					if (fsmFloat7 != null)
					{
						fsmFloat7.Value = 100f;
					}
				}
				GameObject gameObject2 = GameObject.Find("battery(itemx)") ?? GameObject.Find("battery");
				if (gameObject2 != null)
				{
					PlayMakerFSM component2 = gameObject2.GetComponent<PlayMakerFSM>();
					if (component2 != null)
					{
						FsmFloat fsmFloat8 = component2.FsmVariables.FindFsmFloat("Charge");
						if (fsmFloat8 != null)
						{
							fsmFloat8.Value = 100f;
						}
						FsmFloat fsmFloat9 = component2.FsmVariables.FindFsmFloat("Condition");
						if (fsmFloat9 != null)
						{
							fsmFloat9.Value = 100f;
						}
					}
				}
				BetterCheatBox bcb = GetBetterCheatBox();
				if (bcb != null && bcb.suspensionDamageDisabler != null)
				{
					var sdd = bcb.suspensionDamageDisabler;
					if (sdd.cornerTireHealth != null)
					{
						for (int i = 0; i < sdd.cornerTireHealth.Length; i++)
						{
							if (sdd.cornerTireHealth[i] != null) sdd.cornerTireHealth[i].Value = 100f;
						}
					}
					if (sdd.corners != null)
					{
						for (int i = 0; i < sdd.corners.Length; i++)
						{
							if (sdd.corners[i]?.Value != null)
							{
								sdd.corners[i].Value.transform.localEulerAngles = Vector3.zero;
							}
						}
					}
				}
			}
			if (tuneEngine)
			{
				array = componentsInChildren;
				foreach (PlayMakerFSM playMakerFSM2 in array)
				{
					FsmFloat fsmFloat10 = playMakerFSM2.FsmVariables.FindFsmFloat("Rot");
					if (fsmFloat10 != null && playMakerFSM2.gameObject.name.IndexOf("distributor", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						fsmFloat10.Value = 14.5f;
					}
					FsmFloat fsmFloat11 = playMakerFSM2.FsmVariables.FindFsmFloat("IdleMixture") ?? playMakerFSM2.FsmVariables.FindFsmFloat("Mixture");
					if (fsmFloat11 != null)
					{
						fsmFloat11.Value = 14.7f;
					}
					FsmFloat fsmFloat12 = playMakerFSM2.FsmVariables.FindFsmFloat("Tension");
					if (fsmFloat12 != null)
					{
						fsmFloat12.Value = 1f;
					}
					for (int v = 1; v <= 8; v++)
					{
						FsmFloat valve = playMakerFSM2.FsmVariables.FindFsmFloat("Valve" + v);
						if (valve != null) valve.Value = 7f;
					}
				}
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>✔ [CHEAT]: Сацума полностью отремонтирована, затянута и настроена!</color>");
		}

		public void TeleportVehicleToPlayer(string vehicleName)
		{
			Transform transform = GameObject.Find("PLAYER")?.transform;
			if (!(transform == null))
			{
				Vector3 vector = transform.position + transform.forward * 2.8f + Vector3.up * 0.25f;
				Quaternion quaternion = Quaternion.LookRotation(transform.forward);
				GameObject gameObject = null;
				string text = vehicleName.ToLower();
				gameObject = (text.Contains("satsuma") ? (cachedSatsuma ?? GameObject.Find("SATSUMA(504kg, 330)") ?? GameObject.Find("SATSUMA(580kg, 240hp)") ?? GameObject.Find("SATSUMA(557kg, 248)") ?? GameObject.Find("SATSUMA")) : ((!text.Contains("hayo") && !text.Contains("van")) ? ((!text.Contains("jonnez") && !text.Contains("moped")) ? ((!text.Contains("gifu") && !text.Contains("truck")) ? ((!text.Contains("fern") && !text.Contains("muscle")) ? (text.Contains("ruscko") ? (GameObject.Find("RCO_RUSCKO12(270)") ?? GameObject.Find("RCO_RUSCKO12")) : ((text.Contains("kekmet") || text.Contains("tractor")) ? (GameObject.Find("KEKMET(350-400psi)") ?? GameObject.Find("KEKMET")) : (text.Contains("boat") ? GameObject.Find("BOAT") : ((!text.Contains("combine")) ? GameObject.Find(vehicleName) : (GameObject.Find("COMBINE(350-400psi)") ?? GameObject.Find("COMBINE")))))) : (GameObject.Find("FERNDALE(1630kg)") ?? GameObject.Find("FERNDALE"))) : (GameObject.Find("GIFU(750/450psi)") ?? GameObject.Find("GIFU"))) : (GameObject.Find("JONNEZ ES(Clone)") ?? GameObject.Find("JONNEZ ES"))) : (GameObject.Find("HAYOSIKO(1500kg, 250)") ?? GameObject.Find("HAYOSIKO"))));
				if (gameObject != null)
				{
					gameObject.SetActive(value: true);
					gameObject.transform.position = vector;
					gameObject.transform.rotation = quaternion;
					BroadcastTeleportObject(gameObject, vector, quaternion, vehicleName);
				}
				else
				{
					ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [CHEAT]: Транспорт не найден: " + vehicleName + "</color>");
				}
			}
		}

		public static string GetGameObjectPath(GameObject go)
		{
			if (go == null)
			{
				return "";
			}
			string text = go.name;
			Transform parent = go.transform.parent;
			while (parent != null)
			{
				text = parent.name + "/" + text;
				parent = parent.parent;
			}
			return text;
		}

		public static GameObject FindObjectByPath(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return null;
			}
			GameObject gameObject = GameObject.Find(path);
			if (gameObject != null)
			{
				return gameObject;
			}
			string[] array = path.Split('/');
			if (array.Length != 0)
			{
				GameObject gameObject2 = GameObject.Find(array[0]);
				if (gameObject2 != null)
				{
					Transform transform = gameObject2.transform;
					for (int i = 1; i < array.Length; i++)
					{
						Transform transform2 = transform.Find(array[i]);
						if (transform2 == null)
						{
							break;
						}
						transform = transform2;
					}
					if (transform != null)
					{
						return transform.gameObject;
					}
				}
			}
			return GameObject.Find(array[array.Length - 1]);
		}

		public GameObject FindInBetterCheatBox(string friendlyName)
		{
			if (string.IsNullOrEmpty(friendlyName))
			{
				return null;
			}
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox == null)
			{
				return null;
			}
			if (betterCheatBox.tpItemsButtons != null)
			{
				for (int i = 0; i < betterCheatBox.tpItemsButtons.Length; i++)
				{
					TPItemsButton tPItemsButton = betterCheatBox.tpItemsButtons[i];
					if (tPItemsButton == null || tPItemsButton.items == null)
					{
						continue;
					}
					for (int j = 0; j < tPItemsButton.items.Length; j++)
					{
						TPMeToObject tPMeToObject = tPItemsButton.items[j];
						if (tPMeToObject != null && string.Equals(tPMeToObject.buttonName, friendlyName, StringComparison.OrdinalIgnoreCase))
						{
							if (tPMeToObject.transform != null)
							{
								return tPMeToObject.transform.gameObject;
							}
							if (tPMeToObject.transforms != null && tPMeToObject.transforms.Length != 0 && tPMeToObject.transforms[0] != null)
							{
								return tPMeToObject.transforms[0].gameObject;
							}
						}
					}
				}
			}
			if (betterCheatBox.tpMeToObjects != null)
			{
				for (int k = 0; k < betterCheatBox.tpMeToObjects.Length; k++)
				{
					TPMeToObject tPMeToObject2 = betterCheatBox.tpMeToObjects[k];
					if (tPMeToObject2 != null && string.Equals(tPMeToObject2.buttonName, friendlyName, StringComparison.OrdinalIgnoreCase) && tPMeToObject2.transform != null)
					{
						return tPMeToObject2.transform.gameObject;
					}
				}
			}
			return null;
		}

		public GameObject FindSpawnTemplate(string buttonName, string templateName)
		{
			BetterCheatBox betterCheatBox = GetBetterCheatBox();
			if (betterCheatBox != null && betterCheatBox.spawnObjectList != null && betterCheatBox.spawnObjectList.items != null)
			{
				TPMeToObject[] items = betterCheatBox.spawnObjectList.items;
				foreach (TPMeToObject tPMeToObject in items)
				{
					if (tPMeToObject != null && ((!string.IsNullOrEmpty(buttonName) && string.Equals(tPMeToObject.buttonName, buttonName, StringComparison.OrdinalIgnoreCase)) || (!string.IsNullOrEmpty(templateName) && tPMeToObject.transform != null && string.Equals(tPMeToObject.transform.name, templateName, StringComparison.OrdinalIgnoreCase))) && tPMeToObject.transform != null)
					{
						return tPMeToObject.transform.gameObject;
					}
				}
			}
			string[] spawnerFsmHolders = new string[] { "CreateItems", "Spawner/CreateItems", "CreateSpraycans", "Spawner/CreateSpraycans", "CreateMooseMeat", "Spawner/CreateMooseMeat" };
			foreach (var holderName in spawnerFsmHolders)
			{
				GameObject holder = GameObject.Find(holderName);
				if (holder != null)
				{
					PlayMakerFSM[] fsms = holder.GetComponents<PlayMakerFSM>();
					foreach (var fsm in fsms)
					{
						if (string.Equals(fsm.FsmName, buttonName, StringComparison.OrdinalIgnoreCase) || string.Equals(fsm.FsmName, templateName, StringComparison.OrdinalIgnoreCase))
						{
							var prefabVar = fsm.FsmVariables.FindFsmGameObject("CreatePrefab") ?? fsm.FsmVariables.FindFsmGameObject("New");
							if (prefabVar != null && prefabVar.Value != null) return prefabVar.Value;
						}
					}
				}
			}
			if (!string.IsNullOrEmpty(templateName))
			{
				GameObject direct = GameObject.Find(templateName);
				if (direct != null) return direct;
			}
			if (!string.IsNullOrEmpty(buttonName))
			{
				GameObject direct = GameObject.Find(buttonName);
				if (direct != null) return direct;
			}
			return null;
		}

		private static readonly FieldInfo orbOwnerField = typeof(OwnedRigidbody).GetField("owner", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly PropertyInfo orbOwnerProp = typeof(OwnedRigidbody).GetProperty("OwnerID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		private static readonly MethodInfo orbSetOwnerMethod = (orbOwnerProp != null) ? orbOwnerProp.GetSetMethod(true) : null;
		private static readonly FieldInfo orbCachedPosField = typeof(OwnedRigidbody).GetField("cachedPosition", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly FieldInfo orbCachedRotField = typeof(OwnedRigidbody).GetField("cachedEulerAngles", BindingFlags.Instance | BindingFlags.NonPublic);
		private static readonly MethodInfo nrmRequestOwnershipWithOwnerMethod = typeof(NetRigidbodyManager).GetMethod("RequestOwnership", BindingFlags.Static | BindingFlags.NonPublic, null, new Type[] { typeof(OwnedRigidbody), typeof(ulong) }, null);
		private static readonly FieldInfo nrmOwnedRigidbodiesField = typeof(NetRigidbodyManager).GetField("ownedRigidbodies", BindingFlags.Static | BindingFlags.NonPublic);

		public static void SetOwnedRigidbodyOwner(OwnedRigidbody orb, ulong newOwnerId)
		{
			if (orb == null) return;
			try
			{
				if (nrmRequestOwnershipWithOwnerMethod != null)
				{
					nrmRequestOwnershipWithOwnerMethod.Invoke(null, new object[] { orb, newOwnerId });
					return;
				}
			}
			catch {}

			try
			{
				if (orbSetOwnerMethod != null)
				{
					orbSetOwnerMethod.Invoke(orb, new object[] { newOwnerId });
				}
				else if (orbOwnerField != null)
				{
					orbOwnerField.SetValue(orb, newOwnerId);
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Error setting OwnedRigidbody owner: " + ex.Message);
			}
		}

		public static void RelinquishSingleRigidbody(Rigidbody rb, ulong targetOwnerId)
		{
			if (rb == null) return;
			try
			{
				int hash = NetRigidbodyManager.GetRigidbodyHash(rb);
				OwnedRigidbody orb = null;
				if (hash != 0)
				{
					orb = NetRigidbodyManager.GetOwnedRigidbody(hash);
				}
				if (orb == null && nrmOwnedRigidbodiesField != null)
				{
					List<OwnedRigidbody> list = nrmOwnedRigidbodiesField.GetValue(null) as List<OwnedRigidbody>;
					if (list != null)
					{
						for (int i = 0; i < list.Count; i++)
						{
							if (list[i] != null && list[i].Rigidbody == rb)
							{
								orb = list[i];
								break;
							}
						}
					}
				}
				if (orb != null)
				{
					SetOwnedRigidbodyOwner(orb, targetOwnerId);
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Error relinquishing Rigidbody: " + ex.Message);
			}
		}

		public static void TransferOrRelinquishOwnership(GameObject go, ulong newOwnerId)
		{
			if (go == null) return;
			try
			{
				Rigidbody component = go.GetComponent<Rigidbody>();
				if (component != null)
				{
					RelinquishSingleRigidbody(component, newOwnerId);
				}
				Rigidbody[] componentsInChildren = go.GetComponentsInChildren<Rigidbody>(true);
				if (componentsInChildren != null)
				{
					for (int i = 0; i < componentsInChildren.Length; i++)
					{
						if (componentsInChildren[i] != null && componentsInChildren[i] != component)
						{
							RelinquishSingleRigidbody(componentsInChildren[i], newOwnerId);
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Error in TransferOrRelinquishOwnership: " + ex.Message);
			}
		}

		public static void UpdateNetRigidbodyCache(GameObject go, Vector3 pos, Quaternion rot)
		{
			if (go == null) return;
			try
			{
				Rigidbody[] rbs = go.GetComponentsInChildren<Rigidbody>(true);
				if (rbs == null || rbs.Length == 0)
				{
					Rigidbody single = go.GetComponent<Rigidbody>();
					if (single != null) rbs = new Rigidbody[] { single };
				}
				if (rbs == null) return;

				for (int i = 0; i < rbs.Length; i++)
				{
					Rigidbody rb = rbs[i];
					if (rb == null) continue;
					try
					{
						int hash = NetRigidbodyManager.GetRigidbodyHash(rb);
						OwnedRigidbody orb = (hash != 0) ? NetRigidbodyManager.GetOwnedRigidbody(hash) : null;
						if (orb == null && nrmOwnedRigidbodiesField != null)
						{
							List<OwnedRigidbody> list = nrmOwnedRigidbodiesField.GetValue(null) as List<OwnedRigidbody>;
							if (list != null)
							{
								for (int j = 0; j < list.Count; j++)
								{
									if (list[j] != null && list[j].Rigidbody == rb)
									{
										orb = list[j];
										break;
									}
								}
							}
						}
						if (orb != null)
						{
							if (orbCachedPosField != null)
							{
								orbCachedPosField.SetValue(orb, rb.transform.position);
							}
							if (orbCachedRotField != null)
							{
								orbCachedRotField.SetValue(orb, rb.transform.eulerAngles);
							}
						}
					}
					catch {}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[BCB Sync] Error in UpdateNetRigidbodyCache: " + ex.Message);
			}
		}

		public static void ResetRigidbodyPhysicsAndClaim(GameObject go)
		{
			if (go == null) return;
			Rigidbody component = go.GetComponent<Rigidbody>();
			if (component != null)
			{
				component.velocity = Vector3.zero;
				component.angularVelocity = Vector3.zero;
				try { NetRigidbodyManager.RequestOwnership(component); } catch {}
			}
			Rigidbody[] componentsInChildren = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				if (componentsInChildren[i] != null)
				{
					componentsInChildren[i].velocity = Vector3.zero;
					componentsInChildren[i].angularVelocity = Vector3.zero;
					try { NetRigidbodyManager.RequestOwnership(componentsInChildren[i]); } catch {}
				}
			}
			UpdateNetRigidbodyCache(go, go.transform.position, go.transform.rotation);
		}

		public static void ResetRigidbodyPhysicsLocal(GameObject go)
		{
			if (go == null) return;
			Rigidbody component = go.GetComponent<Rigidbody>();
			if (component != null)
			{
				component.velocity = Vector3.zero;
				component.angularVelocity = Vector3.zero;
			}
			Rigidbody[] componentsInChildren = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
			for (int i = 0; i < componentsInChildren.Length; i++)
			{
				if (componentsInChildren[i] != null)
				{
					componentsInChildren[i].velocity = Vector3.zero;
					componentsInChildren[i].angularVelocity = Vector3.zero;
				}
			}
			UpdateNetRigidbodyCache(go, go.transform.position, go.transform.rotation);
		}

		public static void ResetRigidbodyPhysics(GameObject go)
		{
			ResetRigidbodyPhysicsAndClaim(go);
		}

		public static bool ReviveAndTeleportSatsuma(Vector3 targetPos, Quaternion targetRot)
		{
			GameObject satsuma = null;
			GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
			for (int i = 0; i < all.Length; i++)
			{
				if (all[i] != null && (all[i].name.StartsWith("SATSUMA(504kg, 330)") || all[i].name.StartsWith("SATSUMA(580kg, 240hp)")))
				{
					satsuma = all[i];
					break;
				}
			}

			if (satsuma == null) return false;
			cachedSatsuma = satsuma;

			// 1. Принудительное включение объекта и всех родителей
			Transform curr = satsuma.transform;
			while (curr != null)
			{
				curr.gameObject.SetActive(true);
				curr = curr.parent;
			}
			satsuma.transform.parent = null;
			satsuma.SetActive(true);

			// 2. Включение всех рендереров и коллайдеров кузова
			foreach (var r in satsuma.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
			foreach (var c in satsuma.GetComponentsInChildren<Collider>(true)) c.enabled = true;

			// 3. Сброс зависшей физики и телепортация
			Rigidbody rb = satsuma.GetComponent<Rigidbody>();
			if (rb != null)
			{
				rb.isKinematic = false;
				rb.velocity = Vector3.zero;
				rb.angularVelocity = Vector3.zero;
				rb.position = targetPos;
				rb.rotation = targetRot;
			}
			satsuma.transform.position = targetPos;
			satsuma.transform.rotation = targetRot;

			// 4. Обновление сетевого реестра физики WreckMP
			ResetRigidbodyPhysicsLocal(satsuma);
			UpdateNetRigidbodyCache(satsuma, targetPos, targetRot);

			return true;
		}
	}
	public static class BetterCheatBoxPatches
	{
		public static bool TPToPlayer_Prefix(BetterCheatBox __instance, TPMeToObject tpMeToObject)
		{
			if (tpMeToObject == null)
			{
				return false;
			}
			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
			if (tpMeToObject.transforms != null)
			{
				if (GUILayout.Button(text, __instance.buttonWidth))
				{
					Transform p = __instance.player;
					Vector3 fwd = p != null ? p.forward : Vector3.forward;
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
			else if (tpMeToObject.transform == null)
			{
				if (tpMeToObject.buttonName != null && (tpMeToObject.buttonName.IndexOf("satsuma", StringComparison.OrdinalIgnoreCase) >= 0 || tpMeToObject.buttonName.IndexOf("сацума", StringComparison.OrdinalIgnoreCase) >= 0))
				{
					if (BetterCheatBoxSyncManager.cachedSatsuma == null)
					{
						BetterCheatBoxSyncManager.cachedSatsuma = GameObject.Find("SATSUMA(504kg, 330)") ?? GameObject.Find("SATSUMA(580kg, 240hp)");
					}
					if (BetterCheatBoxSyncManager.cachedSatsuma != null)
					{
						tpMeToObject.transform = BetterCheatBoxSyncManager.cachedSatsuma.transform;
					}
				}
				if (tpMeToObject.transform == null)
				{
					GUILayout.Button($"<size={num}><color=grey>{tpMeToObject.buttonName}</color></size>", __instance.buttonWidth);
				}
			}
			if (tpMeToObject.transform != null && GUILayout.Button(text, __instance.buttonWidth))
			{
				Transform p = __instance.player;
				Vector3 fwd = p != null ? p.forward : Vector3.forward;
				GameObject go = tpMeToObject.transform.gameObject;
				Vector3 targetPos;
				Quaternion targetRot;
				if (BetterCheatBoxSyncManager.IsVehicleName(tpMeToObject.buttonName) || BetterCheatBoxSyncManager.IsVehicleName(go.name))
				{
					targetPos = p.position + fwd * 3.5f + Vector3.up * 0.4f;
					targetRot = Quaternion.LookRotation(fwd);
					if (tpMeToObject.buttonName != null && tpMeToObject.buttonName.IndexOf("satsuma", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						BetterCheatBoxSyncManager.ReviveAndTeleportSatsuma(targetPos, targetRot);
						BetterCheatBoxSyncManager.Instance?.BroadcastTeleportObject(go, targetPos, targetRot, tpMeToObject.buttonName, -1);
						return false;
					}
				}
				else
				{
					targetPos = p.position + fwd * 1.5f + Vector3.up * 0.2f;
					targetRot = tpMeToObject.transform.rotation;
				}
				go.SetActive(value: true);
				tpMeToObject.transform.position = targetPos;
				tpMeToObject.transform.rotation = targetRot;
				BetterCheatBoxSyncManager.ResetRigidbodyPhysicsAndClaim(go);
				BetterCheatBoxSyncManager.Instance?.BroadcastTeleportObject(go, targetPos, targetRot, tpMeToObject.buttonName, -1);
			}
			return false;
		}

		public static bool SpawnAtPlayer_Prefix(BetterCheatBox __instance, TPMeToObject tpMeToObject)
		{
			if (tpMeToObject == null)
			{
				return false;
			}
			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
			if (tpMeToObject.transforms != null)
			{
				if (!GUILayout.Button(text, __instance.buttonWidth))
				{
					return false;
				}
				Transform p = __instance.player;
				Vector3 fwd = p != null ? p.forward : Vector3.forward;
				Vector3 right = p != null ? p.right : Vector3.right;
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
				Transform p = __instance.player;
				Vector3 fwd = p != null ? p.forward : Vector3.forward;
				Vector3 right = p != null ? p.right : Vector3.right;
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
			if (tpMeToObject == null)
			{
				return false;
			}
			float num = __instance.guiBox.width / 40f;
			string text = $"<size={num}><b>{tpMeToObject.buttonName}</b></size>";
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

		public static bool PlayMakerFSM_SendEvent_Prefix(PlayMakerFSM __instance, string eventName)
		{
			if (__instance == null || string.IsNullOrEmpty(eventName)) return true;
			if (BetterCheatBoxSyncManager.Instance == null || 
			    BetterCheatBoxSyncManager.Instance.isNetworkApplying || 
			    BetterCheatBoxSyncManager.Instance.suppressSkipPostOrder)
				return true;

			BetterCheatBox bcb = BetterCheatBoxSyncManager.cachedBcbInstance;
			if (bcb == null) return true;

			if (__instance != bcb.cloudFsm && __instance != bcb.inventoryFsm && __instance != bcb.orderFsm)
			{
				return true;
			}

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
				BetterCheatBoxSyncManager.Instance.BroadcastSkip("POST_ORDER");
			}
			return true;
		}
	}
	public class InGameDashboardGUI : MonoBehaviour
	{
		public static InGameDashboardGUI Instance;

		public bool isVisible;

		private Rect windowRect = new Rect(40f, 60f, 580f, 500f);

		private int selectedTab;

		private readonly string[] tabs = new string[6] { "Статус P2P", "Jonnez Пассажир", "Сацума и Гараж", "Сюжет и Экономика", "Почта и Детали", "Better Cheat Box" };

		private void Awake()
		{
			Instance = this;
		}

		private void Update()
		{
			if (Input.GetKeyDown(KeyCode.F10))
			{
				isVisible = !isVisible;
			}
		}

		private void OnGUI()
		{
			if (isVisible)
			{
				GUI.backgroundColor = new Color(0.06f, 0.08f, 0.14f, 0.98f);
				GUI.color = Color.white;
				windowRect = GUI.Window(99912, windowRect, DrawDashboardWindow, "<color=#00ffcc><b>★ WRECKMP EXTENDED SYNC DASHBOARD [F10] ★</b></color>");
			}
		}

		private void DrawDashboardWindow(int windowID)
		{
			GUI.DragWindow(new Rect(0f, 0f, 580f, 25f));
			GUILayout.Space(5f);
			selectedTab = GUILayout.Toolbar(selectedTab, tabs, GUILayout.Height(30f));
			GUILayout.Space(10f);
			if (selectedTab == 0)
			{
				GUILayout.Label("<color=#ffdd00><b>СЕТЕВОЙ СТАТУС:</b></color> <color=#00ff00>WreckMP Честный P2P (100% Авто-синхронизация)</color>");
				GUILayout.Label("Текущая сцена: <b>" + Application.loadedLevelName + "</b>");
				GUILayout.Label("FPS: <b>" + (1f / Mathf.Max(0.0001f, Time.deltaTime)).ToString("F0") + "</b>");
				GUILayout.Space(6f);
				GUILayout.Label("<color=#aaff00>ℹ Меню F10 не требуется для игры: все события мира синхронизируются автоматически в фоне!</color>");
				GUILayout.Space(10f);
				GUILayout.Label("<color=#00ffff><b>ПОДСИСТЕМЫ СИНХРОНИЗАЦИИ:</b></color>");
				GUILayout.Label("✔ Jonnez ES Co-op: <color=#00ff00>АКТИВЕН (Клавиша U для посадки, Pitch/Roll Lock фикс)</color>");
				GUILayout.Label("✔ Капот Сацумы и бак: <color=#00ff00>АВТОМАТИЧЕСКИЙ</color>");
				GUILayout.Label("✔ Чемодан Йоуко (2,000,000 MK): <color=#00ff00>АВТОМАТИЧЕСКИЙ</color>");
				GUILayout.Label("✔ Автомат Паятсо и Килью: <color=#00ff00>АВТОМАТИЧЕСКИЙ</color>");
				GUILayout.Label("✔ Каталог, Почта, Теймо и Посылки: <color=#00ff00>100% АВТОМАТИЧЕСКИЙ (SafeFsmWatcher)</color>");
				GUILayout.Label("✔ Better Cheat Box Network Sync: <color=#00ff00>100% ПОЛНАЯ СИНХРОНИЗАЦИЯ (Harmony + State Observer)</color>");
				GUILayout.Label("✔ Заспавненные предметы (Ghost Item Sync): <color=#00ff00>АКТИВЕН (CheatSpawnedItemSync)</color>");
				GUILayout.Label("✔ Предметы в руках напарника (Universal Hand Sync): <color=#00ff00>АКТИВЕН (order_envelope, чеки, предметы)</color>");
				GUILayout.Label("✔ Телефон и вилка в доме (Telephone Sync): <color=#00ff00>АКТИВЕН (Receiver + Plug)</color>");
				GUILayout.Label("✔ Мочеиспускание напарника (Pee Stream Sync): <color=#00ff00>АКТИВЕН (Клавиша P)</color>");
				GUILayout.Label("✔ Карманный фонарик (Flashlight Sync): <color=#00ff00>АКТИВЕН (Spotlight)</color>");
				GUILayout.Label("✔ Защита от эхо-зацикливания (Anti-Echo): <color=#00ff00>АКТИВНА ВО ВСЕХ МОДУЛЯХ</color>");
				GUILayout.Label("✔ Автоматический сброс при рестарте: <color=#00ff00>АКТИВЕН</color>");
			}
			else if (selectedTab == 1)
			{
				JonnezPassengerSystem instance = JonnezPassengerSystem.Instance;
				GUILayout.Label("<color=#00ffff><b>УПРАВЛЕНИЕ МОПЕДОМ JONNEZ ES:</b></color>");
				if (instance != null)
				{
					GUILayout.Label("Статус пассажира: <b>" + (instance.isLocalPassenger ? "<color=#00ff00>ВЫ НА ПАССАЖИРСКОМ МЕСТЕ</color>" : "<color=#aaaaaa>СВОБОДНО</color>") + "</b>");
					GUILayout.Space(8f);
					if (!instance.isLocalPassenger)
					{
						if (GUILayout.Button("Сесть на пассажирское место (U)", GUILayout.Height(34f)))
						{
							instance.MountLocalPassenger();
						}
					}
					else if (GUILayout.Button("Слезть с мопеда (F)", GUILayout.Height(34f)))
					{
						instance.DismountLocalPassenger();
					}
				}
			}
			else if (selectedTab == 2)
			{
				ExtendedVehiclesSync instance2 = ExtendedVehiclesSync.Instance;
				BetterCheatBoxSyncManager instance3 = BetterCheatBoxSyncManager.Instance;
				GUILayout.Label("<color=#00ffff><b>САЦУМА И АВТОПАРК:</b></color>");
				GUILayout.Label("<color=#888888>Капот и заправка синхронизируются автоматически при взаимодействии в игре.</color>");
				GUILayout.Space(6f);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("[Отладка] Открыть капот", GUILayout.Height(30f)))
				{
					instance2?.BroadcastHoodState(isOpen: true);
				}
				if (GUILayout.Button("[Отладка] Закрыть капот", GUILayout.Height(30f)))
				{
					instance2?.BroadcastHoodState(isOpen: false);
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(6f);
				if (GUILayout.Button("[Отладка] Заправить Сацуму (+10 л 95-го)", GUILayout.Height(30f)))
				{
					instance2?.BroadcastRefueling(0, 10f);
				}
				GUILayout.Space(10f);
				GUILayout.Label("<color=#ffdd00><b>РЕМОНТ И СБОРКА САЦУМЫ:</b></color>");
				if (GUILayout.Button("\ud83d\udd27 Полный ремонт и затяжка ВСЕХ болтов Сацумы", GUILayout.Height(34f)))
				{
					instance3?.BroadcastRepair(fixAll: true, tightenBolts: true);
				}
				GUILayout.Space(6f);
				if (GUILayout.Button("⚡ ВОСКРЕСИТЬ САТСУМУ В ГАРАЖ (Ctrl+F9)", GUILayout.Height(34f)))
				{
					Vector3 garagePos = new Vector3(-10.5f, 4.4f, 7.5f);
					Quaternion garageRot = Quaternion.Euler(0, 90f, 0);
					if (BetterCheatBoxSyncManager.ReviveAndTeleportSatsuma(garagePos, garageRot))
					{
						PlayMakerFSM.BroadcastEvent("SATSUMA_REVIVED");
						ExtendedSyncDebugHUD.Log("<color=#00ffcc>⚡ [REVIVE]: Сацума успешно воскрешена в гараж!</color>");
					}
					else
					{
						ExtendedSyncDebugHUD.Log("<color=#ff3333>ERR [REVIVE]: Сацума не найдена в памяти игры!</color>");
					}
				}
				GUILayout.Space(10f);
				GUILayout.Label("<color=#ffdd00><b>САЛОН И ОСВЕЩЕНИЕ (CABIN LIGHT & DETAILS):</b></color>");
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Плафон Hayosiko (ВКЛ)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("HAYOSIKO(1500kg, 250)", "CABIN_LIGHT", true);
					ExtendedVehiclesSync.HandleVehicleToggle("HAYOSIKO(1500kg, 250)", "CABIN_LIGHT", true);
				}
				if (GUILayout.Button("Плафон Hayosiko (ВЫКЛ)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("HAYOSIKO(1500kg, 250)", "CABIN_LIGHT", false);
					ExtendedVehiclesSync.HandleVehicleToggle("HAYOSIKO(1500kg, 250)", "CABIN_LIGHT", false);
				}
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Плафон Satsuma (ВКЛ)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("SATSUMA(504kg, 330)", "CABIN_LIGHT", true);
					ExtendedVehiclesSync.HandleVehicleToggle("SATSUMA(504kg, 330)", "CABIN_LIGHT", true);
				}
				if (GUILayout.Button("Плафон Satsuma (ВЫКЛ)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("SATSUMA(504kg, 330)", "CABIN_LIGHT", false);
					ExtendedVehiclesSync.HandleVehicleToggle("SATSUMA(504kg, 330)", "CABIN_LIGHT", false);
				}
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Аварийка (ВКЛ/ВЫКЛ)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("SATSUMA(504kg, 330)", "HAZARDS", true);
					ExtendedVehiclesSync.HandleVehicleToggle("SATSUMA(504kg, 330)", "HAZARDS", true);
				}
				if (GUILayout.Button("Бардачок (ОТКР/ЗАКР)", GUILayout.Height(28f)))
				{
					instance2?.BroadcastVehicleToggle("SATSUMA(504kg, 330)", "GLOVEBOX", true);
					ExtendedVehiclesSync.HandleVehicleToggle("SATSUMA(504kg, 330)", "GLOVEBOX", true);
				}
				GUILayout.EndHorizontal();
			}
			else if (selectedTab == 3)
			{
				BetterCheatBoxSyncManager instance4 = BetterCheatBoxSyncManager.Instance;
				NetJoukoStorylineManager instance5 = NetJoukoStorylineManager.Instance;
				GUILayout.Label("<color=#00ffff><b>СЮЖЕТ И ЭКОНОМИКА:</b></color>");
				GUILayout.Label("<color=#888888>Чемодан Йоуко и продажа килью синхронизируются автоматически.</color>");
				GUILayout.Space(6f);
				if (GUILayout.Button("[Отладка] Забрать чемодан Йоуко (2,000,000 MK)", GUILayout.Height(32f)))
				{
					instance5?.BroadcastSuitcaseTaken();
				}
				GUILayout.Space(6f);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("+50,000 MK", GUILayout.Height(30f)))
				{
					instance4?.BroadcastMoney(50000f);
				}
				if (GUILayout.Button("+500,000 MK", GUILayout.Height(30f)))
				{
					instance4?.BroadcastMoney(500000f);
				}
				GUILayout.EndHorizontal();
				GUILayout.Space(6f);
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Готовое Килью (Таймер)", GUILayout.Height(30f)))
				{
					instance4?.BroadcastSkipTimer("KILJU");
				}
				if (GUILayout.Button("Фургон Дяди (Uncle)", GUILayout.Height(30f)))
				{
					instance4?.BroadcastSkipTimer("UNCLE");
				}
				GUILayout.EndHorizontal();
			}
			else if (selectedTab == 4)
			{
				NetPartsDeliverySync instance6 = NetPartsDeliverySync.Instance;
				GUILayout.Label("<color=#00ffff><b>КАТАЛОГ ЗАПЧАСТЕЙ И ПОЧТА ТЕЙМО:</b></color>");
				GUILayout.Label("<color=#00ffcc><b>★ 100% АВТОМАТИЧЕСКИЙ РЕЖИМ ВКЛЮЧЕН ★</b></color>");
				GUILayout.Label("<color=#888888>Заказ по каталогу, отправка конверта в ящик, звонок Теймо, касса и коробки синхронизируются сами при действиях игроков!</color>");
				GUILayout.Space(6f);
				if (instance6 != null)
				{
					GUILayout.Label("Каталог в гараже (Magazine): <b>" + (instance6.isCatalogHooked ? "<color=#00ff00>ПОДКЛЮЧЕН (АВТО)</color>" : "<color=#ff3333>НЕ НАЙДЕН</color>") + "</b>");
					GUILayout.Label("Почта Теймо (PostOrderBuy): <b>" + (instance6.isPostOfficeHooked ? "<color=#00ff00>ПОДКЛЮЧЕНА (АВТО)</color>" : "<color=#ff3333>НЕ НАЙДЕНА</color>") + "</b>");
					GUILayout.Label("Почтовый ящик (MailBox): <b>" + (instance6.isMailboxHooked ? "<color=#00ff00>ПОДКЛЮЧЕН (АВТО)</color>" : "<color=#ffaa00>СКАНИРОВАНИЕ</color>") + "</b>");
					GUILayout.Label("Домашний телефон (Telephone): <b>" + (instance6.isTelephoneHooked ? "<color=#00ff00>ПОДКЛЮЧЕН (АВТО)</color>" : "<color=#ffaa00>СКАНИРОВАНИЕ</color>") + "</b>");
					GUILayout.Space(8f);
					GUILayout.Label("<color=#ffdd00>Кнопки ручного тестирования / отладки:</color>");
					if (GUILayout.Button("\ud83d\udce6 [Отладка] Синхронизировать заказ каталога", GUILayout.Height(30f)))
					{
						instance6.BroadcastOrderPlaced();
					}
					GUILayout.Space(4f);
					if (GUILayout.Button("✉ [Отладка] Отправить конверт почтой", GUILayout.Height(30f)))
					{
						instance6.BroadcastEnvelopeMailed();
					}
					GUILayout.Space(4f);
					if (GUILayout.Button("☎ [Отладка] Симулировать прибытие посылок / Звонок", GUILayout.Height(30f)))
					{
						instance6.BroadcastDeliveryReady();
					}
					GUILayout.Space(4f);
					if (GUILayout.Button("\ud83d\udcb3 [Отладка] Оплатить и выдать коробки на кассе", GUILayout.Height(30f)))
					{
						instance6.BroadcastPostOrderPay();
					}
				}
			}
			else if (selectedTab == 5)
			{
				DrawBetterCheatBoxTab();
			}
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Закрыть панель [F10]", GUILayout.Height(28f)))
			{
				isVisible = false;
			}
		}

		private void DrawBetterCheatBoxTab()
		{
			BetterCheatBoxSyncManager instance = BetterCheatBoxSyncManager.Instance;
			BetterCheatBox betterCheatBox = BetterCheatBoxSyncManager.GetBetterCheatBox();
			GUILayout.Label("<color=#00ffff><b>СИНХРОНИЗАЦИЯ BETTER CHEAT BOX:</b></color>");
			GUILayout.Label("Мод BetterCheatBox: <b>" + ((betterCheatBox != null) ? "<color=#00ff00>ОБНАРУЖЕН И ПОДКЛЮЧЕН</color>" : "<color=#ffaa00>СКАНИРОВАНИЕ / РЕЖИМ СОВМЕСТИМОСТИ</color>") + "</b>");
			GUILayout.Label("Harmony сетевые перехватчики: <b>" + ((instance != null && instance.isHarmonyPatched) ? "<color=#00ff00>АКТИВНЫ (100% ТРАНСЛЯЦИЯ В P2P)</color>" : "<color=#ffff00>ОЖИДАНИЕ ЗАГРУЗКИ МОДА</color>") + "</b>");
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>1. РЕМОНТ И ТЮНИНГ САЦУМЫ:</b></color>");
			if (GUILayout.Button("\ud83d\udd27 Полный ремонт, сборка, затяжка ВСЕХ болтов и тюнинг Сацумы", GUILayout.Height(34f)))
			{
				instance?.BroadcastFullRepair(fixAll: true, tightenBolts: true, tuneEngine: true);
			}
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("\ud83e\uddef Потушить огонь двигателя", GUILayout.Height(28f)))
			{
				instance?.BroadcastSatsumaFire(active: false);
			}
			if (GUILayout.Button("\ud83d\udd25 Зажечь двигатель (тест)", GUILayout.Height(28f)))
			{
				instance?.BroadcastSatsumaFire(active: true);
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>2. ТЕЛЕПОРТАЦИЯ АВТОМОБИЛЕЙ (К ИГРОКУ):</b></color>");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Сацума", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Satsuma");
			}
			if (GUILayout.Button("Фургон (Hayosiko)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Hayosiko");
			}
			if (GUILayout.Button("Мопед (Jonnez)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Jonnez");
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Ассенизатор (Gifu)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Gifu");
			}
			if (GUILayout.Button("Маслкар (Ferndale)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Ferndale");
			}
			if (GUILayout.Button("Руско (Ruscko)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Ruscko");
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Трактор (Kekmet)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Kekmet");
			}
			if (GUILayout.Button("Катер (Boat)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Boat");
			}
			if (GUILayout.Button("Комбайн (Combine)", GUILayout.Height(28f)))
			{
				instance?.TeleportVehicleToPlayer("Combine");
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>3. СЮЖЕТ И СКИПЫ ТАЙМЕРОВ:</b></color>");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Фургон Дяди", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("UNCLE");
			}
			if (GUILayout.Button("Посылка каталога", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("POST_ORDER");
			}
			if (GUILayout.Button("Ремонт Флитари", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("REPAIR_WORK");
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Готовое килью", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("KILJU");
			}
			if (GUILayout.Button("Ресток Теймо", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("RESTOCK");
			}
			if (GUILayout.Button("Случайная погода", GUILayout.Height(28f)))
			{
				instance?.BroadcastSkip("WEATHER");
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>4. ДЕНЬГИ, ПОТРЕБНОСТИ И КЛЮЧИ:</b></color>");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("+10,000 MK", GUILayout.Height(28f)))
			{
				instance?.BroadcastMoneyAdd(10000f);
			}
			if (GUILayout.Button("+50,000 MK", GUILayout.Height(28f)))
			{
				instance?.BroadcastMoneyAdd(50000f);
			}
			if (GUILayout.Button("+500,000 MK", GUILayout.Height(28f)))
			{
				instance?.BroadcastMoneyAdd(500000f);
			}
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("⚡ Обнулить потребности (Godmode)", GUILayout.Height(28f)))
			{
				instance?.ResetAllNeeds();
			}
			if (GUILayout.Button("\ud83d\udd11 Разблокировать ВСЕ ключи", GUILayout.Height(28f)))
			{
				instance?.UnlockAllKeys();
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>5. ШИНЫ И ДОМ:</b></color>");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Выровнять подвеску Сацумы", GUILayout.Height(28f)))
			{
				for (int i = 0; i < 4; i++)
				{
					instance?.BroadcastStraightenSuspension(i);
				}
			}
			if (GUILayout.Button("Очистить пол дома от луж", GUILayout.Height(28f)))
			{
				for (int j = 0; j < 4; j++)
				{
					instance?.BroadcastPissStain(j, 0f, 0f);
				}
			}
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
			GUILayout.Label("<color=#ffdd00><b>6. ФИЗИКА И ТОПЛИВО:</b></color>");
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Скорость 1x", GUILayout.Height(28f))) instance?.BroadcastPhysicsSpeed(1f);
			if (GUILayout.Button("Скорость 2x", GUILayout.Height(28f))) instance?.BroadcastPhysicsSpeed(2f);
			if (GUILayout.Button("Скорость 0.25x", GUILayout.Height(28f))) instance?.BroadcastPhysicsSpeed(0.25f);
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Заправить все 8 авто (100%)", GUILayout.Height(28f)))
			{
				instance?.BroadcastFuel("Satsuma", 36f);
				instance?.BroadcastFuel("Hayosiko", 40f);
				instance?.BroadcastFuel("Jonnez ES", 3.4f);
				instance?.BroadcastFuel("Ferndale", 79f);
				instance?.BroadcastFuel("Ruscko", 30f);
				instance?.BroadcastFuel("Gifu", 300f);
				instance?.BroadcastFuel("Kekmet", 65f);
				instance?.BroadcastFuel("Boat", 4f);
			}
			GUILayout.EndHorizontal();
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

		public string ItemId;
		public Rigidbody rb;
		public bool isHeldByRemote;
		private bool wasHeldLocally;

		private GameEvent itemPickedUpEvent;
		private GameEvent itemDroppedEvent;

		private void Awake()
		{
			if (string.IsNullOrEmpty(ItemId))
			{
				Instance = this;
			}
		}

		private void Start()
		{
			if (this == Instance)
			{
				itemPickedUpEvent = new GameEvent("Cheat_ItemPickedUp", OnReceiveItemPickedUp);
				itemDroppedEvent = new GameEvent("Cheat_ItemDropped", OnReceiveItemDropped);
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
			if (!string.IsNullOrEmpty(ItemId) && RegisteredItems.ContainsKey(ItemId) && RegisteredItems[ItemId] == this)
			{
				RegisteredItems.Remove(ItemId);
			}
		}

		public void OnSceneReset()
		{
			RegisteredItems.Clear();
		}

		public static Rigidbody GetGrabbedRigidbody()
		{
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
			sync.ItemId = id;
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

			bool isHeld = CheckIsHeldLocally();
			if (isHeld && !wasHeldLocally)
			{
				wasHeldLocally = true;
				isHeldByRemote = false;
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
				writer.Write((long)steamId);
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
			ulong steamId = (ulong)reader.ReadInt64();
			if (steamId == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				CheatSpawnedItemSync item = FindItem(id);
				if (item != null)
				{
					item.isHeldByRemote = true;
					item.wasHeldLocally = false;
					Player partner = WreckMPGlobals.Players.ContainsKey(steamId) ? WreckMPGlobals.Players[steamId] : null;
					if (partner != null)
					{
						Transform hand = AvatarBoneHelper.FindPlayerHandRight(partner);
						if (hand != null)
						{
							item.transform.parent = hand;
							if (id.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
							{
								item.transform.localPosition = new Vector3(0.05f, 0.04f, 0.08f);
								item.transform.localRotation = Quaternion.Euler(0f, 90f, 15f);
							}
							else
							{
								item.transform.localPosition = new Vector3(0f, 0.05f, 0.1f);
								item.transform.localRotation = Quaternion.identity;
							}
						}
						if (item.rb != null)
						{
							item.rb.isKinematic = true;
							item.rb.velocity = Vector3.zero;
						}
						SetPartnerGrabbedItem(partner, item.rb);
						ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [SPAWN]: Игрок " + partner.PlayerName + " поднял " + id + "</color>");
					}
				}
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
					item.transform.parent = null;
					item.transform.position = pos;
					item.transform.rotation = rot;
					if (item.rb != null)
					{
						item.rb.isKinematic = false;
						item.rb.velocity = vel;
					}
					Collider[] cols = item.GetComponentsInChildren<Collider>(true);
					for (int c = 0; c < cols.Length; c++)
					{
						if (cols[c] != null) cols[c].enabled = true;
					}
					BetterCheatBoxSyncManager.UpdateNetRigidbodyCache(item.gameObject, pos, rot);
					foreach (var p in WreckMPGlobals.Players.Values)
					{
						SetPartnerGrabbedItem(p, null);
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
			}
			catch (Exception ex)
			{
				ModConsole.Error("[UniversalHandItemSync] Start error: " + ex.Message);
			}
		}

		public void OnSceneReset()
		{
			lastHeldItemName = "";
			wasHolding = false;
			cachedPickUpSlot = null;
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

		private GameObject GetLocallyHeldItem()
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
					if (itemName.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0 || itemName.IndexOf("msc_shared_envelope", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						CheatSpawnedItemSync envItem = CheatSpawnedItemSync.FindItem("msc_shared_envelope") ?? CheatSpawnedItemSync.FindItem("msc_order_envelope_shared");
						if (envItem != null && envItem.isHeldByRemote)
						{
							ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [HAND]: Конверт заказа уже синхронизирован в руке напарника</color>");
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
							if (c is Collider) ((Collider)c).enabled = false;
							if (c is Rigidbody) ((Rigidbody)c).isKinematic = true;
							if (c is PlayMakerFSM) ((PlayMakerFSM)c).enabled = false;
						}

						visual.layer = 2;
						Transform[] allChilds = visual.GetComponentsInChildren<Transform>(true);
						for (int j = 0; j < allChilds.Length; j++)
						{
							if (allChilds[j] != null) allChilds[j].gameObject.layer = 2;
						}

						visual.transform.parent = handRight;
						visual.transform.localScale = template.transform.localScale;

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
			try
			{
				string clean = GetCleanItemName(itemName);
				GameObject found = GameObject.Find(itemName) ?? 
				                   (!string.IsNullOrEmpty(clean) ? GameObject.Find(clean) : null) ?? 
				                   GameObject.Find(clean + "(itemx)") ?? 
				                   GameObject.Find(clean + "(Clone)");
				if (found != null) return found;

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
							return allResources[j];
						}
					}
				}
			}
			catch { }

			return null;
		}
	}

	public class NetTelephoneHardwareSync : MonoBehaviour
	{
		public static NetTelephoneHardwareSync Instance;
		public bool isNetworkApplying;

		private GameEvent syncPhoneReceiverEvent;
		private GameEvent syncPhonePlugEvent;

		public PlayMakerFSM receiverFsm;
		public PlayMakerFSM plugFsm;
		public GameObject phoneCordIn;
		public GameObject phoneCordOut;

		public bool cachedReceiverPickedUp;
		public bool cachedPlugIn = true;
		private float nextScanTime;

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			syncPhoneReceiverEvent = new GameEvent("Sync_PhoneReceiver", OnReceivePhoneReceiver);
			syncPhonePlugEvent = new GameEvent("Sync_PhonePlug", OnReceivePhonePlug);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			receiverFsm = null;
			plugFsm = null;
			phoneCordIn = null;
			phoneCordOut = null;
			cachedReceiverPickedUp = false;
			cachedPlugIn = true;
			nextScanTime = 0f;
			ScanAndHookTelephone();
		}

		private void Update()
		{
			if (Time.time > nextScanTime && (receiverFsm == null || plugFsm == null || phoneCordIn == null))
			{
				nextScanTime = Time.time + 2f;
				ScanAndHookTelephone();
			}

			if (isNetworkApplying) return;

			if (receiverFsm != null && receiverFsm.Fsm != null)
			{
				string state = receiverFsm.ActiveStateName;
				if (string.Equals(state, "Pick phone", StringComparison.OrdinalIgnoreCase))
				{
					if (!cachedReceiverPickedUp)
					{
						cachedReceiverPickedUp = true;
						BroadcastPhoneReceiver(true);
					}
				}
				else if (string.Equals(state, "Close phone", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "State 1", StringComparison.OrdinalIgnoreCase))
				{
					if (cachedReceiverPickedUp)
					{
						cachedReceiverPickedUp = false;
						BroadcastPhoneReceiver(false);
					}
				}
			}

			bool currentPlugIn = cachedPlugIn;
			if (plugFsm != null)
			{
				FsmBool cordVar = plugFsm.FsmVariables.FindFsmBool("CordPhone") ?? plugFsm.FsmVariables.FindFsmBool("Cord");
				if (cordVar != null)
				{
					currentPlugIn = cordVar.Value;
				}
				else if (phoneCordIn != null)
				{
					currentPlugIn = phoneCordIn.activeInHierarchy;
				}
			}
			else if (phoneCordIn != null)
			{
				currentPlugIn = phoneCordIn.activeInHierarchy;
			}

			if (currentPlugIn != cachedPlugIn)
			{
				cachedPlugIn = currentPlugIn;
				BroadcastPhonePlug(currentPlugIn);
			}
		}

		public void ScanAndHookTelephone()
		{
			try
			{
				if (receiverFsm == null)
				{
					Transform logic = GameObject.Find("YARD")?.transform.Find("Building/LIVINGROOM/Telephone/Logic")
						?? GameObject.Find("YARD")?.transform.Find("Building/HomeInterior/Telephone/Logic")
						?? GameObject.Find("Telephone/Logic")?.transform
						?? GameObject.Find("TELEPHONE")?.transform;

					if (logic != null)
					{
						Transform handleTf = logic.Find("UseHandle") ?? logic.Find("Receiver") ?? logic.parent?.Find("Receiver") ?? logic.parent?.Find("UseHandle");
						if (handleTf != null)
						{
							receiverFsm = handleTf.GetComponent<PlayMakerFSM>() ?? handleTf.GetComponentInChildren<PlayMakerFSM>();
							if (receiverFsm != null)
							{
								try
								{
									FsmEvent evPick = receiverFsm.AddEvent("MP_PICK");
									receiverFsm.AddGlobalTransition(evPick, "Pick phone");
									FsmEvent evClose = receiverFsm.AddEvent("MP_CLOSE");
									receiverFsm.AddGlobalTransition(evClose, "Close phone");
								}
								catch {}

								SafeFsmWatcher.Attach(receiverFsm, new string[] { "Pick phone", "Close phone" }, () =>
								{
									if (isNetworkApplying || receiverFsm == null) return;
									string active = receiverFsm.ActiveStateName;
									bool picked = string.Equals(active, "Pick phone", StringComparison.OrdinalIgnoreCase);
									if (picked != cachedReceiverPickedUp)
									{
										cachedReceiverPickedUp = picked;
										BroadcastPhoneReceiver(picked);
									}
								});
								ExtendedSyncDebugHUD.Log("<color=#33ff33>[PHONE]</color> Трубка телефона успешно перехвачена!");
							}
						}
					}
				}

				Transform cordTf = GameObject.Find("YARD")?.transform.Find("Building/LIVINGROOM/Telephone/Cord")
					?? GameObject.Find("YARD")?.transform.Find("Building/HomeInterior/Telephone/Cord")
					?? GameObject.Find("Telephone/Cord")?.transform
					?? GameObject.Find("Cord")?.transform;

				if (cordTf != null)
				{
					Transform inTf = cordTf.Find("PhoneCordIn") ?? AvatarBoneHelper.FindChildRecursive(cordTf, "PhoneCordIn") ?? cordTf.Find("CordIn") ?? cordTf.Find("PlugIn");
					Transform outTf = cordTf.Find("PhoneCordOut") ?? AvatarBoneHelper.FindChildRecursive(cordTf, "PhoneCordOut") ?? cordTf.Find("CordOut") ?? cordTf.Find("PlugOut");
					if (inTf != null) phoneCordIn = inTf.gameObject;
					if (outTf != null) phoneCordOut = outTf.gameObject;

					if (plugFsm == null)
					{
						plugFsm = cordTf.GetComponent<PlayMakerFSM>() ?? cordTf.GetComponentInChildren<PlayMakerFSM>();
						if (plugFsm != null)
						{
							SafeFsmWatcher.Attach(plugFsm, new string[] { "Position", "Wait player", "Wait button", "State 1", "State 2" }, () =>
							{
								if (isNetworkApplying || plugFsm == null) return;
								FsmBool cordVar = plugFsm.FsmVariables.FindFsmBool("CordPhone") ?? plugFsm.FsmVariables.FindFsmBool("Cord");
								bool isPlugged = (cordVar != null) ? cordVar.Value : (phoneCordIn != null ? phoneCordIn.activeInHierarchy : true);
								if (isPlugged != cachedPlugIn)
								{
									cachedPlugIn = isPlugged;
									BroadcastPhonePlug(isPlugged);
								}
							});
							ExtendedSyncDebugHUD.Log("<color=#33ff33>[PHONE]</color> Вилка телефона успешно перехвачена!");
						}
					}
				}
				else if (plugFsm == null)
				{
					GameObject plugGo = GameObject.Find("YARD/Building/HomeInterior/Telephone/Plug")
						?? GameObject.Find("YARD/Building/LIVINGROOM/Telephone/Plug")
						?? GameObject.Find("Telephone/Plug")
						?? GameObject.Find("Plug");

					if (plugGo != null)
					{
						plugFsm = plugGo.GetComponent<PlayMakerFSM>() ?? plugGo.GetComponentInChildren<PlayMakerFSM>();
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetTelephoneHardwareSync] Ошибка сканирования: " + ex.Message);
			}
		}

		public void BroadcastPhoneReceiver(bool isPickedUp)
		{
			if (isNetworkApplying || syncPhoneReceiverEvent == null) return;
			using (GameEventWriter writer = syncPhoneReceiverEvent.Writer())
			{
				writer.Write(isPickedUp);
				syncPhoneReceiverEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [PHONE]: Трубка " + (isPickedUp ? "снята" : "повешена") + "</color>");
		}

		public void BroadcastPhonePlug(bool isPluggedIn)
		{
			if (isNetworkApplying || syncPhonePlugEvent == null) return;
			using (GameEventWriter writer = syncPhonePlugEvent.Writer())
			{
				writer.Write(isPluggedIn);
				syncPhonePlugEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [PHONE]: Вилка розетки " + (isPluggedIn ? "вставлена" : "выдернута на пол") + "</color>");
		}

		private void OnReceivePhoneReceiver(GameEventReader reader)
		{
			bool isPickedUp = reader.ReadBoolean();
			if (reader.sender == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				cachedReceiverPickedUp = isPickedUp;
				if (receiverFsm != null)
				{
					receiverFsm.SendEvent(isPickedUp ? "MP_PICK" : "MP_CLOSE");
				}
				Transform soundTf = (receiverFsm != null) ? receiverFsm.transform : transform;
				try
				{
					MasterAudio.PlaySound3DAndForget("HouseFoley", soundTf, true, 1f, null, 0f, isPickedUp ? "phone_pick" : "phone_hangup");
				}
				catch {}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [PHONE]: Напарник " + (isPickedUp ? "снял трубку" : "положил трубку") + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetTelephoneHardwareSync] Ошибка OnReceivePhoneReceiver: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void OnReceivePhonePlug(GameEventReader reader)
		{
			bool isPluggedIn = reader.ReadBoolean();
			if (reader.sender == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				cachedPlugIn = isPluggedIn;
				if (phoneCordIn != null) phoneCordIn.SetActive(isPluggedIn);
				if (phoneCordOut != null) phoneCordOut.SetActive(!isPluggedIn);
				if (plugFsm != null)
				{
					FsmBool cordVar = plugFsm.FsmVariables.FindFsmBool("CordPhone") ?? plugFsm.FsmVariables.FindFsmBool("Cord");
					if (cordVar != null) cordVar.Value = isPluggedIn;
					plugFsm.SendEvent("Position");
				}
				Transform soundTf = (phoneCordIn != null) ? phoneCordIn.transform : (plugFsm != null ? plugFsm.transform : transform);
				try
				{
					MasterAudio.PlaySound3DAndForget("HouseFoley", soundTf, true, 1f, null, 0f, "light_switch");
				}
				catch {}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [PHONE]: Напарник " + (isPluggedIn ? "вставил вилку в розетку" : "выдернул вилку на пол") + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetTelephoneHardwareSync] Ошибка OnReceivePhonePlug: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}

	public class NetUrinationSync : MonoBehaviour
	{
		public static NetUrinationSync Instance;
		public bool isNetworkApplying;

		private GameEvent syncPlayerPeeEvent;

		private GameObject localUrinateGo;
		private Transform localFluid;
		private ParticleSystem localPs;
		private AudioSource localAudio;
		private PlayMakerFSM localUrinateFsm;
		private bool wasLocalPeeing;
		private float nextScanTime;

		private class RemotePeeStream
		{
			public ulong steamId;
			public GameObject streamObj;
			public ParticleSystem ps;
			public AudioSource audio;
			public bool lastPeeingState;

			public void SetPeeing(bool peeing)
			{
				lastPeeingState = peeing;
				if (streamObj != null)
				{
					streamObj.SetActive(peeing);
				}
				if (ps != null)
				{
					if (peeing) { ps.Clear(); ps.Play(); }
					else { ps.Stop(); }
				}
				if (audio != null)
				{
					if (peeing && !audio.isPlaying) audio.Play();
					else if (!peeing && audio.isPlaying) audio.Stop();
				}
			}
		}

		private readonly Dictionary<ulong, RemotePeeStream> remoteStreams = new Dictionary<ulong, RemotePeeStream>();

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			syncPlayerPeeEvent = new GameEvent("Sync_PlayerPee", OnReceivePlayerPee);
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Combine(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			OnSceneReset();
		}

		private void OnDestroy()
		{
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Remove(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
		}

		private void OnMemberExit(ulong steamId)
		{
			if (remoteStreams.TryGetValue(steamId, out var stream))
			{
				if (stream.streamObj != null) UnityEngine.Object.Destroy(stream.streamObj);
				remoteStreams.Remove(steamId);
			}
		}

		public void OnSceneReset()
		{
			localUrinateGo = null;
			localFluid = null;
			localPs = null;
			localAudio = null;
			localUrinateFsm = null;
			wasLocalPeeing = false;
			nextScanTime = 0f;
			foreach (var kvp in remoteStreams)
			{
				if (kvp.Value.streamObj != null) UnityEngine.Object.Destroy(kvp.Value.streamObj);
			}
			remoteStreams.Clear();
			ScanLocalUrinate();
		}

		private void Update()
		{
			if (Time.time > nextScanTime && (localUrinateGo == null || localFluid == null))
			{
				nextScanTime = Time.time + 2f;
				ScanLocalUrinate();
			}

			foreach (var kvp in remoteStreams)
			{
				if (kvp.Value.lastPeeingState && kvp.Value.streamObj != null)
				{
					Player partner = WreckMPGlobals.Players.ContainsKey(kvp.Key) ? WreckMPGlobals.Players[kvp.Key] : null;
					if (partner != null && partner.player != null)
					{
						Transform pelvis = AvatarBoneHelper.FindPlayerPelvis(partner);
						if (pelvis != null && kvp.Value.streamObj.transform.parent != pelvis)
						{
							kvp.Value.streamObj.transform.parent = pelvis;
							kvp.Value.streamObj.transform.localPosition = new Vector3(0f, -0.05f, 0.18f);
							kvp.Value.streamObj.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
						}
					}
				}
			}

			if (isNetworkApplying) return;

			bool pPressed = Input.GetKey(KeyCode.P);
			bool isPeeing = false;
			if (pPressed)
			{
				bool psPlaying = localPs != null && localPs.isPlaying;
				bool fsmPeeing = localUrinateFsm != null && (string.Equals(localUrinateFsm.ActiveStateName, "Urinate", StringComparison.OrdinalIgnoreCase) || string.Equals(localUrinateFsm.ActiveStateName, "Start urinate", StringComparison.OrdinalIgnoreCase));
				bool fluidActive = localFluid != null && localFluid.gameObject.activeSelf;
				FsmFloat urineVar = FsmVariables.GlobalVariables.FindFsmFloat("PlayerUrine");
				bool hasUrine = (urineVar == null || urineVar.Value > 0.05f);

				isPeeing = (psPlaying || fsmPeeing || fluidActive) && hasUrine;
			}
			else
			{
				isPeeing = false;
			}

			if (isPeeing != wasLocalPeeing)
			{
				wasLocalPeeing = isPeeing;
				BroadcastPlayerPee(isPeeing);
			}
		}

		private void ScanLocalUrinate()
		{
			try
			{
				localUrinateGo = GameObject.Find("FPSPlayer/Player/Urinate") ?? GameObject.Find("PLAYER/Urinate") ?? GameObject.Find("Urinate");
				if (localUrinateGo != null)
				{
					localFluid = localUrinateGo.transform.Find("Fluid");
					localPs = localUrinateGo.GetComponentInChildren<ParticleSystem>();
					localAudio = localUrinateGo.GetComponentInChildren<AudioSource>();
					localUrinateFsm = localUrinateGo.GetComponent<PlayMakerFSM>();
				}
			}
			catch
			{
			}
		}

		public void BroadcastPlayerPee(bool isPeeing)
		{
			if (isNetworkApplying || syncPlayerPeeEvent == null) return;
			using (GameEventWriter writer = syncPlayerPeeEvent.Writer())
			{
				writer.Write(isPeeing);
				syncPlayerPeeEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [PEE]: Мочеиспускание " + (isPeeing ? "НАЧАТО" : "ЗАВЕРШЕНО") + "</color>");
		}

		private RemotePeeStream GetOrCreateRemoteStream(ulong steamId)
		{
			if (remoteStreams.TryGetValue(steamId, out var existing) && existing != null && existing.streamObj != null)
			{
				return existing;
			}

			Player partner = WreckMPGlobals.Players.ContainsKey(steamId) ? WreckMPGlobals.Players[steamId] : null;
			if (partner == null) return null;

			Transform pelvis = AvatarBoneHelper.FindPlayerPelvis(partner);
			if (pelvis == null) return null;

			GameObject streamObj = null;
			ParticleSystem ps = null;
			AudioSource audio = null;

			if (localFluid != null)
			{
				streamObj = (GameObject)UnityEngine.Object.Instantiate(localFluid.gameObject);
				streamObj.name = "RemotePeeStream_" + steamId;
				streamObj.transform.parent = pelvis;
				streamObj.transform.localPosition = new Vector3(0f, -0.05f, 0.18f);
				streamObj.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
				ps = streamObj.GetComponentInChildren<ParticleSystem>();
				audio = streamObj.GetComponentInChildren<AudioSource>();
			}
			else
			{
				streamObj = new GameObject("RemotePeeStream_" + steamId);
				streamObj.transform.parent = pelvis;
				streamObj.transform.localPosition = new Vector3(0f, -0.05f, 0.18f);
				streamObj.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);

				ParticleSystem[] allPs = Resources.FindObjectsOfTypeAll<ParticleSystem>();
				for (int i = 0; i < allPs.Length; i++)
				{
					if (allPs[i] != null && (allPs[i].name.IndexOf("Fluid", StringComparison.OrdinalIgnoreCase) >= 0 ||
						allPs[i].name.IndexOf("pee", StringComparison.OrdinalIgnoreCase) >= 0 ||
						allPs[i].name.IndexOf("urine", StringComparison.OrdinalIgnoreCase) >= 0))
					{
						GameObject clone = (GameObject)UnityEngine.Object.Instantiate(allPs[i].gameObject);
						clone.transform.parent = streamObj.transform;
						clone.transform.localPosition = Vector3.zero;
						clone.transform.localRotation = Quaternion.identity;
						ps = clone.GetComponentInChildren<ParticleSystem>();
						audio = clone.GetComponentInChildren<AudioSource>();
						break;
					}
				}
			}

			if (streamObj != null)
			{
				if (audio == null)
				{
					audio = streamObj.AddComponent<AudioSource>();
					if (localAudio != null && localAudio.clip != null)
					{
						audio.clip = localAudio.clip;
					}
					else
					{
						AudioClip[] clips = Resources.FindObjectsOfTypeAll<AudioClip>();
						for (int c = 0; c < clips.Length; c++)
						{
							if (clips[c] != null && (clips[c].name.IndexOf("pee", StringComparison.OrdinalIgnoreCase) >= 0 ||
								clips[c].name.IndexOf("urin", StringComparison.OrdinalIgnoreCase) >= 0 ||
								clips[c].name.IndexOf("piss", StringComparison.OrdinalIgnoreCase) >= 0))
							{
								audio.clip = clips[c];
								break;
							}
						}
					}
				}

				if (audio != null)
				{
					audio.spatialBlend = 1.0f;
					audio.minDistance = 1.0f;
					audio.maxDistance = 25.0f;
					audio.rolloffMode = AudioRolloffMode.Linear;
					audio.loop = true;
					audio.volume = 0.85f;
				}

				streamObj.SetActive(false);
			}

			RemotePeeStream newStream = new RemotePeeStream
			{
				steamId = steamId,
				streamObj = streamObj,
				ps = ps,
				audio = audio
			};
			remoteStreams[steamId] = newStream;
			return newStream;
		}

		private void OnReceivePlayerPee(GameEventReader reader)
		{
			bool isPeeing = reader.ReadBoolean();
			ulong sender = reader.sender;
			if (sender == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				RemotePeeStream stream = GetOrCreateRemoteStream(sender);
				if (stream != null)
				{
					stream.SetPeeing(isPeeing);
				}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [PEE]: Напарник " + (isPeeing ? "начал мочиться" : "закончил мочиться") + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetUrinationSync] Ошибка OnReceivePlayerPee: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}

	public class NetFlashlightSync : MonoBehaviour
	{
		public static NetFlashlightSync Instance;
		public bool isNetworkApplying;

		private GameEvent syncFlashlightEvent;

		private GameObject flashlightGo;
		private Light flashlightLight;
		private PlayMakerFSM flashlightFsm;

		private bool cachedLightOn;
		private float nextScanTime;

		private readonly Dictionary<ulong, Light> remoteHandLights = new Dictionary<ulong, Light>();
		private readonly Dictionary<ulong, bool> remoteLightStates = new Dictionary<ulong, bool>();

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			syncFlashlightEvent = new GameEvent("Sync_Flashlight", OnReceiveFlashlight);
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Combine(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
			OnSceneReset();
		}

		private void OnDestroy()
		{
			WreckMPGlobals.OnMemberExit = (Action<ulong>)Delegate.Remove(WreckMPGlobals.OnMemberExit, new Action<ulong>(OnMemberExit));
		}

		private void OnMemberExit(ulong steamId)
		{
			if (remoteHandLights.TryGetValue(steamId, out var light))
			{
				if (light != null) UnityEngine.Object.Destroy(light.gameObject);
				remoteHandLights.Remove(steamId);
			}
			remoteLightStates.Remove(steamId);
		}

		public void OnSceneReset()
		{
			flashlightGo = null;
			flashlightLight = null;
			flashlightFsm = null;
			cachedLightOn = false;
			nextScanTime = 0f;
			foreach (var kvp in remoteHandLights)
			{
				if (kvp.Value != null) UnityEngine.Object.Destroy(kvp.Value.gameObject);
			}
			remoteHandLights.Clear();
			remoteLightStates.Clear();
			ScanAndHookFlashlight();
		}

		private void Update()
		{
			if (Time.time > nextScanTime && (flashlightGo == null || flashlightLight == null))
			{
				nextScanTime = Time.time + 2f;
				ScanAndHookFlashlight();
			}

			foreach (var kvp in remoteLightStates)
			{
				ulong sId = kvp.Key;
				bool shouldBeOn = kvp.Value;
				Player partner = WreckMPGlobals.Players.ContainsKey(sId) ? WreckMPGlobals.Players[sId] : null;
				if (partner != null && partner.player != null)
				{
					Light hl = GetOrCreateHandLight(partner);
					if (hl != null && hl.enabled != shouldBeOn)
					{
						hl.enabled = shouldBeOn;
					}
				}
			}

			if (isNetworkApplying) return;

			if (flashlightLight != null)
			{
				bool currentOn = flashlightLight.enabled && flashlightLight.gameObject.activeInHierarchy;
				if (currentOn != cachedLightOn)
				{
					cachedLightOn = currentOn;
					BroadcastFlashlight(currentOn);
				}
			}
		}

		public void ScanAndHookFlashlight()
		{
			try
			{
				flashlightGo = GameObject.Find("ITEMS/flashlight(itemx)")
					?? GameObject.Find("flashlight(itemx)")
					?? GameObject.Find("flashlight(item)")
					?? GameObject.Find("FLASHLIGHT")
					?? GameObject.Find("Flashlight");

				if (flashlightGo == null)
				{
					Transform items = GameObject.Find("ITEMS")?.transform;
					if (items != null)
					{
						flashlightGo = items.Find("flashlight(itemx)")?.gameObject
							?? items.Find("flashlight(item)")?.gameObject
							?? items.Find("FLASHLIGHT")?.gameObject;
					}
				}

				if (flashlightGo != null)
				{
					flashlightLight = flashlightGo.GetComponentInChildren<Light>();
					if (flashlightLight == null)
					{
						Light[] lights = flashlightGo.GetComponentsInChildren<Light>(true);
						if (lights != null && lights.Length > 0) flashlightLight = lights[0];
					}
					flashlightFsm = flashlightGo.GetComponent<PlayMakerFSM>() ?? flashlightGo.GetComponentInChildren<PlayMakerFSM>();

					if (flashlightFsm != null)
					{
						try
						{
							FsmEvent onEv = flashlightFsm.AddEvent("MP_ON");
							flashlightFsm.AddGlobalTransition(onEv, "On");
							FsmEvent offEv = flashlightFsm.AddEvent("MP_OFF");
							flashlightFsm.AddGlobalTransition(offEv, "Off");
						}
						catch {}

						SafeFsmWatcher.Attach(flashlightFsm, new string[] { "On", "On 2", "Off", "OFF", "State 2" }, () =>
						{
							if (isNetworkApplying) return;
							bool on = (flashlightLight != null) ? (flashlightLight.enabled && flashlightLight.gameObject.activeInHierarchy) : false;
							if (on != cachedLightOn)
							{
								cachedLightOn = on;
								BroadcastFlashlight(on);
							}
						});
						ExtendedSyncDebugHUD.Log("<color=#33ff33>[FLASHLIGHT]</color> Карманный фонарик успешно перехвачен!");
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetFlashlightSync] Ошибка сканирования: " + ex.Message);
			}
		}

		public void BroadcastFlashlight(bool isOn)
		{
			if (isNetworkApplying || syncFlashlightEvent == null) return;
			using (GameEventWriter writer = syncFlashlightEvent.Writer())
			{
				writer.Write(isOn);
				syncFlashlightEvent.Send(writer, 0uL, safe: true);
			}
			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [FLASHLIGHT]: Фонарик " + (isOn ? "ВКЛЮЧЕН" : "ВЫКЛЮЧЕН") + "</color>");
		}

		private Light GetOrCreateHandLight(Player remotePlayer)
		{
			if (remotePlayer == null || remotePlayer.player == null) return null;
			ulong steamId = remotePlayer.SteamID;
			if (remoteHandLights.TryGetValue(steamId, out var existing) && existing != null)
			{
				return existing;
			}

			Transform hand = AvatarBoneHelper.FindPlayerHandRight(remotePlayer);
			if (hand == null) return null;

			GameObject lightObj = new GameObject("RemotePartnerFlashlightLight");
			lightObj.transform.parent = hand;
			lightObj.transform.localPosition = new Vector3(0f, 0.05f, 0.15f);
			lightObj.transform.localRotation = Quaternion.identity;

			Light light = lightObj.AddComponent<Light>();
			light.type = LightType.Spot;
			light.range = (flashlightLight != null) ? flashlightLight.range : 30f;
			light.spotAngle = (flashlightLight != null) ? flashlightLight.spotAngle : 55f;
			light.intensity = (flashlightLight != null) ? flashlightLight.intensity : 2.5f;
			light.color = (flashlightLight != null) ? flashlightLight.color : new Color(1f, 0.95f, 0.8f);
			light.shadows = LightShadows.None;
			light.enabled = false;

			remoteHandLights[steamId] = light;
			return light;
		}

		private void OnReceiveFlashlight(GameEventReader reader)
		{
			bool isOn = reader.ReadBoolean();
			ulong sender = reader.sender;
			if (sender == WreckMPGlobals.UserID) return;

			isNetworkApplying = true;
			try
			{
				cachedLightOn = isOn;
				remoteLightStates[sender] = isOn;

				if (flashlightLight != null)
				{
					flashlightLight.enabled = isOn;
				}
				if (flashlightFsm != null)
				{
					flashlightFsm.SendEvent(isOn ? "MP_ON" : "MP_OFF");
				}

				Player partner = WreckMPGlobals.Players.ContainsKey(sender) ? WreckMPGlobals.Players[sender] : null;
				if (partner != null)
				{
					Light handLight = GetOrCreateHandLight(partner);
					if (handLight != null)
					{
						handLight.enabled = isOn;
					}
				}

				Transform soundTf = (flashlightGo != null) ? flashlightGo.transform : transform;
				try
				{
					MasterAudio.PlaySound3DAndForget("HouseFoley", soundTf, true, 1f, null, 0f, "flashlight_switch");
				}
				catch {}
				ExtendedSyncDebugHUD.Log("<color=#ffcc00>IN [FLASHLIGHT]: Напарник переключил свет фонарика: " + (isOn ? "ВКЛ" : "ВЫКЛ") + "</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetFlashlightSync] Ошибка OnReceiveFlashlight: " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
}
