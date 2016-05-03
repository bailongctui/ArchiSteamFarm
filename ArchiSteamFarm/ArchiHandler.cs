﻿/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using SteamKit2;
using SteamKit2.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal sealed class ArchiHandler : ClientMsgHandler {
		private readonly Bot Bot;

		internal ArchiHandler(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException("bot");
			}

			Bot = bot;
		}

		/*
		  ____        _  _  _                   _
		 / ___| __ _ | || || |__    __ _   ___ | | __ ___
		| |    / _` || || || '_ \  / _` | / __|| |/ // __|
		| |___| (_| || || || |_) || (_| || (__ |   < \__ \
		 \____|\__,_||_||_||_.__/  \__,_| \___||_|\_\|___/

		*/

		internal sealed class NotificationsCallback : CallbackMsg {
			internal enum ENotification : byte {
				Unknown = 0,
				Trading = 1,
				// Only custom below, different than ones available as user_notification_type
				Items = 255
			}

			internal readonly HashSet<ENotification> Notifications;

			internal NotificationsCallback(JobID jobID, CMsgClientUserNotifications msg) {
				if (jobID == null || msg == null) {
					throw new ArgumentNullException("jobID || msg");
				}

				JobID = jobID;

				Notifications = new HashSet<ENotification>();
				foreach (var notification in msg.notifications) {
					Notifications.Add((ENotification) notification.user_notification_type);
				}
			}

			internal NotificationsCallback(JobID jobID, CMsgClientItemAnnouncements msg) {
				if (jobID == null || msg == null) {
					throw new ArgumentNullException("jobID || msg");
				}

				JobID = jobID;

				if (msg.count_new_items > 0) {
					Notifications = new HashSet<ENotification> {
						ENotification.Items
					};
				}
			}
		}

		internal sealed class OfflineMessageCallback : CallbackMsg {
			internal readonly uint OfflineMessagesCount;

			internal OfflineMessageCallback(JobID jobID, CMsgClientOfflineMessageNotification msg) {
				if (jobID == null || msg == null) {
					throw new ArgumentNullException("jobID || msg");
				}

				JobID = jobID;
				OfflineMessagesCount = msg.offline_messages;
			}
		}

		internal sealed class PurchaseResponseCallback : CallbackMsg {
			internal enum EPurchaseResult : sbyte {
				Unknown = -1,
				OK = 0,
				AlreadyOwned = 9,
				RegionLocked = 13,
				InvalidKey = 14,
				DuplicatedKey = 15,
				BaseGameRequired = 24,
				OnCooldown = 53
			}

			internal readonly EResult Result;
			internal readonly EPurchaseResult PurchaseResult;
			internal readonly KeyValue ReceiptInfo;
			internal readonly Dictionary<uint, string> Items;

			internal PurchaseResponseCallback(JobID jobID, CMsgClientPurchaseResponse msg) {
				if (jobID == null || msg == null) {
					throw new ArgumentNullException("jobID || msg");
				}

				JobID = jobID;
				Result = (EResult) msg.eresult;
				PurchaseResult = (EPurchaseResult) msg.purchase_result_details;

				if (msg.purchase_receipt_info == null) {
					return;
				}

				ReceiptInfo = new KeyValue();
				using (MemoryStream ms = new MemoryStream(msg.purchase_receipt_info)) {
					if (!ReceiptInfo.TryReadAsBinary(ms)) {
						return;
					}

					var lineItems = ReceiptInfo["lineitems"].Children;
					Items = new Dictionary<uint, string>(lineItems.Count);

					foreach (KeyValue lineItem in lineItems) {
						uint appID = (uint) lineItem["PackageID"].AsUnsignedLong();
						string gameName = lineItem["ItemDescription"].Value;
						gameName = WebUtility.HtmlDecode(gameName); // Apparently steam expects client to decode sent HTML
						Items[appID] = gameName;
					}
				}
			}
		}

		/*
		 __  __        _    _                 _
		|  \/  |  ___ | |_ | |__    ___    __| | ___
		| |\/| | / _ \| __|| '_ \  / _ \  / _` |/ __|
		| |  | ||  __/| |_ | | | || (_) || (_| |\__ \
		|_|  |_| \___| \__||_| |_| \___/  \__,_||___/

		*/

		internal void PlayGame(string gameName) {
			if (!Client.IsConnected) {
				return;
			}

			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
			if (!string.IsNullOrEmpty(gameName)) {
				request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
					game_extra_info = gameName,
					game_id = new GameID() {
						AppType = GameID.GameType.Shortcut,
						ModID = uint.MaxValue
					}
				});
			}

			Client.Send(request);
		}

		internal void PlayGames(uint gameID) {
			if (!Client.IsConnected) {
				return;
			}

			PlayGames(new HashSet<uint> { gameID });
		}

		internal void PlayGames(HashSet<uint> gameIDs) {
			if (gameIDs == null || !Client.IsConnected) {
				return;
			}

			var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
			foreach (uint gameID in gameIDs) {
				if (gameID == 0) {
					continue;
				}

				request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed {
					game_id = new GameID(gameID),
				});
			}

			Client.Send(request);
		}

		internal async Task<PurchaseResponseCallback> RedeemKey(string key) {
			if (string.IsNullOrEmpty(key) || !Client.IsConnected) {
				return null;
			}

			var request = new ClientMsgProtobuf<CMsgClientRegisterKey>(EMsg.ClientRegisterKey) {
				SourceJobID = Client.GetNextJobID()
			};

			request.Body.key = key;

			Client.Send(request);

			try {
				return await new AsyncJob<PurchaseResponseCallback>(Client, request.SourceJobID);
			} catch (Exception e) {
				Logging.LogGenericException(e, Bot.BotName);
				return null;
			}
		}

		// TODO: Please remove me immediately after https://github.com/SteamRE/SteamKit/issues/254 gets fixed
		internal void HackedLogOn(SteamUser.LogOnDetails details) {
			if (!Client.IsConnected) {
				return;
			}

			SteamID steamID = new SteamID(details.AccountID, details.AccountInstance, Client.ConnectedUniverse, EAccountType.Individual);

			var logon = new ClientMsgProtobuf<CMsgClientLogon>(EMsg.ClientLogon);
			logon.Body.obfustucated_private_ip = details.LoginID.Value;
			logon.ProtoHeader.client_sessionid = 0;
			logon.ProtoHeader.steamid = steamID.ConvertToUInt64();
			logon.Body.account_name = details.Username;
			logon.Body.password = details.Password;
			logon.Body.should_remember_password = details.ShouldRememberPassword;
			logon.Body.protocol_version = MsgClientLogon.CurrentProtocol;
			logon.Body.client_os_type = (uint) details.ClientOSType;
			logon.Body.client_language = details.ClientLanguage;
			logon.Body.cell_id = details.CellID;
			logon.Body.steam2_ticket_request = details.RequestSteam2Ticket;
			logon.Body.client_package_version = 1771;
			logon.Body.auth_code = details.AuthCode;
			logon.Body.two_factor_code = details.TwoFactorCode;
			logon.Body.login_key = details.LoginKey;
			logon.Body.sha_sentryfile = details.SentryFileHash;
			logon.Body.eresult_sentryfile = (int) (details.SentryFileHash != null ? EResult.OK : EResult.FileNotFound);

			Client.Send(logon);
		}

		/*
		 _   _                    _  _
		| | | |  __ _  _ __    __| || |  ___  _ __  ___
		| |_| | / _` || '_ \  / _` || | / _ \| '__|/ __|
		|  _  || (_| || | | || (_| || ||  __/| |   \__ \
		|_| |_| \__,_||_| |_| \__,_||_| \___||_|   |___/

		*/

		public override void HandleMsg(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			switch (packetMsg.MsgType) {
				case EMsg.ClientFSOfflineMessageNotification:
					HandleFSOfflineMessageNotification(packetMsg);
					break;
				case EMsg.ClientItemAnnouncements:
					HandleItemAnnouncements(packetMsg);
					break;
				case EMsg.ClientPurchaseResponse:
					HandlePurchaseResponse(packetMsg);
					break;
				case EMsg.ClientUserNotifications:
					HandleUserNotifications(packetMsg);
					break;
			}
		}

		private void HandleFSOfflineMessageNotification(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientOfflineMessageNotification>(packetMsg);
			Client.PostCallback(new OfflineMessageCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandleItemAnnouncements(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientItemAnnouncements>(packetMsg);
			Client.PostCallback(new NotificationsCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandlePurchaseResponse(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientPurchaseResponse>(packetMsg);
			Client.PostCallback(new PurchaseResponseCallback(packetMsg.TargetJobID, response.Body));
		}

		private void HandleUserNotifications(IPacketMsg packetMsg) {
			if (packetMsg == null) {
				return;
			}

			var response = new ClientMsgProtobuf<CMsgClientUserNotifications>(packetMsg);
			Client.PostCallback(new NotificationsCallback(packetMsg.TargetJobID, response.Body));
		}
	}
}