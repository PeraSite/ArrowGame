using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using ArrowGame.Client.UI;
using ArrowGame.Common;
using ArrowGame.Common.Extensions;
using ArrowGame.Common.Packets.Client;
using ArrowGame.Common.Packets.Server;
using UnityEngine;

namespace ArrowGame.Client {
	public class NetworkManager : MonoBehaviour {
		public static NetworkManager Instance { get; private set; }
		public event Action<IPacket> OnPacketReceived;

		// TCP 통신 오브젝트
		private TcpClient _client;
		private NetworkStream _stream;
		private BinaryReader _reader;
		private BinaryWriter _writer;
		private ConcurrentQueue<IPacket> _packetQueue;

		public RoomState RoomState { get; private set; }

		[Header("로컬 플레이어")]
		[SerializeField] private UnityInputProvider _localInputProvider;
		[SerializeField] private HealthDisplay _localHealthDisplay;
		[SerializeField] private PlayerIdDisplay _localIdDisplay;
		private InputState _lastState;
		private const int NOT_ASSIGNED_ID = -999;
		private int _localPlayerID;

		[Header("타 플레이어")]
		[SerializeField] private GameObject _replicatedCharacterPrefab;
		[SerializeField] private Vector2 _spawnLocation;
		private Dictionary<int, GameObject> _replicatedCharacters;

		[Header("화살")]
		[SerializeField] private Arrow _arrowPrefab;
		[SerializeField] private float _arrowSpawnY;

		[Header("엔딩")]
		[SerializeField] private EndingPanel _endingPanel;


#region Unity Lifecycle
		private void Awake() {
			// Singleton 로직
			if (Instance != null && Instance != this) {
				Destroy(gameObject);
				return;
			}
			Instance = this;
			DontDestroyOnLoad(gameObject);

			// 변수 초기화
			_client = new TcpClient();
			_packetQueue = new ConcurrentQueue<IPacket>();
			_replicatedCharacters = new Dictionary<int, GameObject>();

			_localPlayerID = NOT_ASSIGNED_ID;
			RoomState = RoomState.Waiting;

			JoinServer();
		}

		private void OnDestroy() {
			_client?.Dispose();
			_stream?.Dispose();
			_reader?.Dispose();
			_writer?.Dispose();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatic() {
			Instance = null;
		}
#endregion

#region Ticking Logic
		private void Update() {
			CheckPlayerInput();

			if (_packetQueue.TryDequeue(out var incomingPacket)) {
				Debug.Log($"[S -> C] {incomingPacket}");
				HandlePacket(incomingPacket);
			}
		}

		private void CheckPlayerInput() {
			var currentState = _localInputProvider.GetState();

			if (!currentState.Equals(_lastState)) {
				SendInputState(currentState);
			}

			_lastState = currentState;
		}

		private void SendInputState(InputState state) {
			if (_localPlayerID == -1) return;
			if (RoomState != RoomState.Playing) return;

			SendPacket(new PlayerInputPacket(_localPlayerID, state));
		}
  #endregion

		private void JoinServer() {
			if (_client.Connected) {
				Debug.Log("Can't join twice!");
				return;
			}

			_client.Connect(IPAddress.Loopback, 9000);
			_stream = _client.GetStream();
			_writer = new BinaryWriter(_stream);
			_reader = new BinaryReader(_stream);

			SendPacket(new ClientPingPacket());

			var listenThread = new Thread(() => {
				while (_client.Connected) {
					if (!_stream.CanRead) continue;
					if (!_stream.CanWrite) continue;
					if (!_client.Connected) {
						Debug.Log("Disconnected!");
						break;
					}

					try {
						var packetID = _reader.BaseStream.ReadByte();

						// 읽을 수 없다면(데이터가 끝났다면 리턴)
						if (packetID == -1) break;

						var packetType = (PacketType)packetID;

						// 타입에 맞는 패킷 객체 생성 후 큐에 추가
						var basePacket = packetType.CreatePacket(_reader);
						_packetQueue.Enqueue(basePacket);
					}
					catch (Exception) {
						break;
					}
				}
			});
			listenThread.Start();
		}

		private void SendPacket(IPacket packet) {
			if (!_client.Connected) {
				Debug.LogError("서버에 연결되지 않았습니다!");
				return;
			}

			Debug.Log($"[C -> S] {packet}");
			_writer.Write(packet);
		}

		private void HandlePacket(IPacket incomingPacket) {
			OnPacketReceived?.Invoke(incomingPacket);
			switch (incomingPacket) {
				case ServerAssignPlayerIdPacket packet: {
					// 전달받은 Player ID 할당
					_localPlayerID = packet.PlayerId;
					_localIdDisplay.Init(_localPlayerID);
					break;
				}

				case ServerRoomJoinPacket packet: {
					// 자신의 아이디와 같은 패킷이라면 무시
					if (_localPlayerID == packet.PlayerId) return;

					var instantiated = Instantiate(_replicatedCharacterPrefab, _spawnLocation, Quaternion.identity);
					_replicatedCharacters.Add(packet.PlayerId, instantiated);
					instantiated.GetComponent<PlayerIdDisplay>().Init(packet.PlayerId);
					break;
				}

				case ServerRoomQuitPacket packet: {
					// 자신의 아이디와 같은 패킷이라면 무시
					if (_localPlayerID == packet.PlayerId) return;

					if (_replicatedCharacters.TryGetValue(packet.PlayerId, out GameObject go)) {
						//이미 존재하는 플레이어 ID의 패킷을 받았을 때
						Destroy(go);
						_replicatedCharacters.Remove(packet.PlayerId);
					}

					break;
				}

				case ServerRoomStatusPacket packet: {
					RoomState = packet.State;

					if (RoomState == RoomState.Ending) {
						var winnerId = packet.PlayerHp.OrderByDescending(x => x.Value).First().Key;
						var isWinner = winnerId == _localPlayerID;
						_endingPanel.ShowPanel(isWinner ? "YOU WIN!" : "YOU LOSE!");
					}

					if (_localPlayerID == NOT_ASSIGNED_ID) break;

					foreach (var (id, hp) in packet.PlayerHp) {
						if (id == _localPlayerID) {
							_localHealthDisplay.SetHealth(hp);
							Debug.Log($"Setting local player health to {hp}");
						} else {
							if (_replicatedCharacters.TryGetValue(id, out GameObject go)) {
								go.GetComponent<HealthDisplay>().SetHealth(hp);
								Debug.Log($"Setting replicated player {id} health to {hp}", go);
							}
						}
					}
					break;
				}

				case PlayerInputPacket packet: {
					// 자신의 아이디와 같은 패킷이라면 무시
					if (packet.PlayerId == _localPlayerID) return;

					if (RoomState != RoomState.Playing) return;

					if (_replicatedCharacters.TryGetValue(packet.PlayerId, out GameObject go)) {
						// 이미 존재하는 플레이어 ID의 패킷을 받았을 때
						var networkInputProvider = go.GetComponent<NetworkInputProvider>();
						networkInputProvider.LastReceivedState = packet.State;
					}

					break;
				}

				case ServerArrowSpawnPacket packet: {
					if (RoomState != RoomState.Playing) return;

					var arrow = Instantiate(_arrowPrefab, new Vector3(packet.X, _arrowSpawnY, 0), Quaternion.identity);
					arrow.Init(packet.Speed);
					break;
				}
			}
		}

		public void HitByArrow() {
			if (_localPlayerID == NOT_ASSIGNED_ID) {
				Debug.LogError("아직 플레이어 ID가 할당되지 않았습니다!");
				return;
			}

			SendPacket(new ClientArrowHitPacket(_localPlayerID));
		}
	}
}
