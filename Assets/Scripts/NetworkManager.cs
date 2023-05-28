using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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

		// 입력 관련
		[SerializeField] private UnityInputProvider _unityInputProvider;
		private InputState _lastState;

		// 서버에서 부여받은 플레이어 ID
		private int _localPlayerID;

		// Replication 관련
		[SerializeField] private GameObject _replicatedCharacterPrefab;
		[SerializeField] private Vector2 _spawnLocation;
		private Dictionary<int, GameObject> _replicatedCharacters;

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

			// Not received by server
			_localPlayerID = -1;

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
			var currentState = _unityInputProvider.GetState();

			if (!currentState.Equals(_lastState)) {
				SendInputState(currentState);
			}

			_lastState = currentState;
		}

		private void SendInputState(InputState state) {
			if (_localPlayerID == -1) return;

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
				case ServerPongPacket packet: {
					_localPlayerID = packet.PlayerId;
					break;
				}

				case PlayerInputPacket packet: {
					if (packet.PlayerId == _localPlayerID) return;

					NetworkInputProvider networkInputProvider;


					if (_replicatedCharacters.TryGetValue(packet.PlayerId, out GameObject go)) {
						// 이미 존재하는 플레이어 ID의 패킷을 받았을 때
						networkInputProvider = go.GetComponent<NetworkInputProvider>();
					} else {
						// 새로운 플레이어 ID의 패킷을 받았을 때
						var instantiated = Instantiate(_replicatedCharacterPrefab, _spawnLocation, Quaternion.identity);
						_replicatedCharacters.Add(packet.PlayerId, instantiated);
						networkInputProvider = instantiated.GetComponent<NetworkInputProvider>();
					}

					networkInputProvider.LastReceivedState = packet.State;
					break;
				}
			}
		}
	}
}
