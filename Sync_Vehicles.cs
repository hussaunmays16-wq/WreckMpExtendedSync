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
				if (Input.GetKeyDown(KeyCode.U))
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
			ExtendedSyncDebugHUD.Log("<color=#00ff00>[JONNEZ]</color> Вы сели на пассажирское место Jonnez [U для высадки]");
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
				GUI.Label(new Rect(Screen.width / 2 - 140, Screen.height - 69, 280f, 25f), "<color=#ffdd00><b>Пассажир Jonnez ES [U — слезть]</b></color>");
			}
		}
	}
	public class ExtendedVehiclesSync : MonoBehaviour
	{
		public static ExtendedVehiclesSync Instance;

		private void Awake()
		{
			Instance = this;
		}

		public void OnSceneReset()
		{
		}
	}

	public class GasStationAndFluidSync : MonoBehaviour
	{
		public static GasStationAndFluidSync Instance;

		private GameEvent gasStationNozzleEvent;
		private GameEvent fluidPourEvent;

		private bool isNetworkApplying;
		public bool IsNetworkApplying => isNetworkApplying;

		private class GasPumpTracker
		{
			public string PumpName;
			public GameObject PumpObject;
			public PlayMakerFSM MeterFsm;
			public float LastLiters;
			public float LastPrice;
			public bool LastIsPumping;
			public string LastTargetVehicle = "";
			public float NextScanTime;
		}

		private static readonly Dictionary<string, GasPumpTracker> ActivePumps = new Dictionary<string, GasPumpTracker>(StringComparer.OrdinalIgnoreCase);

		public static readonly string[] MonitoredPumpNames = new string[]
		{
			"gas pump 95",
			"gas pump diesel",
			"gas pump fuel oil"
		};

		public static readonly string[] FluidVehicleNames = new string[]
		{
			"SATSUMA(504kg, 330)",
			"SATSUMA(580kg, 240hp)",
			"HAYOSIKO(1500kg, 250)",
			"RCO_RUSCKO12(270)",
			"FERNDALE(1630kg)",
			"GIFU(750/450psi)",
			"KEKMET(350-400psi)",
			"JONNEZ ES(Clone)",
			"FLATBED"
		};

		private static GameObject FindVehicleDirect(string nameOrPrefix)
		{
			if (string.IsNullOrEmpty(nameOrPrefix)) return null;
			GameObject direct = GameObject.Find(nameOrPrefix);
			if (direct != null) return direct;
			for (int i = 0; i < FluidVehicleNames.Length; i++)
			{
				if (FluidVehicleNames[i].IndexOf(nameOrPrefix, StringComparison.OrdinalIgnoreCase) >= 0 ||
				    nameOrPrefix.IndexOf(FluidVehicleNames[i].Substring(0, Math.Min(6, FluidVehicleNames[i].Length)), StringComparison.OrdinalIgnoreCase) >= 0)
				{
					GameObject found = GameObject.Find(FluidVehicleNames[i]);
					if (found != null) return found;
				}
			}
			return null;
		}



		private float nextCanisterCheckTime;
		private float nextPumpCheckTime;
		private float lastPourBroadcastTime;
		private string lastPourItemId = "";

		private void Awake()
		{
			Instance = this;
		}

		private void Start()
		{
			gasStationNozzleEvent = new GameEvent("SyncGasStationNozzle", OnReceiveGasPump);
			fluidPourEvent = new GameEvent("SyncFluidPour", OnReceiveFluidPour);
			OnSceneReset();
		}

		public void OnSceneReset()
		{
			StopAllCoroutines();
			isNetworkApplying = false;
			ActivePumps.Clear();
			nextCanisterCheckTime = 0f;
			nextPumpCheckTime = 0f;
			lastPourBroadcastTime = 0f;
			lastPourItemId = "";
		}

		private void Update()
		{
			if (Application.loadedLevelName != "GAME") return;
			if (isNetworkApplying) return;

			UpdateGasPumps();
			UpdateCanistersPouring();
		}

		private void UpdateGasPumps()
		{
			if (Time.time < nextPumpCheckTime) return;
			nextPumpCheckTime = Time.time + 0.18f;

			for (int i = 0; i < MonitoredPumpNames.Length; i++)
			{
				string pName = MonitoredPumpNames[i];
				if (!ActivePumps.TryGetValue(pName, out GasPumpTracker tracker))
				{
					tracker = new GasPumpTracker { PumpName = pName };
					ActivePumps[pName] = tracker;
				}

				if (tracker.PumpObject == null || tracker.MeterFsm == null)
				{
					if (Time.time < tracker.NextScanTime) continue;
					tracker.NextScanTime = Time.time + 3.0f;

					tracker.PumpObject = GameObject.Find(pName);
					if (tracker.PumpObject != null)
					{
						PlayMakerFSM[] fsms = tracker.PumpObject.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int f = 0; f < fsms.Length; f++)
						{
							if (fsms[f].FsmVariables.FindFsmFloat("Liters") != null ||
							    fsms[f].FsmVariables.FindFsmFloat("Price") != null ||
							    fsms[f].FsmVariables.FindFsmFloat("LitersMeter") != null)
							{
								tracker.MeterFsm = fsms[f];
								break;
							}
						}
					}
				}

				if (tracker.MeterFsm == null) continue;

				FsmFloat lVar = tracker.MeterFsm.FsmVariables.FindFsmFloat("Liters") ?? tracker.MeterFsm.FsmVariables.FindFsmFloat("LitersMeter");
				FsmFloat pVar = tracker.MeterFsm.FsmVariables.FindFsmFloat("Price") ?? tracker.MeterFsm.FsmVariables.FindFsmFloat("PriceMeter");

				float currentLiters = (lVar != null) ? lVar.Value : 0f;
				float currentPrice = (pVar != null) ? pVar.Value : 0f;

				if (tracker.LastLiters < 0.001f && currentLiters > 0.001f)
				{
					tracker.LastLiters = currentLiters;
					tracker.LastPrice = currentPrice;
				}
				else if (Mathf.Abs(currentLiters - tracker.LastLiters) > 0.04f)
				{
					float delta = currentLiters - tracker.LastLiters;
					bool isPumping = delta > 0f;
					tracker.LastLiters = currentLiters;
					tracker.LastPrice = currentPrice;
					tracker.LastIsPumping = isPumping;

					string targetVeh = FindVehicleNearNozzle(tracker.PumpObject.transform.position, 5.0f);
					tracker.LastTargetVehicle = targetVeh;

					BroadcastGasPump(pName, isPumping, currentLiters, currentPrice, targetVeh, delta);
				}
			}
		}

		private string FindVehicleNearNozzle(Vector3 nozzlePos, float radius)
		{
			for (int i = 0; i < FluidVehicleNames.Length; i++)
			{
				string vName = FluidVehicleNames[i];
				GameObject vObj = GameObject.Find(vName);
				if (vObj != null && Vector3.Distance(nozzlePos, vObj.transform.position) <= radius)
				{
					return vName;
				}
			}
			return "";
		}

		public void BroadcastGasPump(string pumpName, bool isPumping, float liters, float price, string targetVehicle, float deltaLiters)
		{
			if (isNetworkApplying) return;

			using (GameEventWriter writer = gasStationNozzleEvent.Writer())
			{
				writer.Write(pumpName ?? "");
				writer.Write(isPumping);
				writer.Write(liters);
				writer.Write(price);
				writer.Write(targetVehicle ?? "");
				writer.Write(deltaLiters);
				gasStationNozzleEvent.Send(writer, 0uL, safe: true);
			}

			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [АЗС]: " + pumpName + " -> " + (isPumping ? "ЗАПРАВКА " : "СТОП ") + (string.IsNullOrEmpty(targetVehicle) ? "" : ("(" + targetVehicle + ") ")) + liters.ToString("F1") + " л (" + price.ToString("F1") + " mk)</color>");
		}

		private void OnReceiveGasPump(GameEventReader reader)
		{
			string pumpName = reader.ReadString();
			bool isPumping = reader.ReadBoolean();
			float totalLiters = reader.ReadSingle();
			float price = reader.ReadSingle();
			string targetVehicle = reader.ReadString();
			float deltaLiters = 0f;
			try { deltaLiters = reader.ReadSingle(); } catch { }

			isNetworkApplying = true;
			try
			{
				GameObject pump = GameObject.Find(pumpName);
				if (pump != null)
				{
					PlayMakerFSM[] fsms = pump.GetComponentsInChildren<PlayMakerFSM>(true);
					for (int i = 0; i < fsms.Length; i++)
					{
						FsmFloat l = fsms[i].FsmVariables.FindFsmFloat("Liters") ?? fsms[i].FsmVariables.FindFsmFloat("LitersMeter");
						if (l != null) l.Value = totalLiters;
						FsmFloat p = fsms[i].FsmVariables.FindFsmFloat("Price") ?? fsms[i].FsmVariables.FindFsmFloat("PriceMeter");
						if (p != null) p.Value = price;
					}
				}

				if (!string.IsNullOrEmpty(targetVehicle) && deltaLiters > 0.001f)
				{
					GameObject veh = FindVehicleDirect(targetVehicle);
					if (veh != null)
					{
						PlayMakerFSM[] vFsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int j = 0; j < vFsms.Length; j++)
						{
							FsmFloat fl = vFsms[j].FsmVariables.FindFsmFloat("FuelLevel");
							if (fl != null)
							{
								fl.Value = Mathf.Clamp(fl.Value + deltaLiters, 0f, 120f);
								break;
							}
						}
					}
				}

				ExtendedSyncDebugHUD.Log("<color=#aaff00>IN [АЗС]: " + pumpName + " -> " + (isPumping ? "ЗАПРАВКА " : "СТОП ") + (string.IsNullOrEmpty(targetVehicle) ? "" : ("(" + targetVehicle + ") ")) + totalLiters.ToString("F1") + " л (" + price.ToString("F1") + " mk)</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[GasPump Receive Error] " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private void UpdateCanistersPouring()
		{
			if (Time.time < nextCanisterCheckTime) return;
			nextCanisterCheckTime = Time.time + 0.22f;

			GameObject held = UniversalHandItemSync.Instance?.GetLocallyHeldItem();
			if (held == null) return;

			string hName = held.name.ToLower();
			string fluidType = null;
			string fillTarget = "";

			if (hName.Contains("motor oil") || hName.Contains("oil"))
			{
				fluidType = "oil";
				fillTarget = "engine";
			}
			else if (hName.Contains("coolant"))
			{
				fluidType = "coolant";
				fillTarget = "radiator";
			}
			else if (hName.Contains("brake fluid") || hName.Contains("brakefluid"))
			{
				fluidType = "brake_fluid";
				fillTarget = "brakes";
			}
			else if (hName.Contains("two stroke") || hName.Contains("two-stroke") || hName.Contains("twostroke"))
			{
				fluidType = "fuel_twostroke";
				fillTarget = "tank";
			}
			else if (hName.Contains("jerrycan") || hName.Contains("gasoline") || hName.Contains("diesel"))
			{
				fluidType = hName.Contains("diesel") ? "fuel_diesel" : "fuel_gasoline";
				fillTarget = "tank";
			}

			if (fluidType == null) return;

			bool isTilted = (held.transform.up.y < -0.12f || held.transform.forward.y < -0.3f);
			
			PlayMakerFSM[] fsmList = held.GetComponentsInChildren<PlayMakerFSM>(true);
			bool fsmPouring = false;
			float remainingFluid = 100f;

			for (int i = 0; i < fsmList.Length; i++)
			{
				if (fsmList[i] == null) continue;
				string st = (fsmList[i].ActiveStateName ?? "").ToLower();
				if (st.Contains("pour") || st.Contains("use") || st.Contains("drain") || st.Contains("flow"))
				{
					fsmPouring = true;
				}
				FsmFloat flVar = fsmList[i].FsmVariables.FindFsmFloat("Fluid") ?? fsmList[i].FsmVariables.FindFsmFloat("Volume") ?? fsmList[i].FsmVariables.FindFsmFloat("Fuel") ?? fsmList[i].FsmVariables.FindFsmFloat("Liters");
				if (flVar != null)
				{
					remainingFluid = flVar.Value;
				}
			}

			bool isCurrentlyPouring = (isTilted || fsmPouring) && (remainingFluid > 0.01f);

			if (isCurrentlyPouring)
			{
				string targetVehicle = "";
				for (int v = 0; v < FluidVehicleNames.Length; v++)
				{
					string vn = FluidVehicleNames[v];
					GameObject vObj = GameObject.Find(vn);
					if (vObj != null && Vector3.Distance(held.transform.position, vObj.transform.position) <= 2.8f)
					{
						targetVehicle = vn;
						break;
					}
				}

				if (Time.time - lastPourBroadcastTime >= 0.35f || lastPourItemId != held.name)
				{
					lastPourBroadcastTime = Time.time;
					lastPourItemId = held.name;
					float amountAdded = 0.08f;
					BroadcastFluidPour(held.name, fluidType, true, remainingFluid, targetVehicle, fillTarget, amountAdded);
				}
			}
		}

		public void BroadcastFluidPour(string itemId, string itemType, bool isPouring, float remainingAmount, string targetVehicle, string fillTarget, float amountAdded)
		{
			if (isNetworkApplying) return;

			using (GameEventWriter writer = fluidPourEvent.Writer())
			{
				writer.Write(itemId ?? "");
				writer.Write(itemType ?? "");
				writer.Write(isPouring);
				writer.Write(remainingAmount);
				writer.Write(targetVehicle ?? "");
				writer.Write(fillTarget ?? "");
				writer.Write(amountAdded);
				fluidPourEvent.Send(writer, 0uL, safe: true);
			}

			ExtendedSyncDebugHUD.Log("<color=#00ffcc>OUT [ЖИДКОСТЬ]: " + itemType + " (" + (string.IsNullOrEmpty(targetVehicle) ? "на землю" : targetVehicle) + ") +" + amountAdded.ToString("F2") + " л</color>");
		}

		private void OnReceiveFluidPour(GameEventReader reader)
		{
			string itemId = reader.ReadString();
			string itemType = reader.ReadString();
			bool isPouring = reader.ReadBoolean();
			float remainingAmount = reader.ReadSingle();
			string targetVehicle = reader.ReadString();
			string fillTarget = reader.ReadString();
			float amountAdded = reader.ReadSingle();

			isNetworkApplying = true;
			try
			{
				if (!string.IsNullOrEmpty(targetVehicle) && amountAdded > 0.001f)
				{
					GameObject veh = FindVehicleDirect(targetVehicle);
					if (veh != null)
					{
						PlayMakerFSM[] vFsms = veh.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int i = 0; i < vFsms.Length; i++)
						{
							if (vFsms[i] == null) continue;
							if (itemType == "oil")
							{
								FsmFloat oil = vFsms[i].FsmVariables.FindFsmFloat("OilLevel");
								if (oil != null) oil.Value = Mathf.Clamp(oil.Value + amountAdded * 10f, 0f, 100f);
							}
							else if (itemType == "coolant")
							{
								FsmFloat col = vFsms[i].FsmVariables.FindFsmFloat("Coolant");
								if (col != null) col.Value = Mathf.Clamp(col.Value + amountAdded * 10f, 0f, 100f);
							}
							else if (itemType == "brake_fluid")
							{
								FsmFloat fld = vFsms[i].FsmVariables.FindFsmFloat("Fluid");
								if (fld != null) fld.Value = Mathf.Clamp(fld.Value + amountAdded * 15f, 0f, 100f);
							}
							else if (itemType.StartsWith("fuel"))
							{
								FsmFloat fl = vFsms[i].FsmVariables.FindFsmFloat("FuelLevel");
								if (fl != null) fl.Value = Mathf.Clamp(fl.Value + amountAdded, 0f, 120f);
							}
						}
					}
				}

				if (!string.IsNullOrEmpty(itemId))
				{
					GameObject cObj = GameObject.Find(itemId);
					if (cObj != null)
					{
						PlayMakerFSM[] cFsms = cObj.GetComponentsInChildren<PlayMakerFSM>(true);
						for (int k = 0; k < cFsms.Length; k++)
						{
							FsmFloat rVar = cFsms[k].FsmVariables.FindFsmFloat("Fluid") ?? cFsms[k].FsmVariables.FindFsmFloat("Volume") ?? cFsms[k].FsmVariables.FindFsmFloat("Fuel") ?? cFsms[k].FsmVariables.FindFsmFloat("Liters");
							if (rVar != null) rVar.Value = remainingAmount;
						}

						ParticleSystem ps = cObj.GetComponentInChildren<ParticleSystem>();
						if (ps != null)
						{
							if (isPouring && !ps.isPlaying) ps.Play();
							else if (!isPouring && ps.isPlaying) ps.Stop();
						}
					}
				}

				ExtendedSyncDebugHUD.Log("<color=#aaff00>IN [ЖИДКОСТЬ]: " + itemType + " (" + (string.IsNullOrEmpty(targetVehicle) ? "на землю" : targetVehicle) + ") +" + amountAdded.ToString("F2") + " л (ост. " + remainingAmount.ToString("F1") + ")</color>");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[FluidPour Receive Error] " + ex.Message);
			}
			finally
			{
				isNetworkApplying = false;
			}
		}
	}
}
