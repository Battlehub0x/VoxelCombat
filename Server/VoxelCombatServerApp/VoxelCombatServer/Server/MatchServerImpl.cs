﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading;

namespace Battlehub.VoxelCombat
{

    //This is for testing purposes only and does not needed on client -> should be moved to server
    public class PingTimer
    {
        private class PingInfo
        {
            public float m_pingTime;
            public float[] m_intervals;
            public int m_index;
            public bool m_isInitialized;
        }

        private float m_time;

        private Dictionary<Guid, PingInfo> m_pingInfo = new Dictionary<Guid, PingInfo>();

        private bool m_initialized;

        public PingTimer(Guid[] clientIds, int intervalsCount)
        {
            for (int i = 0; i < clientIds.Length; ++i)
            {
                m_pingInfo.Add(clientIds[i],
                    new PingInfo
                    {
                        m_intervals = new float[intervalsCount]
                    });
            }
        }

        public void Update(float time)
        {
            m_time = time;
        }

        public void OnClientDisconnected(Guid clientId, Action initializedCallback)
        {
            m_pingInfo.Remove(clientId);
            if (m_pingInfo.Values.All(pi => pi.m_isInitialized))
            {
                if(!m_initialized)
                {
                    m_initialized = true;
                    initializedCallback();
                }
                
            }
        }

        public void PingAll()
        {
            foreach(PingInfo pingInfo in m_pingInfo.Values)
            {
                pingInfo.m_pingTime = m_time;
            }
        }

        public void Ping(Guid clientId)
        {
            if (m_pingInfo.ContainsKey(clientId))
            {
                m_pingInfo[clientId].m_pingTime =  m_time;
            }
        }

        public RTTInfo Pong(Guid clientId, Action initializedCallback)
        {
            PingInfo pingInfo =  m_pingInfo[clientId];

            float interval = m_time - pingInfo.m_pingTime;

            pingInfo.m_intervals[pingInfo.m_index] = interval;
            pingInfo.m_index++;
            pingInfo.m_index %= pingInfo.m_intervals.Length;
            if (pingInfo.m_index == 0)
            {
                if (!pingInfo.m_isInitialized && m_pingInfo.Values.Where(pi => pi != pingInfo).All(pi => pi.m_isInitialized))
                {
                    if (!m_initialized)
                    {
                        m_initialized = true;
                        initializedCallback();
                    }

                    pingInfo.m_isInitialized = true;
                }
            }

            RTTInfo rtt = new RTTInfo();

            rtt.RTT = pingInfo.m_intervals.Average();
            rtt.RTTMax = m_pingInfo.Values.Select(pi => pi.m_intervals.Average()).Max();

            return rtt;
        }
    }

   
    public class MatchServerImpl : IMatchServer, ILoop
    {
        private void GetRidOfWarnings()
        {
            ConnectionStateChanged(new Error(), true);
        }

        private readonly ServerEventArgs<Player[], Dictionary<Guid, Dictionary<Guid, Player>>, VoxelAbilities[][], Room> m_readyToPlayAllArgs = new ServerEventArgs<Player[], Dictionary<Guid, Dictionary<Guid, Player>>, VoxelAbilities[][], Room>();
        private readonly ServerEventArgs<CommandsBundle> m_tickArgs = new ServerEventArgs<CommandsBundle>();
        private readonly ServerEventArgs<RTTInfo> m_pingArgs = new ServerEventArgs<RTTInfo>();
        private readonly ServerEventArgs<bool> m_pausedArgs = new ServerEventArgs<bool>();

        public event ServerEventHandler<ServerEventArgs<Player[], Dictionary<Guid, Dictionary<Guid, Player>>, VoxelAbilities[][], Room>> ReadyToPlayAll;
        public event ServerEventHandler<ServerEventArgs<CommandsBundle>> Tick;
        public event ServerEventHandler<ServerEventArgs<RTTInfo>> Ping;
        public event ServerEventHandler<ServerEventArgs<bool>> Paused;
        public event ServerEventHandler<bool> ConnectionStateChanged;

        private IMatchEngine m_engine;
        private IReplaySystem m_replay;

        private float m_prevTickTime;
        private long m_tick;
        private PingTimer m_pingTimer;
        private bool m_initialized;

        private Player m_neutralPlayer;
        private Guid m_serverIdentity = new Guid(ConfigurationManager.AppSettings["ServerIdentity"]);


        private readonly HashSet<Guid> m_readyToPlay;
        private readonly Dictionary<Guid, Dictionary<Guid, Player>> m_clientIdToPlayers;
        private readonly Dictionary<Guid, Player> m_players;
        private Dictionary<Guid, VoxelAbilities[]> m_abilities;
        private IBotController[] m_bots;
        private Room m_room;
        
        private string m_persistentDataPath;

        private bool enabled;

        public bool IsConnected
        {
            get { throw new NotSupportedException(); }
        }

        public MatchServerImpl(string persistentDataPath, Room room, Guid[] clientIds, Player[] players, ReplayData replay)
        {
            m_persistentDataPath = persistentDataPath;
            m_room = room;

            m_readyToPlay = new HashSet<Guid>();
            m_clientIdToPlayers = new Dictionary<Guid, Dictionary<Guid, Player>>();
            for(int i = 0; i < clientIds.Length; ++i)
            {
                Guid clientId = clientIds[i];
                Dictionary<Guid, Player> idToPlayer;
                if(!m_clientIdToPlayers.TryGetValue(clientId, out idToPlayer))
                {
                    idToPlayer = new Dictionary<Guid, Player>();
                    m_clientIdToPlayers.Add(clientId, idToPlayer);
                }

                Player player = players[i];
                idToPlayer.Add(player.Id, player);
            }

            m_players = players.ToDictionary(p => p.Id);

            //Adding neutral player to room
            m_neutralPlayer = new Player();
            m_neutralPlayer.BotType = BotType.Neutral;
            m_neutralPlayer.Name = "Neutral";
            m_neutralPlayer.Id = Guid.NewGuid();

            Init(replay);

            enabled = false; //Will be set to true when match engine will be ready
        }

        private void Init(ReplayData replay)
        {
            Dictionary<Guid, Player> idToPlayer = new Dictionary<Guid, Player>();
            idToPlayer.Add(m_neutralPlayer.Id, m_neutralPlayer);
            m_clientIdToPlayers.Add(m_serverIdentity, idToPlayer);
            m_players.Add(m_neutralPlayer.Id, m_neutralPlayer);

            if(!m_room.Players.Contains(m_neutralPlayer.Id))
            {
                m_room.Players.Insert(0, m_neutralPlayer.Id);
            }

            m_abilities = new Dictionary<Guid, VoxelAbilities[]>();
            for (int i = 0; i < m_room.Players.Count; ++i)
            {
                m_abilities.Add(m_room.Players[i], CreateTemporaryAbilies());
            }

            m_pingTimer = new PingTimer(m_clientIdToPlayers.Keys.ToArray(), 3);

            if(replay != null)
            {
                m_replay = MatchFactory.CreateReplayPlayer();
                m_replay.Load(replay);
            }
        }

        public void Destroy()
        {
            m_room.Players.Remove(m_neutralPlayer.Id);

            if (m_engine != null)
            {
                MatchFactory.DestroyMatchEngine(m_engine);
            }
        }

        private VoxelAbilities[] CreateTemporaryAbilies()
        {
            List<VoxelAbilities> abilities = new List<VoxelAbilities>();
            Array voxelTypes = Enum.GetValues(typeof(KnownVoxelTypes));
            for (int typeIndex = 0; typeIndex < voxelTypes.Length; ++typeIndex)
            {
                VoxelAbilities ability = new VoxelAbilities((int)voxelTypes.GetValue(typeIndex));
                abilities.Add(ability);
            }
            return abilities.ToArray();
        }

        public bool IsLocal(Guid clientId, Guid playerId)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Check whether status has error code
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool HasError(Error error)
        {
            return error.Code != StatusCode.OK;
        }

        public void RegisterClient(Guid clientId, ServerEventHandler callback)
        {
            throw new NotSupportedException();
        }

        public void UnregisterClient(Guid clientId, ServerEventHandler callback)
        {
            throw new NotSupportedException();
        }

        public void DownloadMapData(Guid clientId, ServerEventHandler<MapData> callback)
        {
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                Error error = new Error(StatusCode.NotRegistered);
                callback(error, null);
                return;
            }

            DownloadMapDataById(m_room.MapInfo.Id, (error, mapDataBytes) =>
            {
                MapData mapData = null;
                try
                {
                    if (!HasError(error))
                    {
                        mapData = ProtobufSerializer.Deserialize<MapData>(mapDataBytes);
                    }
                }
                catch (Exception e)
                {
                    error = new Error(StatusCode.UnhandledException) { Message = e.ToString() };
                }
                  
                callback(error, mapData);
            });
        }

        public void DownloadMapData(Guid clientId, ServerEventHandler<byte[]> callback)
        {
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                Error error = new Error(StatusCode.NotRegistered);
                callback(error, null);
                return;
            }

            DownloadMapDataById(m_room.MapInfo.Id, callback);
        }

        private void DownloadMapDataById(Guid mapId, ServerEventHandler<byte[]> callback)
        {
            byte[] mapData = new byte[0];
            Error error = new Error();

            string dataPath = m_persistentDataPath + "/Maps/";
            string filePath = dataPath + mapId + ".data";
            if (!File.Exists(filePath))
            {
                error.Code = StatusCode.NotFound;

                callback(error, mapData);
            }
            else
            {
                error.Code = StatusCode.OK;
                try
                {
                    byte[] mapDataBytes = File.ReadAllBytes(filePath);
                }
                catch (Exception e)
                {
                    error.Code = StatusCode.UnhandledException;
                    error.Message = e.Message;
                }

                callback(error, mapData);
            }
        }

        public void SetClientDisconnected(Guid clientId, Guid[] disconnectedClients, ServerEventHandler callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error);
                return;
            }

            if(clientId != m_serverIdentity)
            {
                if(disconnectedClients.Length != 1 || disconnectedClients[0] != clientId)
                {
                    error.Code = StatusCode.NotAuthorized;
                    callback(error);
                    return;
                }
            }
            
            for (int i = 0; i < disconnectedClients.Length; ++i)
            {
                Guid disconnectedClientId = disconnectedClients[i];

                Dictionary<Guid, Player> disconnectedPlayers;
                if(m_clientIdToPlayers.TryGetValue(disconnectedClientId, out disconnectedPlayers))
                {
                    foreach(Guid playerId in disconnectedPlayers.Keys)
                    {
#warning Fix Engine to handle LeaveRoom command without removing PlayerControllers. Just change colors or destroy units
                        if (m_initialized)
                        {
                            Cmd cmd = new Cmd(CmdCode.LeaveRoom, -1);
                            //m_replay.Record(playerId, cmd, m_tick);
                            //m_engine.Submit(playerId, cmd);    
                        }
                    }
                    
                    m_clientIdToPlayers.Remove(disconnectedClientId);
                    m_pingTimer.OnClientDisconnected(disconnectedClientId, () => OnPingPongCompleted(error, clientId));
                }
            }

            if(!HasError(error))
            {
                TryToInitEngine(callback);
            }
            else
            {
                callback(error);
            }

        }

        public void ReadyToPlay(Guid clientId, ServerEventHandler callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error);
                return;
            }

            if (!m_readyToPlay.Contains(clientId))
            {
                m_readyToPlay.Add(clientId);
            }

            TryToInitEngine(callback);
        }

     
        public void Submit(Guid clientId, Guid playerId, Cmd cmd, ServerEventHandler callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error);
                return;
            }
            
            if(cmd.Code == CmdCode.LeaveRoom)
            {
                error.Code = StatusCode.NotAllowed;
            }
            else
            {
                if (!m_initialized)
                {
                    error.Code = StatusCode.NotAllowed;
                    error.Message = "Match is not initialized";
                }
                else if(!enabled)
                {
                    error.Code = StatusCode.Paused;
                    error.Message = "Match is paused"; 
                }
                else
                {
                    m_replay.Record(playerId, cmd, m_tick);
                    m_engine.Submit(playerId, cmd); // if I will use RTT Ticks then it will be possible to reverse order of commands sent by client (which is BAD!)
                }
            }

            callback(error);
        }

        public void Pong(Guid clientId, ServerEventHandler callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error);
                return;
            }

            callback(error);
             
            RTTInfo rttInfo = m_pingTimer.Pong(clientId, () => OnPingPongCompleted(error, clientId));
            m_pingTimer.Ping(clientId);
            if (Ping != null)
            {
                m_pingArgs.Arg = rttInfo;
                m_pingArgs.Targets = new[] { clientId };
                Ping(error, m_pingArgs);
            }
        }

        private void OnPingPongCompleted(Error error, Guid clientId)
        {

            //Currently MatchEngine will be launched immediately and it does not care about different RTT for diffierent clients.
            //Some clients will look -50 ms to the past and some clients will look -500 ms or more to the past.
            //Is this a big deal? Don't know... Further investigation and playtest needed

            enabled = true;
            m_initialized = true;


            Player[] players;
            VoxelAbilities[][] abilities;
            if (m_room != null)
            {
                error.Code = StatusCode.OK;
                players = new Player[m_room.Players.Count];

                List<IBotController> bots = new List<IBotController>();

                //Will override or
                abilities = new VoxelAbilities[m_room.Players.Count][];
                for (int i = 0; i < m_room.Players.Count; ++i)
                {
                    Player player = m_players[m_room.Players[i]];

                    players[i] = player;
                    abilities[i] = m_abilities[m_room.Players[i]];

                    if (player.IsBot && player.BotType != BotType.Replay)
                    {
                        bots.Add(MatchFactory.CreateBotController(player, m_engine, m_engine.BotPathFinder, m_engine.BotTaskRunner));
                    }
                }

                m_bots = bots.ToArray();
            }
            else
            {
                error.Code = StatusCode.NotFound;
                error.Message = "Room not found";
                players = new Player[0];
                abilities = new VoxelAbilities[0][];
            }

            RaiseReadyToPlayAll(error, players, abilities);

        }

        private void RaiseReadyToPlayAll(Error error, Player[] players, VoxelAbilities[][] abilities)
        {
            if (ReadyToPlayAll != null)
            {
                m_readyToPlayAllArgs.Arg = players;
                m_readyToPlayAllArgs.Arg2 = m_clientIdToPlayers;
                m_readyToPlayAllArgs.Arg3 = abilities;
                m_readyToPlayAllArgs.Arg4 = m_room;
                m_readyToPlayAllArgs.Except = Guid.Empty;

                ReadyToPlayAll(error, m_readyToPlayAllArgs); 
            }
        }

        public void Pause(Guid clientId, bool pause, ServerEventHandler callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error);
                return;
            }

            if (!m_initialized)
            {
                error.Code = StatusCode.NotAllowed;
                error.Message = "Match is not initialized";
            }
            else
            {
                enabled = !pause;
            }

            if (Paused != null)
            {
                m_pausedArgs.Arg = pause;
                m_pausedArgs.Except = clientId;
                Paused(error, m_pausedArgs);
            }
            callback(error);
        }

        public void GetReplay(Guid clientId, ServerEventHandler<ReplayData> callback)
        {
            Error error = new Error(StatusCode.OK);
            if (!m_clientIdToPlayers.ContainsKey(clientId))
            {
                error.Code = StatusCode.NotRegistered;
                callback(error, null);
                return;
            }

            ReplayData replay = m_replay.Save();
            if (!m_initialized)
            {
                error.Code = StatusCode.NotAllowed;
                error.Message = "Match was not initialized";
            }
            callback(error, replay);
        }

        private void TryToInitEngine(ServerEventHandler callback)
        {
            if (m_engine == null && m_readyToPlay.Count > 0 && m_readyToPlay.Count == m_clientIdToPlayers.Count)
            {
                InitEngine(callback);
            }
            else
            {
                callback(new Error(StatusCode.OK));
            }
        }

        private void InitEngine(ServerEventHandler callback)
        {
            DownloadMapData(m_room.MapInfo.Id, (Error error, MapData mapData) =>
            {
                if (HasError(error))
                {
                    if (callback != null)
                    {
                        callback(error);
                    }
                }
                else
                {
                    MapRoot mapRoot = ProtobufSerializer.Deserialize<MapRoot>(mapData.Bytes);
                    IMatchEngine engine = MatchFactory.CreateMatchEngine(mapRoot, m_room.Players.Count);

                    Dictionary<int, VoxelAbilities>[] allAbilities = new Dictionary<int, VoxelAbilities>[m_room.Players.Count];
                    for (int i = 0; i < m_room.Players.Count; ++i)
                    {
                        allAbilities[i] = m_abilities[m_room.Players[i]].ToDictionary(a => a.Type);
                    }

                    if (m_replay == null)
                    {
                        m_replay = MatchFactory.CreateReplayRecorder();
                    }

                    //Zero is neutral
                    for (int i = 0; i < m_room.Players.Count; ++i)
                    {
                        Guid playerGuid = m_room.Players[i];
                        engine.RegisterPlayer(m_room.Players[i], i, allAbilities);
                        m_replay.RegisterPlayer(m_room.Players[i], i);
                    }
                    engine.CompletePlayerRegistration();

                    m_prevTickTime = 0;

                    m_engine = engine;

                    if (callback != null)
                    {
                        callback(error);
                    }

                    m_pingTimer.PingAll();

                    if (Ping != null)
                    {
                        m_pingArgs.Arg = new RTTInfo();
                        m_pingArgs.Except = Guid.Empty;
                        Ping(new Error(StatusCode.OK), m_pingArgs);
                    }
                }
            });
        }

        public void Update(float time)
        {
            if(m_pingTimer != null)
            {
                m_pingTimer.Update(time);
            }

            if(!enabled)
            {
                return;
            }

            m_engine.PathFinder.Update();
            m_engine.TaskRunner.Update();
            m_engine.BotPathFinder.Update();
            m_engine.BotTaskRunner.Update();

            for (int i = 0; i < m_bots.Length; ++i)
            {
                m_bots[i].Update(time);
            }

            FixedUpdate(time);
        }

        private void FixedUpdate(float time)
        {
            if(m_engine == null)
            {
                return;
            }

            float delta = time - m_prevTickTime;
            if (delta >= GameConstants.MatchEngineTick)
            {
                m_replay.Tick(m_engine, m_tick);

                CommandsBundle commands = ProtobufSerializer.DeepClone(m_engine.Tick());
                commands.Tick = m_tick;

                if (Tick != null)
                {
                    Error error = new Error(StatusCode.OK);
                    m_tickArgs.Except = Guid.Empty;
                    m_tickArgs.Arg = commands;
                    Tick(error, m_tickArgs);
                }

                m_tick++;
                m_prevTickTime = time;
            }
        }

        public void CancelRequests()
        {
            throw new NotSupportedException();
        }
    }
}
