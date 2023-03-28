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

        if (_allPlayersReady == null)
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

        foreach (var player in ReInput.players.Players.Where(p => p.isPlaying))
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