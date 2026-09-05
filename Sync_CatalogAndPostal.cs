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

		private static GameObject cachedOrderEnvelopeTemplate;
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
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка OnSceneReset: " + ex.Message);
			}

			if (Application.loadedLevelName == "GAME")
			{
				StartCoroutine(LazyHookCatalogAndPostal());
				StartCoroutine(ClearSceneResettingFlag());
			}
			else
			{
				isSceneResetting = false;
			}
		}

		private IEnumerator ClearSceneResettingFlag()
		{
			yield return new WaitForSeconds(3f);
			isSceneResetting = false;
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
				string[] scanParents = new string[] { "YARD", "STORE" };
				for (int p = 0; p < scanParents.Length; p++)
				{
					GameObject parentObj = GameObject.Find(scanParents[p]);
					if (parentObj != null)
					{
						PlayMakerArrayListProxy[] proxies = parentObj.GetComponentsInChildren<PlayMakerArrayListProxy>(true);
						for (int i = 0; i < proxies.Length; i++)
						{
							if (proxies[i] != null && (proxies[i].name == "OrderList" || proxies[i].name == "Magazine" || proxies[i].referenceName == "OrderList"))
							{
								cachedOrderList = proxies[i];
								return cachedOrderList;
							}
						}
					}
				}
				PlayMakerArrayListProxy[] allProxies = Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>();
				if (allProxies != null)
				{
					for (int j = 0; j < allProxies.Length; j++)
					{
						PlayMakerArrayListProxy p = allProxies[j];
						if (p != null && p.gameObject != null && p.gameObject.hideFlags == HideFlags.None)
						{
							if (p.gameObject.name == "OrderList" || p.gameObject.name == "Magazine" || p.referenceName == "OrderList")
							{
								cachedOrderList = p;
								return cachedOrderList;
							}
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
						if (gameObject == null)
						{
							string[] scanParents = new string[] { "YARD", "STORE" };
							for (int p = 0; p < scanParents.Length; p++)
							{
								GameObject parentObj = GameObject.Find(scanParents[p]);
								if (parentObj != null)
								{
									Transform[] children = parentObj.GetComponentsInChildren<Transform>(true);
									for (int c = 0; c < children.Length; c++)
									{
										if (children[c] != null && children[c].name == "ButtonOrder")
										{
											gameObject = children[c].gameObject;
											break;
										}
									}
								}
								if (gameObject != null) break;
							}
						}
						if (gameObject == null)
						{
							GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
							if (allObjects != null)
							{
								for (int o = 0; o < allObjects.Length; o++)
								{
									GameObject g = allObjects[o];
									if (g != null && g.hideFlags == HideFlags.None && g.name == "ButtonOrder")
									{
										gameObject = g;
										break;
									}
								}
							}
						}
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
						else if (lastOrderItems.Count > 0 && !postOrderPaidSent)
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
						string[] scanParents = new string[] { "YARD", "STORE" };
						for (int sp = 0; sp < scanParents.Length; sp++)
						{
							GameObject parentObj = GameObject.Find(scanParents[sp]);
							if (parentObj != null)
							{
								Transform[] componentsInChildren = parentObj.GetComponentsInChildren<Transform>(includeInactive: true);
								foreach (Transform transform in componentsInChildren)
								{
									if (transform != null && transform.name.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
									{
										cachedEnvelope = transform.gameObject;
										break;
									}
								}
							}
							if (cachedEnvelope != null) break;
						}
						if (cachedEnvelope == null)
						{
							GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
							if (allObjects != null)
							{
								for (int o = 0; o < allObjects.Length; o++)
								{
									GameObject g = allObjects[o];
									if (g != null && g.hideFlags == HideFlags.None && g.name.IndexOf("envelope", StringComparison.OrdinalIgnoreCase) >= 0)
									{
										if (g.transform.root != null && g.transform.root.name != "FPSPlayer")
										{
											cachedEnvelope = g;
											break;
										}
									}
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
						foreach (var r in postOrderBuyObj.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
						foreach (var c in postOrderBuyObj.GetComponentsInChildren<Collider>(true)) c.enabled = true;
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
				FileLogger.WriteLine("TX OrderPlaced: items=" + list.Count + " [" + string.Join(", ", list.ToArray()) + "] pos=" + pos.ToString("F2"), "INFO");
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
			FileLogger.WriteLine("RX OrderPlaced: items=" + (list.Count > 0 ? list.Count : count) + " [" + string.Join(", ", list.ToArray()) + "] pos=" + pos.ToString("F2"), "INFO");
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
				GameObject template = cachedOrderEnvelopeTemplate;
				GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
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

				if (template != null && env == null)
				{
					cachedOrderEnvelopeTemplate = template;
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

		public void RestorePostOrderBuyVisuals()
		{
			try
			{
				PlayMakerFSM payFsm;
				GameObject postOrderBuyObj = FindPostOrderBuy(out payFsm);
				if (postOrderBuyObj != null)
				{
					postOrderBuyObj.SetActive(true);
					foreach (var r in postOrderBuyObj.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
					foreach (var c in postOrderBuyObj.GetComponentsInChildren<Collider>(true)) c.enabled = true;
				}

				GameObject store = GameObject.Find("STORE");
				if (store != null)
				{
					Transform[] allTr = store.GetComponentsInChildren<Transform>(true);
					for (int k = 0; k < allTr.Length; k++)
					{
						if (allTr[k] != null && IsReceiptObject(allTr[k].name))
						{
							allTr[k].gameObject.SetActive(true);
							foreach (var r in allTr[k].GetComponentsInChildren<Renderer>(true)) r.enabled = true;
							foreach (var c in allTr[k].GetComponentsInChildren<Collider>(true)) c.enabled = true;
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] RestorePostOrderBuyVisuals error: " + ex.Message);
			}
		}

		public void BroadcastDeliveryReady()
		{
			if (isNetworkApplying || deliveryArrivedSent)
			{
				return;
			}
			deliveryArrivedSent = true;
			postOrderBuyWasActive = true;
			RestorePostOrderBuyVisuals();
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
				RestorePostOrderBuyVisuals();
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
			StartCoroutine(RegisterUnpackedBoxesCoroutine(new Vector3(-1551.5f, 4.5f, 1182.8f), isPayer: true));
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
				StartCoroutine(RegisterUnpackedBoxesCoroutine(new Vector3(-1551.5f, 4.5f, 1182.8f), isPayer: false));
			}
			finally
			{
				isNetworkApplying = false;
			}
		}

		private static GameObject cachedParcelBoxTemplate;
		public static GameObject FindParcelBoxTemplateInResources()
		{
			// Кеш: ассет переживает смену сцены, повторные полные сканы не нужны.
			if (cachedParcelBoxTemplate != null)
			{
				return cachedParcelBoxTemplate;
			}
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
							cachedParcelBoxTemplate = all[i];
							return cachedParcelBoxTemplate;
						}
					}
					for (int j = 0; j < all.Length; j++)
					{
						if (all[j] == null) continue;
						if (IsParcelBox(all[j].name))
						{
							cachedParcelBoxTemplate = all[j];
							return cachedParcelBoxTemplate;
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
			string clean = UniversalHandItemSync.GetCleanItemName(name);
			return clean.Equals("PostOrderBuy", StringComparison.OrdinalIgnoreCase) ||
			       clean.Equals("PostOrder", StringComparison.OrdinalIgnoreCase) ||
			       clean.Equals("Bill", StringComparison.OrdinalIgnoreCase);
		}

		public void CleanupAllPostOrderBuyObjects()
		{
			postOrderBuyWasActive = false;
			int count = 0;
			try
			{
				HashSet<int> processed = new HashSet<int>();
				GameObject store = GameObject.Find("STORE");
				if (store != null)
				{
					Transform[] allTr = store.GetComponentsInChildren<Transform>(true);
					for (int k = 0; k < allTr.Length; k++)
					{
						if (allTr[k] != null && IsReceiptObject(allTr[k].name) && processed.Add(allTr[k].gameObject.GetInstanceID()))
						{
							count++;
							DisablePostOrderBuyObject(allTr[k].gameObject);
						}
					}
				}

				GameObject[] sceneObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
				if (sceneObjects != null)
				{
					for (int i = 0; i < sceneObjects.Length; i++)
					{
						GameObject obj = sceneObjects[i];
						if (obj != null && obj.hideFlags == HideFlags.None && IsReceiptObject(obj.name) && processed.Add(obj.GetInstanceID()))
						{
							count++;
							DisablePostOrderBuyObject(obj);
						}
					}
				}
				FileLogger.WriteLine("cleanup: PostOrderBuy objects found: " + count, "INFO");
			}
			catch (Exception ex)
			{
				ModConsole.Error("[NetPartsDeliverySync] Ошибка CleanupAllPostOrderBuyObjects: " + ex.Message);
			}
		}

		private void DisablePostOrderBuyObject(GameObject obj)
		{
			if (obj == null || obj.hideFlags != HideFlags.None) return;
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
				FileLogger.WriteLine("TX CatalogUnbox: part=" + cleanPartName + " [#" + itemIndex + "] pos=" + pos.ToString("F2"), "INFO");
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
			FileLogger.WriteLine("RX CatalogUnbox: part=" + cleanPartName + " [#" + itemIndex + "] pos=" + pos.ToString("F2"), "INFO");
			isNetworkApplying = true;
			try
			{
				HandleCatalogPartUnbox(cleanPartName, itemIndex, pos, rot);
				DestroySpectatorBox(pos, cleanPartName, itemIndex);
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

		private void DestroySpectatorBox(Vector3 unboxPos, string cleanPart, int itemIndex)
		{
			GameObject[] all = UnityEngine.Object.FindObjectsOfType<GameObject>();
			for (int i = 0; i < all.Length; i++)
			{
				GameObject go = all[i];
				if (go == null) continue;
				if (!IsParcelBox(go.name) && (go.transform.root == null || !IsParcelBox(go.transform.root.name))) continue;
				if (Vector3.Distance(go.transform.position, unboxPos) > 3f) continue; // та же коробка
				
				// Пометить как обработанную, чтобы watcher зрителя не счёл это своим открытием
				ParcelUnboxTracker trk = go.GetComponent<ParcelUnboxTracker>();
				if (trk != null)
				{
					trk.WasTriggered = true; // ключевое: глушим его собственный unbox-broadcast
				}
				
				// Дёрнуть FSM, если игра умеет сама "открыть" — иначе просто скрыть
				PlayMakerFSM fsm = go.GetComponentInChildren<PlayMakerFSM>();
				if (fsm != null)
				{
					suppressedParcels.Add(go.GetInstanceID()); // защита от echo
					fsm.SendEvent("OPEN");
				}
				else
				{
					go.SetActive(false);
				}
				ExtendedSyncDebugHUD.Log("[PARTS] Коробка зрителя закрыта по сетевому unbox: " + go.name);
				break; // одну коробку за раз
			}
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
				FileLogger.WriteLine("RX ParcelUnbox: box=" + boxName + " part=" + clean + " pos=" + b.ToString("F2"), "INFO");
				HandleCatalogPartUnbox(clean, 0, b, Quaternion.identity);
				DestroySpectatorBox(b, clean, 0);
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
}
