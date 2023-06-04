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

		[Header("네트워킹 관련")]
		[SerializeField] private string _ip = "127.0.0.1";
		[SerializeField] private int _tcpPort = 9000;
		[SerializeField] private int _udpPort = 9001;

		private TcpClient _tcpClient;
		private NetworkStream _tcpStream;
		private BinaryReader _tcpReader;
		private BinaryWriter _tcpWriter;
		private UdpClient _udpClient;

		private ConcurrentQueue<IPacket> _packetQueue;

		public RoomState RoomState { get; private set; }

		[Header("로컬 플레이어")]
		[SerializeField] private UnityInputProvider _localInputProvider;
		[SerializeField] private HealthDisplay _localHealthDisplay;
		[SerializeField] private PlayerIdDisplay _localIdDisplay;
		private InputState _lastState;
		private const int NOT_ASSIGNED_ID = -999;
		private int _localPlayerId;
		private Guid _localClientId;

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
			_tcpClient = new TcpClient();
			_udpClient = new UdpClient();

			_packetQueue = new ConcurrentQueue<IPacket>();
			_replicatedCharacters = new Dictionary<int, GameObject>();

			_localPlayerId = NOT_ASSIGNED_ID;
			RoomState = RoomState.Waiting;

			JoinServer();
		}

		private void OnDestroy() {
			_tcpReader.Close();
			_tcpWriter.Close();
			_tcpStream.Close();

			_tcpClient.Close();
			_udpClient.Close();
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
			if (_localPlayerId == -1) return;
			if (RoomState != RoomState.Playing) return;

			SendUdpPacket(new PlayerInputPacket(_localPlayerId, state));
		}
  #endregion

		private void JoinServer() {
			if (_tcpClient.Connected) {
				Debug.Log("Can't join twice!");
				return;
			}

			_tcpClient.Connect(IPAddress.Parse(_ip), _tcpPort);
			_tcpStream = _tcpClient.GetStream();
			_tcpWriter = new BinaryWriter(_tcpStream);
			_tcpReader = new BinaryReader(_tcpStream);

			_udpClient.Connect(IPAddress.Parse(_ip), _udpPort);

			_localClientId = Guid.NewGuid();

			SendPacket(new ClientPingPacket(_localClientId));

			var tcpThread = new Thread(() => {
				while (_tcpClient.Connected) {
					if (!_tcpStream.CanRead) continue;
					if (!_tcpStream.CanWrite) continue;
					if (!_tcpClient.Connected) {
						Debug.Log("Disconnected!");
						break;
					}

					try {
						var packetID = _tcpReader.BaseStream.ReadByte();

						// 읽을 수 없다면(데이터가 끝났다면 리턴)
						if (packetID == -1) break;

						var packetType = (PacketType)packetID;

						// 타입에 맞는 패킷 객체 생성 후 큐에 추가
						var basePacket = packetType.CreatePacket(_tcpReader);
						_packetQueue.Enqueue(basePacket);
					}
					catch (Exception) {
						break;
					}
				}
			});
			tcpThread.Start();


			var udpThread = new Thread(() => {
				while (true) {
					try {
						IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
						var received = _udpClient.Receive(ref remote);

						// 아무것도 받지 않았다면 무시
						if (received.Length == 0) continue;

						using var ms = new MemoryStream(received);
						using var br = new BinaryReader(ms);

						var packetID = br.ReadByte();
						var packetType = (PacketType)packetID;

						// 타입에 맞는 패킷 객체 생성 후 큐에 추가
						var basePacket = packetType.CreatePacket(_tcpReader);
						_packetQueue.Enqueue(basePacket);
					}
					catch (Exception) {
						break;
					}
				}
			});
			udpThread.Start();

		}

		private void SendPacket(IPacket packet) {
			if (!_tcpClient.Connected) {
				Debug.LogError("서버에 연결되지 않았습니다!");
				return;
			}
			Debug.Log($"[TCP] [C -> S] {packet}");

			_tcpWriter.Write(packet);
		}

		private void SendUdpPacket(IPacket packet) {
			Debug.Log($"[UDP] [C -> S] {packet}");
			using var ms = new MemoryStream();
			using var bw = new BinaryWriter(ms);

			// 패킷 직렬화
			bw.Write((byte)packet.Type);
			bw.Write(_localClientId);
			packet.Serialize(bw);

			// 전송
			var bytes = ms.ToArray();
			_udpClient.Send(bytes, bytes.Length);
		}

		private void HandlePacket(IPacket incomingPacket) {
			OnPacketReceived?.Invoke(incomingPacket);
			switch (incomingPacket) {
				case ServerAssignPlayerIdPacket packet: {
					// 전달받은 Player ID 할당
					_localPlayerId = packet.PlayerId;
					_localIdDisplay.Init(_localPlayerId);
					break;
				}

				case ServerRoomJoinPacket packet: {
					// 자신의 아이디와 같은 패킷이라면 무시
					if (_localPlayerId == packet.PlayerId) return;

					var instantiated = Instantiate(_replicatedCharacterPrefab, _spawnLocation, Quaternion.identity);
					_replicatedCharacters.Add(packet.PlayerId, instantiated);
					instantiated.GetComponent<PlayerIdDisplay>().Init(packet.PlayerId);
					break;
				}

				case ServerRoomQuitPacket packet: {
					// 자신의 아이디와 같은 패킷이라면 무시
					if (_localPlayerId == packet.PlayerId) return;

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
						var isWinner = winnerId == _localPlayerId;
						_endingPanel.ShowPanel(isWinner ? "YOU WIN!" : "YOU LOSE!");
					}

					if (_localPlayerId == NOT_ASSIGNED_ID) break;

					foreach (var (id, hp) in packet.PlayerHp) {
						if (id == _localPlayerId) {
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
					if (packet.PlayerId == _localPlayerId) return;

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
			if (_localPlayerId == NOT_ASSIGNED_ID) {
				Debug.LogError("아직 플레이어 ID가 할당되지 않았습니다!");
				return;
			}

			SendPacket(new ClientArrowHitPacket(_localPlayerId));
		}
	}
}
