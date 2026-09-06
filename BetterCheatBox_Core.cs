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
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Steamworks;
using UnityEngine;
using WreckMP;

namespace WreckMPExtendedSync
{
	public class SafeFsmWatcher : MonoBehaviour
	{
		private class Subscription
		{
			public PlayMakerFSM fsm;
			public string[] stateNames;
			public Action callback;
			public string previousState = "";
		}

		private readonly List<Subscription> subscriptions = new List<Subscription>();

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
			// ОДИН watcher на GameObject; подписки хранятся списком — вторая система,
			// зацепившаяся за тот же FSM, БОЛЬШЕ НЕ перезаписывает колбэк первой.
			SafeFsmWatcher watcher = fsm.gameObject.GetComponent<SafeFsmWatcher>();
			if (watcher == null)
			{
				watcher = fsm.gameObject.AddComponent<SafeFsmWatcher>();
			}
			// Дедупликация: тот же FSM + эквивалентный колбэк → обновляем фильтр состояний,
			// не плодим вторую подписку.
			for (int i = 0; i < watcher.subscriptions.Count; i++)
			{
				if (watcher.subscriptions[i].fsm == fsm &&
					object.Equals(watcher.subscriptions[i].callback, callback))
				{
					watcher.subscriptions[i].stateNames = stateNames;
					return watcher;
				}
			}
			Subscription subscription = new Subscription
			{
				fsm = fsm,
				stateNames = stateNames,
				callback = callback
			};
			subscription.previousState = ((fsm.Fsm != null && !string.IsNullOrEmpty(fsm.ActiveStateName)) ? fsm.ActiveStateName : "");
			watcher.subscriptions.Add(subscription);
			return watcher;
		}

		private void Update()
		{
			for (int s = 0; s < subscriptions.Count; s++)
			{
				Subscription sub = subscriptions[s];
				if (sub.fsm == null || sub.fsm.Fsm == null)
				{
					continue; // FSM уничтожен — подписка остаётся безвредно висеть
				}
				string activeStateName = sub.fsm.ActiveStateName;
				if (string.IsNullOrEmpty(activeStateName) || activeStateName == sub.previousState)
				{
					continue;
				}
				bool fired = false;
				if (sub.stateNames == null || sub.stateNames.Length == 0)
				{
					fired = true;
				}
				else
				{
					for (int i = 0; i < sub.stateNames.Length; i++)
					{
						string text = sub.stateNames[i];
						if (string.IsNullOrEmpty(text))
						{
							continue;
						}
						bool flag = (text.Length <= 2)
							? string.Equals(activeStateName, text, StringComparison.OrdinalIgnoreCase)
							: (string.Equals(activeStateName, text, StringComparison.OrdinalIgnoreCase) ||
							   activeStateName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
						if (!flag)
						{
							continue;
						}
						fired = true;
						break;
					}
				}
				if (fired)
				{
					if (SuppressNextEnter)
					{
						SuppressNextEnter = false;
					}
					else
					{
						try
						{
							sub.callback?.Invoke();
						}
						catch (Exception ex)
						{
							ModConsole.Error("[SafeFsmWatcher] Ошибка в колбэке FSM: " + ex.Message);
						}
					}
				}
				sub.previousState = activeStateName;
			}
		}
	}

	public class WreckMPExtendedSync : Mod
	{
		public override string ID => "WreckMPExtendedSync";

		public override string Name => "WreckMP Extended Sync (True Co-op Engine)";

		public override string Author => "WreckMP Community";

		public override string Version => "3.9.8";

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
			gameObject.AddComponent<GasStationAndFluidSync>();
			gameObject.AddComponent<ExtendedEconomySync>();
			gameObject.AddComponent<NetJoukoStorylineManager>();
			gameObject.AddComponent<NetMinigamesSlotManager>();
			gameObject.AddComponent<NetPartsDeliverySync>();
			gameObject.AddComponent<BetterCheatBoxSyncManager>();
			var manager = gameObject.AddComponent<CheatSpawnedItemSync>();
			manager.IsManager = true;
			CheatSpawnedItemSync.Instance = manager;
			gameObject.AddComponent<NetTelephoneHardwareSync>();
			gameObject.AddComponent<NetUrinationSync>();
			gameObject.AddComponent<NetFlashlightSync>();
			gameObject.AddComponent<UniversalHandItemSync>();
			gameObject.AddComponent<InGameDashboardGUI>();
			try
			{
				HarmonyInstance harmonyInstance = HarmonyInstance.Create("com.wreckmp.extendedsync.lobbyguard");
				MethodInfo methodInfo = typeof(GameScene).Assembly.GetType("WreckMP.SteamNet")?.GetMethod("OnLobbyMemberStateUpdate", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				MethodInfo method = typeof(LobbyDisconnectionGuard).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
				if (methodInfo != null && method != null)
				{
					harmonyInstance.Patch(methodInfo, new HarmonyMethod(method));
					ModConsole.Print("<color=green>[LobbyGuard]</color> Защита лобби от ложного самоотключения эмулятора активна!");
				}
				else
				{
					ModConsole.Error("[LobbyGuard] Метод OnLobbyMemberStateUpdate или Prefix не найден! Защита лобби НЕ установлена.");
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[LobbyGuard Error] " + ex.Message);
			}
			try
			{
				HarmonyInstance errorLogHarmony = HarmonyInstance.Create("com.wreckmp.extendedsync.errorlog");
				int patched = 0;
				foreach (MethodInfo m in typeof(MSCLoader.ModConsole).GetMethods(BindingFlags.Static | BindingFlags.Public))
				{
					if (m.Name == "Error")
					{
						ParameterInfo[] ps = m.GetParameters();
						if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
						{
							MethodInfo prefix = typeof(ErrorLogPatch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public);
							errorLogHarmony.Patch(m, new HarmonyMethod(prefix));
							patched++;
						}
					}
				}
				FileLogger.WriteLine(patched > 0
					? "ModConsole.Error interception: ENABLED (" + patched + " method(s))"
					: "ModConsole.Error interception: NOT FOUND — сигнатура ModConsole.Error не совпала", "INFO");
				ModConsole.Print("<color=green>[FileLogger]</color> Лог-файл: WreckMPExtendedSync.log в корне игры");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[FileLogger] Error patch: " + ex.Message);
				FileLogger.WriteLine("ModConsole.Error interception: FAILED — " + ex.Message, "ERROR");
			}
			try
			{
				HarmonyInstance postalHarmony = HarmonyInstance.Create("com.wreckmp.extendedsync.postal");
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
			ModConsole.Print("<color=green>[WreckMP Extended Sync v3.9.8]</color> Ядро синхронизации успешно запущено (Режим честного P2P)!");
		}
	}
	public static class LobbyDisconnectionGuard
	{
		[HarmonyPrefix]
		public static bool Prefix([HarmonyArgument(0)] LobbyChatUpdate_t param)
		{
			try
			{
				if (WreckMPGlobals.IsHost && param.m_ulSteamIDUserChanged == WreckMPGlobals.HostID)
				{
					ExtendedSyncDebugHUD.Log("<color=yellow>[LOBBY GUARD]</color> Заблокировано ложное отключение хоста эмулятором (Код: " + param.m_rgfChatMemberStateChange + ")!");
					return false;
				}
			}
			catch { }
			return true;
		}
	}

	public static class ErrorLogPatch
	{
		// Индексный биндинг: имя параметра в MSCLoader может отличаться (error/msg) —
		// Harmony 1.2 биндит по имени, индекс отвязывает от имени.
		[HarmonyPrefix]
		public static bool Prefix([HarmonyArgument(0)] string message)
		{
			FileLogger.WriteLine(message, "ERROR");
			return true; // оригинальный ModConsole.Error выполняется как обычно
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
			string loadedLevelName = Application.loadedLevelName;
			if (loadedLevelName != lastScene)
			{
				lastScene = loadedLevelName;
				OnSceneChanged(loadedLevelName);
			}
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
				GasStationAndFluidSync.Instance?.OnSceneReset();
			}
		}
	}
	public static class FileLogger
	{
		private static readonly object writeLock = new object();
		private static StreamWriter writer;
		private static float lastFlushTime;
		private static bool userLineWritten;
		private const float FlushInterval = 5f;
		private const long MaxLogSizeBytes = 5242880L; // 5 МБ

		private static readonly System.Text.RegularExpressions.Regex RichTextRegex =
			new System.Text.RegularExpressions.Regex("<[^>]+>");

		public static void Init()
		{
			try
			{
				// Корень игры (рядом с exe) каждого инстанса автоматически:
				string logPath = System.IO.Path.GetFullPath(
					System.IO.Path.Combine(System.IO.Path.Combine(Application.dataPath, ".."), "WreckMPExtendedSync.log"));

				// Ротация: файл > 5 МБ переименовывается в .old (перезапись старого .old)
				if (System.IO.File.Exists(logPath))
				{
					System.IO.FileInfo fi = new System.IO.FileInfo(logPath);
					if (fi.Length > MaxLogSizeBytes)
					{
						string oldPath = logPath + ".old";
						if (System.IO.File.Exists(oldPath))
						{
							System.IO.File.Delete(oldPath);
						}
						System.IO.File.Move(logPath, oldPath);
					}
				}

				writer = new StreamWriter(logPath, true, Encoding.UTF8);
				writer.AutoFlush = false;
				lastFlushTime = 0f;
				userLineWritten = false;
				WriteLine("==================================================", "INFO");
				WriteLine("SESSION START v3.9.8 | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "INFO");
				WriteLine("==================================================", "INFO");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[FileLogger] Init error: " + ex.Message);
				writer = null;
			}
		}

		public static void WriteLine(string message, string level)
		{
			if (writer == null || message == null) return;
			try
			{
				string clean = RichTextRegex.Replace(message, ""); // strip <color=...> и пр.
				lock (writeLock)
				{
					writer.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + level + "] " + clean);
				}
			}
			catch { }
		}

		// Вызывать из Update менеджера: периодический flush + пометка пользователя WreckMP
		public static void Tick()
		{
			if (writer == null) return;
			try
			{
				if (!userLineWritten && WreckMPGlobals.UserID != 0uL)
				{
					userLineWritten = true;
					WriteLine("USER ID: " + WreckMPGlobals.UserID + " | Scene: " + Application.loadedLevelName, "INFO");
				}
			}
			catch { }
			if (Time.time - lastFlushTime > FlushInterval)
			{
				lastFlushTime = Time.time;
				Flush();
			}
		}

		public static void Flush()
		{
			if (writer == null) return;
			lock (writeLock)
			{
				try { writer.Flush(); } catch { }
			}
		}

		public static void Close(string reason)
		{
			if (writer == null) return;
			try
			{
				WriteLine("SESSION END (" + reason + ") | " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), "INFO");
				Flush();
				lock (writeLock)
				{
					try { writer.Close(); } catch { }
					writer = null;
				}
			}
			catch { }
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
			FileLogger.Init();
			Instance = this;
		}

		public static void Log(string message)
		{
			try
			{
				FileLogger.WriteLine(message, "INFO");
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
			FileLogger.Tick();
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

		private void OnApplicationQuit()
		{
			FileLogger.Close("quit");
		}

		private void OnDestroy()
		{
			FileLogger.Close("destroy");
		}

		private void OnGUI()
		{
			try
			{
				if (Application.loadedLevelName != "GAME" || logs.Count <= 0)
				{
					return;
				}
				GUI.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
				GUI.color = Color.white;
				GUILayout.BeginArea(new Rect(Screen.width / 2 - 280, Screen.height - 225, 560f, 215f));
				GUILayout.BeginVertical("box");
				GUILayout.Label("<color=#00ffcc><b>★ WRECKMP EXTENDED NETWORK SYNC v3.9.8 ★</b></color>");
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
	public class NetJoukoStorylineManager : MonoBehaviour
	{
		public static NetJoukoStorylineManager Instance;

		private GameEvent joukoSuitcaseEvent;

		private bool isNetworkApplying;

		private bool isSuitcaseHooked;

		private bool hasBeenClaimed;

		private GameObject cachedSuitcase;

		private bool suitcaseWasFoundOnce;

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
			cachedSuitcase = null;
			hasBeenClaimed = false;
			suitcaseWasFoundOnce = false;
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
					suitcaseWasFoundOnce = true;
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
			if (cachedSuitcase == null && suitcaseWasFoundOnce && !hasBeenClaimed && !isNetworkApplying)
			{
				// Игра уничтожила (Destroy) чемодан вместо SetActive(false) —
				// кейс сюжетного продвижения. Считаем взятым.
				BroadcastSuitcaseTaken();
				return;
			}
			if (cachedSuitcase != null && !cachedSuitcase.activeInHierarchy)
			{
				BroadcastSuitcaseTaken();
				return;
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

		public static bool isNetworkApplying;

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
								FsmFloat paymentVar = fsm.FsmVariables.FindFsmFloat("Payment") ?? fsm.FsmVariables.FindFsmFloat("Money");
								float marks = paymentVar?.Value ?? 170f;
								if (paymentVar == null)
								{
									// Fallback-сумма: ищем шире перед тем как слать 170
									paymentVar = fsm.FsmVariables.FindFsmFloat("Price") ?? fsm.FsmVariables.FindFsmFloat("Sum");
									if (paymentVar != null) marks = paymentVar.Value;
									else ModConsole.Error("[KILJU] Переменная оплаты не найдена в FSM — отправлена fallback-сумма 170 MK!");
								}
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
					BetterCheatBoxSyncManager.lastNetworkMoneyApplyTime = Time.time;
					BetterCheatBoxSyncManager.Instance?.ApplyMoneyLocal(fsmFloat.Value);
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
					BetterCheatBoxSyncManager.lastNetworkMoneyApplyTime = Time.time;
					BetterCheatBoxSyncManager.Instance?.ApplyMoneyLocal(fsmFloat.Value);
				}
				GameObject gameObject = GameObject.Find("STORE") ?? GameObject.Find("Store");
				if (gameObject != null)
				{
					PlayMakerFSM[] componentsInChildren = gameObject.GetComponentsInChildren<PlayMakerFSM>(true);
					for (int i = 0; i < componentsInChildren.Length; i++)
					{
						string goName = componentsInChildren[i].gameObject.name;
						string fName = componentsInChildren[i].FsmName ?? "";
						if (goName.IndexOf("CashRegister", StringComparison.OrdinalIgnoreCase) >= 0 ||
						    goName.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0 ||
						    fName.IndexOf("CashRegister", StringComparison.OrdinalIgnoreCase) >= 0 ||
						    fName.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0)
						{
							componentsInChildren[i].SendEvent("PURCHASE");
						}
					}
				}
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
	public class BetterCheatBoxSyncManager : MonoBehaviour
	{
		public static BetterCheatBoxSyncManager Instance;


		public bool isNetworkApplying;
		public static bool isNetworkApplyingMoney;
		public static float lastNetworkMoneyApplyTime;

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
					// PlayMakerFSM.SendEvent объединён в PostalChainPatches.SendEvent_Prefix для экономии CPU
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
			// Общий кооп-кошелёк: деньги синхронизируются при ЛЮБОМ изменении, не только при
			// открытом чит-боксе (guiShow). Топливо, штрафы, покупки — всё уходит в сеть.
			// Защита от ping-pong: ApplyMoneyLocal пишет cachedMoney ДО BroadcastMoney,
			// поэтому эхо от сетевого применения не ретранслируется; additional-гарды
			// (lastNetworkMoneyApplyTime / ExtendedEconomySync.isNetworkApplying /
			// isNetworkApplyingMoney) гасят гонку специфичных событий и общего watcher'а.
			if (betterCheatBox.money != null)
			{
				float value = betterCheatBox.money.Value;
				if (Math.Abs(value - cachedMoney) > 0.05f)
				{
					if (Time.time - lastNetworkMoneyApplyTime < 0.6f ||
						ExtendedEconomySync.isNetworkApplying ||
						isNetworkApplyingMoney)
					{
						cachedMoney = value; // эхо сетевого применения — молча глотаем
					}
					else
					{
						cachedMoney = value;
						BroadcastMoney(value);
					}
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
			if (isNetworkApplying || isNetworkApplyingMoney || ExtendedEconomySync.isNetworkApplying)
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
			isNetworkApplyingMoney = true;
			lastNetworkMoneyApplyTime = Time.time;
			try
			{
				ApplyMoneyLocal(num);
			}
			finally
			{
				isNetworkApplying = false;
				isNetworkApplyingMoney = false;
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
					if (!isNetworkApplying)
					{
						PlayMakerFSM playMakerFSM4 = ((betterCheatBox != null && betterCheatBox.orderFsm != null) ? betterCheatBox.orderFsm : GameObject.Find("Sheets/OrderList/Timer")?.GetComponent<PlayMakerFSM>());
						if (playMakerFSM4 != null)
						{
							playMakerFSM4.SendEvent("FINISHED");
						}
						try { NetPartsDeliverySync.Instance?.BroadcastDeliveryReady(); } catch {}
					}
				}
				finally
				{
					suppressSkipPostOrder = false;
					// Держим временное окно и после выхода: watcher увидит свежий lastPostOrderSkipTime
					// и не ретранслирует FINISHED, пришедший от нашего же SendEvent.
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
			GameObject gameObject = GameObject.Find("SATSUMA(504kg, 330)") ?? GameObject.Find("SATSUMA(580kg, 240hp)") ?? GameObject.Find("SATSUMA(557kg, 248)") ?? GameObject.Find("SATSUMA");
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

		public static ulong GetOwnedRigidbodyOwner(OwnedRigidbody orb)
		{
			if (orb == null) return 0uL;
			try
			{
				if (orbOwnerProp != null)
				{
					object val = orbOwnerProp.GetValue(orb, null);
					if (val != null) return Convert.ToUInt64(val);
				}
				if (orbOwnerField != null)
				{
					object val = orbOwnerField.GetValue(orb);
					if (val != null) return Convert.ToUInt64(val);
				}
			}
			catch { }
			return 0uL;
		}

		public static bool IsLocalOwnerOf(Rigidbody rb)
		{
			if (rb == null) return true;
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
					ulong owner = GetOwnedRigidbodyOwner(orb);
					if (owner != 0uL)
					{
						return owner == WreckMPGlobals.UserID;
					}
				}
			}
			catch { }
			return true;
		}

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
			// ФИКС rubberband: запись cachedPosition/cachedEulerAngles на стороне, НЕ владеющей
			// rigidbody, "заряжает" точку отката для WreckMP — при возврате владения машину
			// телепортирует назад. Пишем кеш ТОЛЬКО если локальный игрок — владелец.
			try
			{
				Rigidbody ownRb = go.GetComponent<Rigidbody>();
				if (ownRb != null && !IsLocalOwnerOf(ownRb))
				{
					return; // не владелец — не трогаем внутренний кеш WreckMP
				}
			}
			catch { }
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

	}
	public class InGameDashboardGUI : MonoBehaviour
	{
		public static InGameDashboardGUI Instance;

		public bool isVisible;

		private Rect windowRect = new Rect(40f, 60f, 580f, 500f);

		private int selectedTab;

		private readonly string[] tabs = new string[5] { "Статус P2P", "Jonnez Пассажир", "Сюжет и Экономика", "Почта и Детали", "Better Cheat Box" };

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
					instance4?.BroadcastMoneyAdd(50000f);
				}
				if (GUILayout.Button("+500,000 MK", GUILayout.Height(30f)))
				{
					instance4?.BroadcastMoneyAdd(500000f);
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
			else if (selectedTab == 3)
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
			else if (selectedTab == 4)
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
			GUILayout.Label("<color=#ffdd00><b>2. СЮЖЕТ И СКИПЫ ТАЙМЕРОВ:</b></color>");
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
			GUILayout.Label("<color=#ffdd00><b>3. ДЕНЬГИ, ПОТРЕБНОСТИ И КЛЮЧИ:</b></color>");
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
			GUILayout.Label("<color=#ffdd00><b>4. ШИНЫ И ДОМ:</b></color>");
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
			GUILayout.Label("<color=#ffdd00><b>5. ФИЗИКА И ТОПЛИВО:</b></color>");
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
				Component[] comps = streamObj.GetComponentsInChildren<Component>(true);
				for (int cIdx = 0; cIdx < comps.Length; cIdx++)
				{
					Component c = comps[cIdx];
					if (c == null || c is Transform || c is ParticleSystem || c is ParticleSystemRenderer || c is AudioSource) continue;
					UnityEngine.Object.Destroy(c);
				}

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
