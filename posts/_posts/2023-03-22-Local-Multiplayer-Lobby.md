---
title: "Local Multiplayer Lobby"
date: 2023-03-22
---

During the last one and a half years I worked on a local multiplayer game alongside some friends.
The game features fast paced action divided into very short (think 10-30 seconds on average) rounds in which moles dig around the stage and try to push each other off the map by throwing hammers or dashing into each other.

One of the parts I worked on was the Lobby system. As this was a local multiplayer/ couch-versus game we needed a way to assign controllers to different players. Thanks to a previous project I was already familiar with how to set up a very barebones version of a lobby system.

## Assets used
Odin Inspector
Rewired
Unity Atoms

## Player : Controller is a 1:1 relationship

As we used Rewired we had to make sure every controller was assigned to only one player at most. Not doing so would result in players being able to control different characters at the same time. For this we check every single controller available to the system and assigning it to the SystemPlayer object with the "removeFromOthers" flag set to true. This was initially done inside the Lobby class but we later decided that we would like to return directly to the Lobby should a game be completed instead of sending the players to the Main Menu. Doing so would have meant that they'd have to re-register themselves again, so we extracted the responsible code and only use it for the MainMenu now.

The code to achieve this is simple and consists of 2 loops inside the OnEnable method:

```csharp

    private void OnEnable()
    {
        var systemPlayer = ReInput.players.GetSystemPlayer();
        foreach (var joystick in ReInput.controllers.Joysticks)
        {
            systemPlayer.controllers.AddController(joystick, true);
        }
        foreach(var player in ReInput.players.Players)
        {
            player.isPlaying = false;
        }
    }

```

## Registering and Unregistering

Once we made sure every controller is assigned to only the SystemPlayer we could continue with the registering logic inside the Lobby class. In short: The most obvious way of handling this was to make the players press a button. This would register them as active players, assign the controller to the virtual player and spawn them in the scene.

How does the system know who's who? This is done by assigning players an ID based on who registered themselves. The first player will get the ID 0, the second one the ID 1 and so on. If player 0 now decides to unregister themself the ID given to the next player will not be 2 but the newly freed 0. This ID system went through a number of iterations, from a very primitive list to a hashset to finally an integer used like a bitmask.

```csharp

    private int _registeredPlayers = 0;

    private int GetFirstAvailableID()
    {
        int id = (int)(Mathf.Log(~_registeredPlayers & -(~_registeredPlayers), 2));
        _registeredPlayers |= (1 << (id));
        return id;
    }
    private void ReleaseID(int id)
    {
        _registeredPlayers &= ~(1 << id);
    }

```

## Lobby Zones

Each player can run around the Lobby scene so we needed a way to tell the system "Hey, I'm ready" or to unregister themselves. This is what the Lobby zones were made for. These zones simply check if a player entered their collider and then invoke an event certain observers can listen to. The Lobby has two lists of LobbyZones, one being for setting the player's status to Ready and one to unregister themselves.

## Lobby.cs

```csharp
public class Lobby : MonoBehaviour
    {
        [SerializeField]
        private int _minPlayersToStart = 2;

        [SerializeField]
        private LobbyZone[] _readyZones;

        [SerializeField]
        private LobbyZone[] _deregisteringZones;

        [FoldoutGroup("Player Input")]
        [ActionIdProperty(typeof(Category))]
        [SerializeField]
        private int _assignmentCategory;

        [FoldoutGroup("Player Input")]
        [ActionIdProperty(typeof(Action))]
        [SerializeField]
        private int _joinAction;

        [FoldoutGroup("Atoms")]
        [SerializeField]
        private IntEventReference _spawnPlayerEvent;

        [FoldoutGroup("Atoms")]
        [SerializeField]
        private BoolEventReference _allPlayersReady;

        private Player _systemPlayer;

        #region Bitflag integers
        // this is a bitflag integer, not a count of how many players are registered!
        // 4 Registered players means this will be 15 (Bits: 1111)
        private int _registeredPlayers = 0;

        // also a bitflag integer
        private int _readyPlayers = 0;

        // also a bitflag integer (for comparison)
        private int _minPlayerCount;
        #endregion

        #region Monobehaviour

        private void Awake()
        {
            if (_spawnPlayerEvent == null)
            {
                Debug.LogError($"Missing a SpawnPlayerEvent Atom Reference on {gameObject.name}!");
            }

            if(_allPlayersReady == null )
            {
                Debug.LogError($"Missing a AllPlayersReadyEvent Atom Reference on {gameObject.name}!");
            }

            _systemPlayer = ReInput.players.GetSystemPlayer();
            _minPlayerCount = 1 << (_minPlayersToStart - 1);

            RewiredMapHandler.SetMapActive(true, Category.Default);
        }

        private void OnEnable()
        {
            foreach (var zone in _readyZones)
            {
                zone.PlayerEntered.AddListener(HandleReadyZone);
            }

            foreach (var zone in _deregisteringZones)
            {
                zone.PlayerEntered.AddListener(HandleDeregisteringZone);
            }

            ReInput.ControllerConnectedEvent += OnControllerConnected;
            ReInput.ControllerDisconnectedEvent += OnControllerDisconnected;

            foreach(var player in ReInput.players.Players.Where(p => p.isPlaying))
            {
                _registeredPlayers |= (1 << (player.id));
            }

            ScoreManagement.Instance.StopMatch();
        }

        private void OnDisable()
        {
            foreach (var zone in _readyZones)
            {
                zone.PlayerEntered.RemoveListener(HandleReadyZone);
            }

            foreach (var zone in _deregisteringZones)
            {
                zone.PlayerEntered.RemoveListener(HandleDeregisteringZone);
            }

            ReInput.ControllerConnectedEvent -= OnControllerConnected;
            ReInput.ControllerDisconnectedEvent -= OnControllerDisconnected;
        }

        private void Update()
        {
            if (_systemPlayer.GetButtonDown(_joinAction))
            {
                RegisterPlayer();
            }
        }

        #region RewiredControllerConnectedEvents

        private void OnControllerConnected(ControllerStatusChangedEventArgs args)
        {
            if (args.controllerType != ControllerType.Joystick)
            {
                return;
            }

            _systemPlayer.controllers.AddController(args.controllerType, args.controllerId, true);
        }

        private void OnControllerDisconnected(ControllerStatusChangedEventArgs args)
        {
            foreach (var player in ReInput.players.Players)
            {
                if (player.controllers.ContainsController(args.controller))
                {
                    UnregisterPlayer(player);
                }
            }
        }

        #endregion

        #endregion

        #region Lobby
        private void RemoveControllersFromPlayer(Player player)
        {
            for (int i = 0; i < player.controllers.Joysticks.Count; i++)
            {
                var joystick = player.controllers.Joysticks[i];
                player.controllers.RemoveController(joystick);
            }
        }

        private void RegisterPlayer()
        {
            var player = ReInput.players.GetPlayer(GetFirstAvailableID());
            var inputSources = _systemPlayer.GetCurrentInputSources(_joinAction);
            var source = inputSources[0];
            switch (source.controllerType)
            {
                case ControllerType.Keyboard:
                    RemoveControllersFromPlayer(player);
                    player.controllers.hasKeyboard = true;
                    player.controllers.AddController(source.controller, true);
                    Debug.Log($"Assigned Keyboard to Player {player.name}");
                    break;
                case ControllerType.Joystick:
                    RemoveControllersFromPlayer(player);
                    player.controllers.AddController(source.controller, true);
                    Debug.Log($"Assigned {source.controller.name} to Player {player.name}");
                    break;
                case ControllerType.Mouse:
                case ControllerType.Custom:
                default:
                    throw new System.NotImplementedException();
            }
            player.isPlaying = true;

            _spawnPlayerEvent.Event?.Raise(player.id);
        }

        private void UnregisterPlayer(Player player)
        {
            Debug.Log($"Unregistering Player {player.id}");
            if (player.controllers.hasKeyboard)
            {
                _systemPlayer.controllers.maps.SetMapsEnabled(true, ControllerType.Keyboard, _assignmentCategory);
                player.controllers.hasKeyboard = false;
            }
            else
            {
                _systemPlayer.controllers.AddController(player.controllers.Joysticks[0], true);
            }

            player.isPlaying = false;
            ReleaseID(player.id);
        }

        private void HandleReadyZone(bool ready, Player player, GameObject playerObject)
        {
            if (ready)
            {
                _readyPlayers |= 1 << player.id;
            }
            else
            {
                _readyPlayers &= ~(1 << player.id);
            }
            Debug.Log($"Setting Player {player.id} ready status to {ready}");

            if (_allPlayersReady != null)
            {
                if (_readyPlayers == _registeredPlayers && _registeredPlayers > _minPlayerCount)
                {
                    _allPlayersReady.Event?.Raise(true);
                }
                else
                {
                    _allPlayersReady.Event?.Raise(false);
                }
            }
        }

        private void HandleDeregisteringZone(bool entered, Player player, GameObject playerObject)
        {
            Debug.Log($"Unregistering Player {player.id}");
            UnregisterPlayer(player);

            Destroy(playerObject);
        }

        private int GetFirstAvailableID()
        {
            int id = (int)(Mathf.Log(~_registeredPlayers & -(~_registeredPlayers), 2));
            _registeredPlayers |= (1 << (id));
            return id;
        }

        private void ReleaseID(int id)
        {
            _registeredPlayers &= ~(1 << id);
        }
        #endregion
    }
```