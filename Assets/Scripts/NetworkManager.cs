using System;
using System.Collections.Concurrent;
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
		[SerializeField] private UnityInputProvider _unityInputProvider;

		public static NetworkManager Instance { get; private set; }

		public event Action<IPacket> OnPacketReceived;
		private TcpClient _client;
		private NetworkStream _stream;
		private BinaryReader _reader;
		private BinaryWriter _writer;
		private ConcurrentQueue<IPacket> _packetQueue;
		private InputState _lastState;

		private int _localPlayerID;

		private void Awake() {
			_client = new TcpClient();
			_packetQueue = new ConcurrentQueue<IPacket>();

			if (Instance != null && Instance != this) {
				Destroy(gameObject);
				return;
			}

			Instance = this;
			DontDestroyOnLoad(gameObject);

			// Not received by server
			_localPlayerID = -1;

			JoinServer();
		}

		private void Update() {
			CheckPlayerInput();

			if (_packetQueue.TryDequeue(out var incomingPacket)) {
				Debug.Log($"[S -> C] {incomingPacket}");
				HandlePacket(incomingPacket);
			}
		}

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

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatic() {
			Instance = null;
		}

		private void HandlePacket(IPacket incomingPacket) {
			OnPacketReceived?.Invoke(incomingPacket);
			switch (incomingPacket) {
				case ServerPongPacket packet: {
					_localPlayerID = packet.PlayerId;
					break;
				}

				case PlayerInputPacket packet: {

					break;
				}
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
	}
}
